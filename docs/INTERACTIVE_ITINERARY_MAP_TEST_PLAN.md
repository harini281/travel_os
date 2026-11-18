# Interactive itinerary map — verification plan

## Data and markers
1. Activities with valid latitude/longitude render as numbered markers.
2. Null, non-finite or out-of-range coordinates are excluded from the map.
3. Marker sequence follows the saved activity order for each day.
4. Marker colours distinguish itinerary days.
5. Selecting a day filters markers and sequence connectors to that day.
6. All-days mode shows all mapped activities and fits the map bounds to visible markers.
7. One visible activity is centred at an appropriate zoom level.
8. An itinerary with no coordinates shows a helpful empty state.
9. Activities without coordinates are counted and surfaced in the map summary.
10. Marker popups display activity title, place, day/date, optional time and an OpenStreetMap link.
11. Popup content is inserted as text, not interpreted as HTML from user-supplied activity names.

## Sequence and map behaviour
12. Dotted connectors join saved activity order within each day.
13. Connector tooltips explicitly state that they represent stop sequence, not road routes.
14. Route distances and travel times are not fabricated or inferred by the map.
15. Changing the selected day clears previous markers and connectors.
16. Map bounds update when the visible set of activities changes.
17. The map invalidates its size after container resize and cleans up map resources on unmount.

## Accessibility and responsive UI
18. The map container has an accessible label.
19. Day filter is keyboard-operable.
20. Map panel and canvas render at desktop and mobile widths.
21. OpenStreetMap attribution remains visible.

## Provider and production considerations
The development map uses OpenStreetMap's public standard tile service. It is not a guaranteed production tile provider. Before launch, select a tile provider or self-hosted tile infrastructure appropriate to expected traffic and comply with its attribution, caching and usage policies. Saved coordinates may represent approximate place locations rather than venue entrances.
