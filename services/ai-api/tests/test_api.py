from datetime import date
import os

TEST_SERVICE_TOKEN = "test-only-travelos-internal-service-token-32-bytes"
os.environ.setdefault("AI_SERVICE_TOKEN", TEST_SERVICE_TOKEN)
from fastapi import HTTPException
from fastapi.testclient import TestClient
from pydantic import ValidationError
import pytest

from app.main import app, provider
from app.provider import PlannerProvider
from app.schemas import (
    ActivitySuggestion,
    DaySuggestion,
    ItinerarySuggestion,
    PlannerRequest,
)

client = TestClient(app, headers={"X-TravelOS-Service-Token": os.environ["AI_SERVICE_TOKEN"]})


def recommendation_payload():
    return {
        "destination": "Lisbon",
        "activityDate": "2027-05-01",
        "currency": "EUR",
        "travellerCount": 2,
        "interests": ["food", "architecture"],
        "travelPace": "balanced",
        "dailyBudgetRemaining": 120,
        "question": "Suggest an indoor alternative because rain is forecast.",
        "focus": "weather",
        "existingActivities": ["Walk around Alfama"],
        "weatherContext": ["High precipitation probability; outdoor walking may be uncomfortable."],
        "planningContext": ["One afternoon slot is free."],
    }


def planner_payload():
    return {
        "contractVersion": "1.0",
        "destination": "Lisbon",
        "startDate": "2027-05-01",
        "endDate": "2027-05-02",
        "travellerCount": 2,
        "budgetAmount": 800,
        "currency": "EUR",
        "interests": ["food", "architecture"],
        "travelPace": "balanced",
    }


@pytest.mark.parametrize("path,payload", [
    ("/api/v1/planner/drafts", planner_payload()),
    ("/api/v1/planner/recommendations", recommendation_payload()),
    ("/api/v1/receipts/extract", {
        "contentType": "image/jpeg",
        "imageBase64": "a" * 100,
    }),
])
def test_ai_routes_reject_missing_internal_service_token(path, payload):
    unauthenticated_client = TestClient(app)
    response = unauthenticated_client.post(path, json=payload)
    assert response.status_code == 401
    assert TEST_SERVICE_TOKEN not in response.text


@pytest.mark.parametrize("path,payload", [
    ("/api/v1/planner/drafts", planner_payload()),
    ("/api/v1/planner/recommendations", recommendation_payload()),
    ("/api/v1/receipts/extract", {
        "contentType": "image/jpeg",
        "imageBase64": "a" * 100,
    }),
])
def test_ai_routes_reject_incorrect_internal_service_token(path, payload):
    response = client.post(
        path,
        json=payload,
        headers={"X-TravelOS-Service-Token": "incorrect-service-token"},
    )
    assert response.status_code == 401


@pytest.mark.parametrize("path,payload", [
    ("/api/v1/planner/drafts", planner_payload()),
    ("/api/v1/planner/recommendations", recommendation_payload()),
    ("/api/v1/receipts/extract", {
        "contentType": "image/jpeg",
        "imageBase64": "a" * 100,
    }),
])
@pytest.mark.parametrize("malformed_token", ["", "not-a-jwt", "Bearer not-a-token", "%%%"])
def test_ai_routes_reject_malformed_present_service_tokens(path, payload, malformed_token):
    response = TestClient(app).post(
        path,
        json=payload,
        headers={"X-TravelOS-Service-Token": malformed_token},
    )
    assert response.status_code == 401
    assert TEST_SERVICE_TOKEN not in response.text


def test_health_remains_public_and_does_not_disclose_provider_configuration():
    response = TestClient(app).get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
    assert "modelConfigured" not in response.text
    assert "openai" not in response.text.lower()


def test_ai_routes_fail_closed_when_internal_token_is_not_configured(monkeypatch):
    monkeypatch.delenv("AI_SERVICE_TOKEN", raising=False)
    response = client.post("/api/v1/planner/drafts", json=planner_payload())
    assert response.status_code == 503
    assert "not configured" in response.json()["detail"]


def test_health_is_minimal_and_public_by_default():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
    assert "mode" not in response.json()
    assert "modelConfigured" not in response.json()


def test_planner_returns_explicit_demo_response_by_default():
    response = client.post("/api/v1/planner/drafts", json=planner_payload())
    assert response.status_code == 200
    assert response.json()["approved"] is False
    assert response.json()["status"] == "demo_only"
    assert response.json()["itinerary"] is None


def test_openai_mode_without_key_returns_service_unavailable(monkeypatch):
    monkeypatch.setattr(provider, "mode", "openai")
    monkeypatch.setattr(provider, "api_key", "")
    response = client.post("/api/v1/planner/drafts", json=planner_payload())
    assert response.status_code == 503
    assert "OPENAI_API_KEY" in response.json()["detail"]


def test_invalid_date_range_is_rejected():
    payload = planner_payload()
    payload["startDate"] = "2027-05-04"
    payload["endDate"] = "2027-05-01"
    response = client.post("/api/v1/planner/drafts", json=payload)
    assert response.status_code == 422


