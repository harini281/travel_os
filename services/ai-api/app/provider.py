from .schemas import PlannerRequest, DraftResponse

class PlannerProvider:
    async def generate(self, request: PlannerRequest) -> DraftResponse:
        # Deterministic placeholder only. No model provider is configured in Stage 1.
        return DraftResponse(
            message=(
                f"Demo adapter received a planning request for {request.destination}. "
                "No AI-generated itinerary has been created. Configure and test a real "
                "provider in a later stage."
            )
        )
