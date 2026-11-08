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
        if (self.endDate - self.startDate).days + 1 > 31:
            raise ValueError("AI draft generation supports trips of up to 31 days")
        if any(len(item) > 60 for item in self.interests):
            raise ValueError("Each interest must be at most 60 characters")
        return self


class ActivitySuggestion(BaseModel):
    model_config = ConfigDict(extra="forbid")
    title: str = Field(min_length=2, max_length=180)
    description: str | None = Field(default=None, max_length=2000)
    placeName: str | None = Field(default=None, max_length=200)
    latitude: float | None = Field(default=None, ge=-90, le=90)
    longitude: float | None = Field(default=None, ge=-180, le=180)
    durationMinutes: int | None = Field(default=None, gt=0)
    estimatedCost: Decimal | None = Field(default=None, ge=0)
    currencyCode: str | None = Field(default=None, pattern=r"^[A-Z]{3}$")
    sortOrder: int = Field(default=1, ge=0)


class DaySuggestion(BaseModel):
    model_config = ConfigDict(extra="forbid")
    dayNumber: int = Field(ge=1)
    date: date
    theme: str = Field(min_length=2, max_length=160)
    activities: list[ActivitySuggestion] = Field(min_length=1, max_length=8)


class ItinerarySuggestion(BaseModel):
    model_config = ConfigDict(extra="forbid")
    title: str = Field(min_length=2, max_length=160)
    summary: str = Field(min_length=2, max_length=1000)
    days: list[DaySuggestion] = Field(min_length=1, max_length=31)

    @model_validator(mode="after")
    def validate_days(self):
        ordered = sorted(self.days, key=lambda day: day.dayNumber)
        if len({day.dayNumber for day in ordered}) != len(ordered):
            raise ValueError("Day numbers must be unique")
        if len({day.date for day in ordered}) != len(ordered):
            raise ValueError("Day dates must be unique")
        return self


class HealthResponse(BaseModel):
    status: Literal["ok"] = "ok"
    mode: Literal["demo", "openai"] = "demo"
    modelConfigured: bool = False


class DraftResponse(BaseModel):
    status: Literal["demo_only", "generated"] = "demo_only"
    approved: Literal[False] = False
    message: str | None = None
    provider: str = "demo"
    model: str | None = None
    itinerary: ItinerarySuggestion | None = None
    warnings: list[str] = Field(default_factory=list)


class ReceiptExtractionRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    contentType: Literal["image/jpeg", "image/png", "image/webp"]
    imageBase64: str = Field(min_length=100, max_length=12000000)
    
    @model_validator(mode="after")
    def validate_image(self):
        import base64
        try:
            decoded = base64.b64decode(self.imageBase64, validate=True)
        except Exception as exc:
            raise ValueError("imageBase64 must contain valid base64 data") from exc
        if not decoded or len(decoded) > 8 * 1024 * 1024:
            raise ValueError("Receipt image must be between 1 byte and 8 MB")
        return self


class ReceiptExtractionResult(BaseModel):
    status: Literal["demo_only", "extracted"]
    provider: Literal["demo", "openai"]
    merchantName: str | None = Field(default=None, max_length=180)
    receiptDate: date | None = None
    totalAmount: Decimal | None = Field(default=None, ge=0)
    currencyCode: str | None = Field(default=None, pattern=r"^[A-Z]{3}$")
    confidence: Decimal | None = Field(default=None, ge=0, le=1)
    warning: str
