from fastapi.testclient import TestClient
from app.main import app

client = TestClient(app)

def test_health_is_demo_only():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["mode"] == "demo"
    assert response.json()["modelConfigured"] is False

def test_planner_returns_unapproved_demo_response():
    response = client.post("/api/v1/planner/drafts", json={
        "contractVersion": "1.0",
        "destination": "Lisbon",
        "startDate": "2027-05-01",
        "endDate": "2027-05-04",
        "travellerCount": 2,
        "budgetAmount": 800,
        "currency": "EUR",
        "interests": ["food", "architecture"],
        "travelPace": "balanced"
    })
    assert response.status_code == 200
    assert response.json()["approved"] is False
    assert response.json()["status"] == "demo_only"

def test_invalid_date_range_is_rejected():
    response = client.post("/api/v1/planner/drafts", json={
        "contractVersion": "1.0",
        "destination": "Lisbon",
        "startDate": "2027-05-04",
        "endDate": "2027-05-01",
        "travellerCount": 2,
        "currency": "EUR"
    })
    assert response.status_code == 422