def test_trips_over_31_days_are_rejected():
    payload = planner_payload()
    payload["startDate"] = "2027-05-01"
    payload["endDate"] = "2027-06-01"
    response = client.post("/api/v1/planner/drafts", json=payload)
    assert response.status_code == 422


def test_itinerary_schema_rejects_duplicate_day_numbers():
    first = DaySuggestion(
        dayNumber=1, date=date(2027, 5, 1), theme="Historic centre",
        activities=[ActivitySuggestion(title="Visit the old town")],
    )
    second = DaySuggestion(
        dayNumber=1, date=date(2027, 5, 2), theme="Riverside",
        activities=[ActivitySuggestion(title="Walk by the river")],
    )
    try:
        ItinerarySuggestion(title="Lisbon trip", summary="A short city break", days=[first, second])
        assert False, "Expected duplicate day numbers to fail validation."
    except ValidationError:
        pass


def test_provider_extracts_structured_response_text():
    body = {
        "output": [{
            "type": "message",
            "content": [{"type": "output_text", "text": '{"title":"Trip"}'}],
        }]
    }
    assert PlannerProvider._extract_output_text(body) == '{"title":"Trip"}'


def test_model_provider_rejects_incomplete_trip_days():
    request = PlannerRequest(**planner_payload())
    suggestion = ItinerarySuggestion(
        title="Lisbon trip",
        summary="A two-day city break.",
        days=[DaySuggestion(
            dayNumber=1,
            date=date(2027, 5, 1),
            theme="Historic centre",
            activities=[ActivitySuggestion(title="Visit the old town")],
        )],
    )
    try:
        PlannerProvider._validate_trip_dates(suggestion, request)
        assert False, "Expected incomplete day count to fail."
    except HTTPException as exc:
        assert "incomplete set" in exc.detail


def test_openai_provider_returns_unapproved_structured_draft(monkeypatch):
    import json
    import app.provider as provider_module

    generated = {
        "title": "Lisbon weekend",
        "summary": "A two-day city break with food and architecture.",
        "days": [
            {
                "dayNumber": 1, "date": "2027-05-01", "theme": "Historic Lisbon",
                "activities": [{
                    "title": "Explore Alfama", "description": None, "placeName": None,
                    "latitude": None, "longitude": None, "durationMinutes": 90,
                    "estimatedCost": None, "currencyCode": None, "sortOrder": 1,
                }],
            },
            {
                "dayNumber": 2, "date": "2027-05-02", "theme": "Riverside",
                "activities": [{
                    "title": "Walk along the river", "description": None, "placeName": None,
                    "latitude": None, "longitude": None, "durationMinutes": 60,
                    "estimatedCost": None, "currencyCode": None, "sortOrder": 1,
                }],
            },
        ],
    }

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {
                "output": [{
                    "type": "message",
                    "content": [{"type": "output_text", "text": json.dumps(generated)}],
                }]
            }

    class FakeClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, traceback):
            return False

        async def post(self, url, headers, json):
            assert url.endswith("/responses")
            assert json["text"]["format"]["type"] == "json_schema"
            assert json["text"]["format"]["strict"] is True
            return FakeResponse()

    monkeypatch.setattr(provider, "mode", "openai")
    monkeypatch.setattr(provider, "api_key", "test-key")
    monkeypatch.setattr(provider, "model", "test-model")
    monkeypatch.setattr(provider_module.httpx, "AsyncClient", lambda **kwargs: FakeClient())

    response = client.post("/api/v1/planner/drafts", json=planner_payload())
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "generated"
    assert body["approved"] is False
    assert body["provider"] == "openai"
    assert body["model"] == "test-model"
    assert len(body["itinerary"]["days"]) == 2


def test_receipt_extraction_is_demo_only_by_default():
    from app.main import receipt_provider
    response = client.post("/api/v1/receipts/extract", json={
        "contentType": "image/jpeg",
        "imageBase64": "a" * 100,
    })
    assert response.status_code == 200
    assert response.json()["status"] == "demo_only"
    assert response.json()["merchantName"] is None


def test_receipt_extraction_rejects_invalid_base64():
    response = client.post("/api/v1/receipts/extract", json={
        "contentType": "image/png",
        "imageBase64": "not-base64" * 20,
    })
    assert response.status_code == 422


def test_receipt_provider_openai_mode_requires_key(monkeypatch):
    from app.main import receipt_provider
    monkeypatch.setattr(receipt_provider, "mode", "openai")
    monkeypatch.setattr(receipt_provider, "api_key", "")
    response = client.post("/api/v1/receipts/extract", json={
        "contentType": "image/jpeg",
        "imageBase64": "a" * 100,
    })
    assert response.status_code == 503
    assert "OPENAI_API_KEY" in response.json()["detail"]


def test_recommendations_are_explicitly_demo_only_without_ai_provider():
    response = client.post("/api/v1/planner/recommendations", json=recommendation_payload())
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "demo_only"
    assert body["recommendations"] == []
    assert "demo mode" in body["summary"]


