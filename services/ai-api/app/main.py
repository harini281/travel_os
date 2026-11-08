from fastapi import FastAPI
from .schemas import PlannerRequest, HealthResponse, DraftResponse
from .provider import PlannerProvider

app = FastAPI(title="TravelOS AI API", version="0.2.0")
provider = PlannerProvider()


@app.get("/health", response_model=HealthResponse)
async def health():
    return HealthResponse(
        mode=provider.mode if provider.mode in {"demo", "openai"} else "demo",
        modelConfigured=provider.model_configured,
    )


@app.post("/api/v1/planner/drafts", response_model=DraftResponse)
async def create_draft(request: PlannerRequest):
    return await provider.generate(request)


from .schemas import ReceiptExtractionRequest, ReceiptExtractionResult
from .receipt_provider import ReceiptProvider

receipt_provider = ReceiptProvider()


@app.post("/api/v1/receipts/extract", response_model=ReceiptExtractionResult)
async def extract_receipt(request: ReceiptExtractionRequest):
    return await receipt_provider.extract(request)
