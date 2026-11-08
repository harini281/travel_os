from fastapi import FastAPI
from .schemas import PlannerRequest, HealthResponse, DraftResponse
from .provider import PlannerProvider

app = FastAPI(title="TravelOS AI API", version="0.1.0")
provider = PlannerProvider()

@app.get("/health", response_model=HealthResponse)
async def health():
    return HealthResponse()

@app.post("/api/v1/planner/drafts", response_model=DraftResponse)
async def create_draft(request: PlannerRequest):
    return await provider.generate(request)