def test_recommendation_request_rejects_invalid_currency_and_long_context():
    payload = recommendation_payload()
    payload["currency"] = "EURO"
    assert client.post("/api/v1/planner/recommendations", json=payload).status_code == 422
    payload = recommendation_payload()
    payload["existingActivities"] = ["x" * 241]
    assert client.post("/api/v1/planner/recommendations", json=payload).status_code == 422


def test_openai_recommendations_return_structured_unapplied_suggestions(monkeypatch):
    import json
    import app.provider as provider_module

    generated = {
        "summary": "A few indoor options that fit the group's interests and remaining budget.",
        "recommendations": [{
            "title": "Visit a local tile museum",
            "category": "weather_alternative",
            "reason": "An indoor cultural activity that suits the architecture interest.",
            "suggestedDurationMinutes": 90,
            "estimatedCost": 12,
            "currencyCode": "EUR",
            "bestFor": "Culture and architecture",
            "verificationNote": "Check opening hours and ticket prices before visiting."
        }]
    }

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {"output": [{
                "type": "message",
                "content": [{"type": "output_text", "text": json.dumps(generated)}]
            }]}

    class FakeClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, traceback):
            return False

        async def post(self, url, headers, json):
            assert url.endswith("/responses")
            assert json["text"]["format"]["name"] == "travelos_activity_recommendations"
            assert json["text"]["format"]["strict"] is True
            return FakeResponse()

    monkeypatch.setattr(provider, "mode", "openai")
    monkeypatch.setattr(provider, "api_key", "test-key")
    monkeypatch.setattr(provider, "model", "test-model")
    monkeypatch.setattr(provider_module.httpx, "AsyncClient", lambda **kwargs: FakeClient())

    response = client.post("/api/v1/planner/recommendations", json=recommendation_payload())
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "generated"
    assert body["provider"] == "openai"
    assert body["recommendations"][0]["category"] == "weather_alternative"
    assert body["recommendations"][0]["currencyCode"] == "EUR"
    assert "Nothing is added" in body["warnings"][1]


def test_recommendation_provider_requires_api_key(monkeypatch):
    monkeypatch.setattr(provider, "mode", "openai")
    monkeypatch.setattr(provider, "api_key", "")
    response = client.post("/api/v1/planner/recommendations", json=recommendation_payload())
    assert response.status_code == 503



def test_recommendation_request_defaults_traveller_count():
    from app.schemas import RecommendationRequest
    request = RecommendationRequest(
        destination="Lisbon",
        activityDate="2027-05-01",
        currency="EUR",
    )
    assert request.travellerCount == 1
    assert request.existingActivities == []
    assert request.weatherContext == []


def test_recommendation_request_rejects_oversized_context():
    from app.schemas import RecommendationRequest
    with pytest.raises(ValidationError):
        RecommendationRequest(
            destination="Lisbon",
            activityDate="2027-05-01",
            currency="EUR",
            existingActivities=["x" * 241],
        )


def test_ai_provider_configuration_accepts_demo_mode():
    from app.configuration import validate_ai_provider_configuration

    validate_ai_provider_configuration({})


@pytest.mark.parametrize("variable,value", [
    ("AI_PROVIDER", "unsupported"),
    ("AI_RECEIPT_PROVIDER", "third-party-guess"),
])
def test_ai_provider_configuration_rejects_unknown_modes(variable, value):
    from app.configuration import validate_ai_provider_configuration

    with pytest.raises(RuntimeError, match="must be either"):
        validate_ai_provider_configuration({variable: value})


@pytest.mark.parametrize("planner_mode,receipt_mode", [
    ("openai", "demo"),
    ("demo", "openai"),
])
def test_ai_provider_configuration_requires_key_for_live_modes(planner_mode, receipt_mode):
    from app.configuration import validate_ai_provider_configuration

    with pytest.raises(RuntimeError, match="OPENAI_API_KEY"):
        validate_ai_provider_configuration({
            "AI_PROVIDER": planner_mode,
            "AI_RECEIPT_PROVIDER": receipt_mode,
            "OPENAI_API_KEY": "",
        })


def test_ai_provider_configuration_accepts_live_provider_with_https_endpoint():
    from app.configuration import validate_ai_provider_configuration

    validate_ai_provider_configuration({
        "AI_PROVIDER": "openai",
        "AI_RECEIPT_PROVIDER": "demo",
        "OPENAI_API_KEY": "test-only-not-a-real-key",
        "OPENAI_MODEL": "gpt-4.1-mini",
        "OPENAI_BASE_URL": "https://api.openai.com/v1",
    })


@pytest.mark.parametrize("base_url", [
    "http://api.openai.com/v1",
    "https://user:password@example.com/v1",
    "https://example.com/v1?token=secret",
    "https://example.com/v1#fragment",
])
def test_ai_provider_configuration_rejects_unsafe_live_endpoint(base_url):
    from app.configuration import validate_ai_provider_configuration

    with pytest.raises(RuntimeError, match="OPENAI_BASE_URL"):
        validate_ai_provider_configuration({
            "AI_PROVIDER": "openai",
            "OPENAI_API_KEY": "test-only-not-a-real-key",
            "OPENAI_BASE_URL": base_url,
        })
