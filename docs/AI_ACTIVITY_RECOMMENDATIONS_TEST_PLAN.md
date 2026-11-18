# Stage 39 — AI activity recommendations verification

## AI service contract
1. Demo mode returns an explicit `demo_only` result with an empty recommendation list.
2. OpenAI mode without an API key returns HTTP 503.
3. Invalid currencies, oversized context strings and malformed requests are rejected.
4. Structured provider output is validated against the recommendation schema.
5. Recommendation count is limited to 1–6.
6. Returned cost currency must match the requested trip currency; missing cost currency is filled from the request.
7. Provider timeouts, transport errors, invalid JSON and provider errors return controlled API errors.
8. Prompts prohibit claims about live opening hours, availability, verified prices, bookings, exact travel times or coordinates.

## Platform API
9. Unauthenticated requests return 401.
10. Non-members cannot read another trip's itinerary context.
11. A missing itinerary returns a conflict response.
12. A missing day returns 404.
13. Invalid day number, focus, traveller count and oversized question are rejected.
14. Context is assembled only from the requested trip and selected itinerary day.
15. Group interest and pace context comes from saved trip-member preferences.
16. Weather context uses the latest saved assessment per activity for the selected day.
17. Budget context compares expenses only in the budget currency and does not convert currencies.
18. Recommendation requests are rate limited.
19. No recommendation is persisted and no existing activity is modified.

## Workspace
20. A traveller can select an itinerary day and recommendation focus.
21. A traveller can enter an optional natural-language request.
22. Results show category, rationale, estimated duration, indicative cost, best-fit group and verification note.
23. Demo/unconfigured provider errors are shown without presenting fake suggestions.
24. The recommendation panel is responsive on mobile.
25. The existing AI full-itinerary draft workflow and apply/review flow remain available.

## Explicit limitations
- AI recommendations are not verified venue listings or live search results.
- Costs are approximate and are not supplier quotes.
- The recommendation service does not book, add, replace or edit itinerary activities.
- The traveller must verify venue details, accessibility, opening hours, prices and suitability before acting.
