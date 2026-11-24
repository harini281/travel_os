import { useEffect, useMemo, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";

export type ItineraryMapActivity = {
  id: string;
  title: string;
  placeName: string | null;
  latitude: number | null;
  longitude: number | null;
  startTime: string | null;
  endTime: string | null;
};

export type ItineraryMapDay = {
  id: string;
  dayNumber: number;
  date: string;
  activities: ItineraryMapActivity[];
};

type Props = { days: ItineraryMapDay[] };

const DAY_COLORS = ["#287b62", "#b47b43", "#527bb5", "#9368a8", "#b45c58", "#5d8995", "#7d8542"];

export default function ItineraryMap({ days }: Props) {
  const mapElement = useRef<HTMLDivElement | null>(null);
  const mapInstance = useRef<L.Map | null>(null);
  const markerLayer = useRef<L.LayerGroup | null>(null);
  const routeLayer = useRef<L.LayerGroup | null>(null);
  const [routeStatus, setRouteStatus] = useState<"idle" | "loading" | "ready" | "partial">("idle");
  const [selectedDay, setSelectedDay] = useState("all");

  const locatedActivities = useMemo(
    () => days.flatMap(day => day.activities
      .filter(activity => Number.isFinite(activity.latitude) && Number.isFinite(activity.longitude)
        && activity.latitude !== null && activity.longitude !== null
        && Math.abs(activity.latitude) <= 90 && Math.abs(activity.longitude) <= 180)
      .map(activity => ({ ...activity, dayId: day.id, dayNumber: day.dayNumber, date: day.date }))),
    [days]
  );

  const visibleActivities = useMemo(
    () => selectedDay === "all" ? locatedActivities : locatedActivities.filter(item => item.dayId === selectedDay),
    [locatedActivities, selectedDay]
  );

  useEffect(() => {
    if (!mapElement.current || mapInstance.current) return;
    const map = L.map(mapElement.current, { zoomControl: true, scrollWheelZoom: false });
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
    }).addTo(map);
    markerLayer.current = L.layerGroup().addTo(map);
    routeLayer.current = L.layerGroup().addTo(map);
    map.setView([20, 0], 2);
    mapInstance.current = map;

    const resizeObserver = new ResizeObserver(() => map.invalidateSize());
    resizeObserver.observe(mapElement.current);
    return () => {
      resizeObserver.disconnect();
      map.remove();
      mapInstance.current = null;
      markerLayer.current = null;
      routeLayer.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapInstance.current;
    const layer = markerLayer.current;
    const routes = routeLayer.current;
    if (!map || !layer || !routes) return;
    layer.clearLayers();
    routes.clearLayers();

    let cancelled = false;
    const byDay = new Map<string, typeof visibleActivities>();
    visibleActivities.forEach(activity => {
      const existing = byDay.get(activity.dayId) ?? [];
      existing.push(activity);
      byDay.set(activity.dayId, existing);
    });

    const dayRoutes = [...byDay.values()].filter(activities => activities.length > 1);
    setRouteStatus(dayRoutes.length ? "loading" : "idle");

    const drawFallback = (activities: typeof visibleActivities) => {
      const color = DAY_COLORS[(activities[0].dayNumber - 1) % DAY_COLORS.length];
      L.polyline(activities.map(item => [item.latitude!, item.longitude!] as [number, number]), {
        color, weight: 2, opacity: 0.45, dashArray: "5 7"
      }).addTo(routes).bindTooltip("Straight-line stop sequence · road route unavailable");
    };

    const loadRoutes = async () => {
      let successfulRoutes = 0;
      for (const activities of dayRoutes) {
        if (cancelled) return;
        const color = DAY_COLORS[(activities[0].dayNumber - 1) % DAY_COLORS.length];
        const coordinates = activities.map(item => `${item.longitude},${item.latitude}`).join(";");
        try {
          const response = await fetch(
            `https://router.project-osrm.org/route/v1/driving/${coordinates}?overview=full&geometries=geojson&steps=false`,
            { signal: AbortSignal.timeout(12000) }
          );
          if (!response.ok) throw new Error(`Routing service returned ${response.status}`);
          const result = await response.json() as {
            code?: string;
            routes?: Array<{ geometry?: { coordinates?: [number, number][] } }>;
          };
          const geometry = result.routes?.[0]?.geometry?.coordinates;
          if (result.code !== "Ok" || !geometry?.length) throw new Error("No road route returned");
          if (cancelled) return;
          L.geoJSON({ type: "LineString", coordinates: geometry }, {
            style: { color, weight: 4, opacity: 0.82 }
          }).addTo(routes).bindTooltip(`Day ${activities[0].dayNumber} · estimated driving route`);
          successfulRoutes += 1;
        } catch {
          if (cancelled) return;
          drawFallback(activities);
        }
      }
      if (!cancelled) setRouteStatus(successfulRoutes === dayRoutes.length ? "ready" : "partial");
    };
    void loadRoutes();

    visibleActivities.forEach(activity => {
      const color = DAY_COLORS[(activity.dayNumber - 1) % DAY_COLORS.length];
      const daySequence = (byDay.get(activity.dayId) ?? []).findIndex(item => item.id === activity.id) + 1;
      const marker = L.marker([activity.latitude!, activity.longitude!], {
        icon: L.divIcon({
          className: "itinerary-map-marker",
          html: `<span style="background:${color}">${daySequence}</span>`,
          iconSize: [28, 28],
          iconAnchor: [14, 14]
        })
      });
      const popup = L.DomUtil.create("div", "itinerary-map-popup");
      const heading = L.DomUtil.create("strong", "", popup);
      heading.textContent = activity.title;
      const place = L.DomUtil.create("div", "itinerary-map-popup-place", popup);
      place.textContent = activity.placeName || "Saved activity location";
      const details = L.DomUtil.create("small", "", popup);
      details.textContent = `Day ${activity.dayNumber} · ${activity.date}${activity.startTime && activity.endTime ? ` · ${activity.startTime.slice(0,5)}–${activity.endTime.slice(0,5)}` : ""}`;
      const link = L.DomUtil.create("a", "itinerary-map-popup-link", popup);
      link.href = `https://www.openstreetmap.org/?mlat=${activity.latitude}&mlon=${activity.longitude}#map=17/${activity.latitude}/${activity.longitude}`;
      link.target = "_blank";
      link.rel = "noopener noreferrer";
      link.textContent = "Open in OpenStreetMap ↗";
      marker.bindPopup(popup);
      marker.bindTooltip(activity.title, { direction: "top", offset: [0, -8] });
      marker.addTo(layer);
    });

    if (visibleActivities.length === 1) {
      map.setView([visibleActivities[0].latitude!, visibleActivities[0].longitude!], 14);
    } else if (visibleActivities.length > 1) {
      const bounds = L.latLngBounds(visibleActivities.map(item => [item.latitude!, item.longitude!] as [number, number]));
      map.fitBounds(bounds, { padding: [36, 36], maxZoom: 14 });
    }
    return () => { cancelled = true; };
  }, [visibleActivities]);

  const withoutCoordinates = days.reduce(
    (count, day) => count + day.activities.filter(activity =>
      activity.latitude === null || activity.longitude === null
    ).length, 0
  );

  return (
    <section className="itinerary-map-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">STAGE 35 · VISUAL TRIP EXPLORER</p>
          <h2>Explore your itinerary map</h2>
          <small>View saved activity locations by day</small>
        </div>
        <label className="itinerary-map-day-filter">
          <span>Show</span>
          <select value={selectedDay} onChange={event => setSelectedDay(event.target.value)}>
            <option value="all">All days</option>
            {days.map(day => <option key={day.id} value={day.id}>Day {day.dayNumber}</option>)}
          </select>
        </label>
      </div>
      <div className="itinerary-map-summary">
        <span><i className="itinerary-map-dot" />{visibleActivities.length} mapped {visibleActivities.length === 1 ? "activity" : "activities"}</span>
        {withoutCoordinates > 0 && <span>{withoutCoordinates} without saved coordinates</span>}
        {routeStatus === "loading" && <span>Calculating driving routes…</span>}
        {routeStatus === "ready" && <span>Road routes loaded</span>}
        {routeStatus === "partial" && <span>Some routes unavailable; dashed lines show stop order only</span>}
      </div>
      {locatedActivities.length === 0
        ? <div className="itinerary-map-empty">Add map coordinates to itinerary activities to see them here.</div>
        : <div ref={mapElement} className="itinerary-map-canvas" role="application" aria-label="Interactive itinerary map" />}
      <p className="itinerary-map-footnote">Pins show saved activity coordinates, not verified venue entrances. Driving routes are estimates from the public OSRM routing service; dashed lines are straight-line fallbacks. This is not live navigation. Map data © OpenStreetMap contributors.</p>
    </section>
  );
}
