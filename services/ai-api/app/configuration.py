from __future__ import annotations

import os
from urllib.parse import urlsplit
from collections.abc import Mapping


_ALLOWED_MODES = {"demo", "openai"}


def validate_ai_provider_configuration(
    environment: Mapping[str, str] | None = None,
) -> None:
    """Fail startup for unsupported provider modes or incomplete live-provider config.

    The OpenAI-compatible base URL is required to use HTTPS whenever a live
    provider is enabled. API keys are never included in exception messages.
    """
    values = environment if environment is not None else os.environ
    planner_mode = values.get("AI_PROVIDER", "demo").strip().lower()
    receipt_mode = values.get("AI_RECEIPT_PROVIDER", "demo").strip().lower()
    api_key = values.get("OPENAI_API_KEY", "").strip()

    if planner_mode not in _ALLOWED_MODES:
        raise RuntimeError("AI_PROVIDER must be either 'demo' or 'openai'.")
    if receipt_mode not in _ALLOWED_MODES:
        raise RuntimeError("AI_RECEIPT_PROVIDER must be either 'demo' or 'openai'.")

    live_provider_enabled = planner_mode == "openai" or receipt_mode == "openai"
    if not live_provider_enabled:
        return

    if not api_key:
        raise RuntimeError(
            "OPENAI_API_KEY must be configured when either AI provider is set to 'openai'."
        )

    model = values.get("OPENAI_MODEL", "gpt-4.1-mini").strip()
    if not model:
        raise RuntimeError("OPENAI_MODEL must not be empty when a live AI provider is enabled.")

    base_url = values.get("OPENAI_BASE_URL", "https://api.openai.com/v1").strip()
    parsed = urlsplit(base_url)
    if (
        parsed.scheme.lower() != "https"
        or not parsed.hostname
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
    ):
        raise RuntimeError(
            "OPENAI_BASE_URL must be an HTTPS URL without embedded credentials, query or fragment."
        )
