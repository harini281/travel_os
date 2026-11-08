from datetime import date
from decimal import Decimal
from typing import Literal
from pydantic import BaseModel, ConfigDict, Field, model_validator

class PlannerRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    contractVersion: Literal["1.0"] = "1.0"
    destination: str = Field(min_length=2, max_length=160)
    startDate: date
    endDate: date
    travellerCount: int = Field(ge=1, le=30)
    budgetAmount: Decimal | None = Field(default=None, ge=0)
    currency: str = Field(pattern=r"^[A-Z]{3}$")
    interests: list[str] = Field(default_factory=list, max_length=20)
    travelPace: Literal["relaxed", "balanced", "fast"] = "balanced"

    @model_validator(mode="after")
    def validate_dates(self):
        if self.endDate < self.startDate:
            raise ValueError("endDate must be on or after startDate")
        return self

class HealthResponse(BaseModel):
    status: Literal["ok"] = "ok"
    mode: Literal["demo"] = "demo"
    modelConfigured: bool = False

class DraftResponse(BaseModel):
    status: Literal["demo_only"] = "demo_only"
    approved: Literal[False] = False
    message: str
