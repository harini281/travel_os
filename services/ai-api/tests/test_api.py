from datetime import date
from fastapi import HTTPException
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.main import app, provider
from app.provider import PlannerProvider
from app.schemas import (
    ActivitySuggestion,
    DaySuggestion,
    ItinerarySuggestion,
    PlannerRequest,
)

client = TestClient(app)


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


def test_health_is_demo_only_by_default():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["mode"] == "demo"
    assert response.json()["modelConfigured"] is False


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
