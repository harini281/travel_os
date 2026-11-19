import hmac
import os
from fastapi import Depends, FastAPI, Header, HTTPException
from .schemas import PlannerRequest, HealthResponse, DraftResponse, RecommendationRequest, RecommendationResponse
from .configuration import validate_ai_provider_configuration
from .provider import PlannerProvider

validate_ai_provider_configuration()

app = FastAPI(title="TravelOS AI API", version="0.2.0")
provider = PlannerProvider()


def require_platform_service_auth(
    service_token: str | None = Header(default=None, alias="X-TravelOS-Service-Token"),
) -> None:
    expected = os.getenv("AI_SERVICE_TOKEN", "").strip()
    if not expected:
        raise HTTPException(status_code=503, detail="Internal AI service authentication is not configured.")
    if service_token is None or not hmac.compare_digest(expected, service_token):
        raise HTTPException(status_code=401, detail="Invalid internal service credentials.")


@app.get("/health", response_model=HealthResponse)
async def health():
    return HealthResponse(
        mode=provider.mode if provider.mode in {"demo", "openai"} else "demo",
        modelConfigured=provider.model_configured,
    )


@app.post("/api/v1/planner/drafts", response_model=DraftResponse)
async def create_draft(request: PlannerRequest, _: None = Depends(require_platform_service_auth)):
    return await provider.generate(request)


@app.post("/api/v1/planner/recommendations", response_model=RecommendationResponse)
async def recommend_activities(request: RecommendationRequest, _: None = Depends(require_platform_service_auth)):
    return await provider.recommend(request)


from .schemas import ReceiptExtractionRequest, ReceiptExtractionResult
from .receipt_provider import ReceiptProvider

receipt_provider = ReceiptProvider()


@app.post("/api/v1/receipts/extract", response_model=ReceiptExtractionResult)
async def extract_receipt(request: ReceiptExtractionRequest, _: None = Depends(require_platform_service_auth)):
    return await receipt_provider.extract(request)
