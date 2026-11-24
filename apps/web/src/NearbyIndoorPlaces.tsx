import { useEffect, useMemo, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";

type IndoorActivity = {
  id: string; title: string; placeName: string | null;
  latitude: number | null; longitude: number | null;
  durationMinutes: number | null; startTime: string | null; endTime: string | null;
  estimatedCost: number | null; currencyCode: string | null; sortOrder: number;
};
type IndoorDay = { id: string; dayNumber: number; date: string; activities: IndoorActivity[] };
type Place = { id: number; name: string; type: string; lat: number; lon: number; distanceKm: number; driveDistanceKm: number | null; driveDurationMinutes: number | null; tags: Record<string, string> };
type WeatherRisk = { activityId: string; activityTitle: string; activityDate: string; severity: "low" | "moderate" | "high"; summary: string; recommendation: string; precipitationProbabilityMax: number | null; precipitationSumMm: number | null; windSpeedMaxKmh: number | null };
type Props = { days: IndoorDay[]; tripId: string; accessToken: string | null; canAddToItinerary: boolean; weatherAssessments: WeatherRisk[]; onAdded: () => Promise<void> | void };

function distanceKm(aLat: number, aLon: number, bLat: number, bLon: number) {
  const radians = (degrees: number) => degrees * Math.PI / 180;
  const dLat = radians(bLat - aLat), dLon = radians(bLon - aLon);
  const value = Math.sin(dLat / 2) ** 2 +
    Math.cos(radians(aLat)) * Math.cos(radians(bLat)) * Math.sin(dLon / 2) ** 2;
  return 6371 * 2 * Math.atan2(Math.sqrt(value), Math.sqrt(1 - value));
}


type OpeningStatus = "open" | "closed" | "unknown";

function openingStatus(hours: string | undefined, date: string | undefined, time: string | null | undefined): OpeningStatus {
  if (!hours || !date || !time) return "unknown";
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
  const clock = /^(\d{1,2}):(\d{2})/.exec(time);
  if (!match || !clock) return "unknown";
  const weekday = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))).getUTCDay();
  const dayNames = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
  const minute = Number(clock[1]) * 60 + Number(clock[2]);
  if (!Number.isFinite(minute) || minute < 0 || minute >= 1440) return "unknown";
  const normalized = hours.trim();
  if (normalized === "24/7") return "open";
  if (/[;]|PH|SH|sunrise|sunset|\+|\[|\]/i.test(normalized)) return "unknown";
  const parts = normalized.split(/\s+/);
  const dayPart = parts.length > 1 ? parts[0] : "Mo-Su";
  const timePart = parts.length > 1 ? parts.slice(1).join(" ") : parts[0];
  if (timePart === "off" || timePart === "closed") return "closed";
  const days = new Set<number>();
  for (const segment of dayPart.split(",")) {
    const range = segment.split("-");
    const start = dayNames.indexOf(range[0]);
    const end = dayNames.indexOf(range[1] ?? range[0]);
    if (start < 0 || end < 0) return "unknown";
    let cursor = start;
    for (let count = 0; count < 7; count++) {
      days.add(cursor);
      if (cursor === end) break;
      cursor = (cursor + 1) % 7;
    }
  }
  if (!days.has(weekday)) return "closed";
  if (!/^\d{1,2}:\d{2}-\d{1,2}:\d{2}(,\d{1,2}:\d{2}-\d{1,2}:\d{2})*$/.test(timePart)) return "unknown";
  const intervals = timePart.split(",").map(range => range.split("-").map(value => {
    const [hour, mins] = value.split(":").map(Number);
    return hour * 60 + mins;
  }));
  return intervals.some(([start, end]) => minute >= start && minute < end) ? "open" : "closed";
}

function placeType(tags: Record<string, string>) {
  return tags.amenity || tags.tourism || tags.leisure || tags.shop || "indoor attraction";
}

export default function NearbyIndoorPlaces({ days, tripId, accessToken, canAddToItinerary, weatherAssessments, onAdded }: Props) {
  const [selectedDayId, setSelectedDayId] = useState(days[0]?.id ?? "");
  const [places, setPlaces] = useState<Place[]>([]);
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [message, setMessage] = useState("");
  const [selectedPlaceId, setSelectedPlaceId] = useState<number | null>(null);
  const [routeStatus, setRouteStatus] = useState<"idle" | "loading" | "ready" | "unavailable">("idle");
  const [routeSummary, setRouteSummary] = useState<{ distanceKm: number; durationMinutes: number } | null>(null);
  const [addingPlaceId, setAddingPlaceId] = useState<number | null>(null);
  const [pendingAddPlaceId, setPendingAddPlaceId] = useState<number | null>(null);
  const [replacementActivityId, setReplacementActivityId] = useState<string>("");
  const [replacementMode, setReplacementMode] = useState(false);
  const [addMessage, setAddMessage] = useState("");
  const mapElement = useRef<HTMLDivElement | null>(null);
  const mapInstance = useRef<L.Map | null>(null);
  const markerLayer = useRef<L.LayerGroup | null>(null);
  const routeLayer = useRef<L.LayerGroup | null>(null);
  const selectedDay = useMemo(() => days.find(day => day.id === selectedDayId) ?? days[0] ?? null, [days, selectedDayId]);
  const weatherAffectedActivities = useMemo(() => (selectedDay?.activities ?? []).flatMap(activity => {
    const assessment = weatherAssessments.find(item => item.activityId === activity.id && item.severity !== "low");
    return assessment ? [{ activity, assessment }] : [];
  }), [selectedDay, weatherAssessments]);
  const anchor = useMemo(() => selectedDay?.activities.find(activity =>
    activity.latitude !== null && activity.longitude !== null &&
    Number.isFinite(activity.latitude) && Number.isFinite(activity.longitude)
  ) ?? null, [selectedDay]);


  useEffect(() => {
    if (!mapElement.current || mapInstance.current) return;
    const map = L.map(mapElement.current, { scrollWheelZoom: false }).setView([6.9271, 79.8612], 13);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      maxZoom: 19
    }).addTo(map);
    markerLayer.current = L.layerGroup().addTo(map);
    routeLayer.current = L.layerGroup().addTo(map);
    mapInstance.current = map;
    const resize = () => map.invalidateSize();
    window.addEventListener("resize", resize);
    return () => {
      window.removeEventListener("resize", resize);
      map.remove();
      mapInstance.current = null;
      markerLayer.current = null;
      routeLayer.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapInstance.current, layer = markerLayer.current, routes = routeLayer.current;
    if (!map || !layer || !routes || status !== "ready") return;
    layer.clearLayers();
    routes.clearLayers();
    if (anchor?.latitude != null && anchor.longitude != null) {
      L.marker([anchor.latitude, anchor.longitude], {
        icon: L.divIcon({ className: "nearby-map-origin", html: "<span>★</span>", iconSize: [30,30], iconAnchor: [15,15] })
      }).bindTooltip("Your planned activity").addTo(layer);
    }
    places.forEach(place => {
      const marker = L.marker([place.lat, place.lon], {
        icon: L.divIcon({ className: "nearby-map-marker", html: "<span>•</span>", iconSize: [28,28], iconAnchor: [14,14] })
      });
      marker.bindTooltip(place.name);
      marker.on("click", () => setSelectedPlaceId(place.id));
      marker.addTo(layer);
    });
    const coordinates: [number,number][] = [
      ...(anchor?.latitude != null && anchor.longitude != null ? [[anchor.latitude, anchor.longitude] as [number,number]] : []),
      ...places.map(place => [place.lat, place.lon] as [number,number])
    ];
    const bounds = L.latLngBounds(coordinates);
    if (bounds.isValid()) map.fitBounds(bounds, { padding: [30,30], maxZoom: 15 });
  }, [places, anchor, status]);

  useEffect(() => {
    const routes = routeLayer.current;
    const selected = places.find(place => place.id === selectedPlaceId);
    if (!routes) return;
    routes.clearLayers();
    if (!selected || anchor?.latitude == null || anchor.longitude == null) {
      setRouteSummary(null);
      setRouteStatus("idle");
      return;
    }
    const controller = new AbortController();
    setRouteSummary(null);
    setRouteStatus("loading");
    const coordinates = `${anchor.longitude},${anchor.latitude};${selected.lon},${selected.lat}`;
    fetch(`https://router.project-osrm.org/route/v1/driving/${coordinates}?overview=full&geometries=geojson&steps=false`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error("Driving route unavailable");
        return await response.json() as { code?: string; routes?: Array<{ distance?: number; duration?: number; geometry?: { coordinates?: [number,number][] } }> };
      })
      .then(result => {
        const geometry = result.routes?.[0]?.geometry?.coordinates;
        if (result.code !== "Ok" || !geometry?.length) throw new Error("No driving route returned");
        if (controller.signal.aborted) return;
        L.polyline(geometry.map(([lon,lat]) => [lat,lon] as [number,number]), { color: "#b47b43", weight: 5, opacity: .85 }).addTo(routes);
        const route = result.routes?.[0];
        if (route?.distance != null && route.duration != null) setRouteSummary({ distanceKm: route.distance / 1000, durationMinutes: route.duration / 60 });
        setRouteStatus("ready");
      })
      .catch(() => { if (!controller.signal.aborted) { setRouteSummary(null); setRouteStatus("unavailable"); } });
    return () => controller.abort();
  }, [selectedPlaceId, places, anchor]);

  useEffect(() => {
    if (!selectedDay) { setStatus("idle"); setPlaces([]); return; }
    if (!anchor || anchor.latitude === null || anchor.longitude === null) {
      setStatus("error");
      setMessage("Add a saved map location to an activity on this day before searching nearby places.");
      setPlaces([]);
      return;
    }
    const controller = new AbortController();
    setStatus("loading");
    setSelectedPlaceId(null);
    setRouteStatus("idle");
    setMessage("");
    setPlaces([]);
    const query = `[out:json][timeout:20];(node["amenity"~"^(museum|arts_centre|library|cinema|theatre|planetarium|aquarium|community_centre)$"](around:3000,${anchor.latitude},${anchor.longitude});way["amenity"~"^(museum|arts_centre|library|cinema|theatre|planetarium|aquarium|community_centre)$"](around:3000,${anchor.latitude},${anchor.longitude});relation["amenity"~"^(museum|arts_centre|library|cinema|theatre|planetarium|aquarium|community_centre)$"](around:3000,${anchor.latitude},${anchor.longitude});node["tourism"~"^(museum|gallery|aquarium)$"](around:3000,${anchor.latitude},${anchor.longitude});way["tourism"~"^(museum|gallery|aquarium)$"](around:3000,${anchor.latitude},${anchor.longitude});node["leisure"~"^(sports_centre|fitness_centre|escape_game)$"](around:3000,${anchor.latitude},${anchor.longitude});way["leisure"~"^(sports_centre|fitness_centre|escape_game)$"](around:3000,${anchor.latitude},${anchor.longitude}););out center tags;`;
    fetch("https://overpass-api.de/api/interpreter", {
      method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" },
      body: new URLSearchParams({ data: query }).toString(), signal: controller.signal
    }).then(async response => {
      if (!response.ok) throw new Error(`Place search service returned ${response.status}.`);
      return await response.json() as { elements?: Array<{ type: string; id: number; lat?: number; lon?: number; center?: { lat: number; lon: number }; tags?: Record<string, string> }> };
    }).then(result => {
      const seen = new Set<string>();
      const found = (result.elements ?? []).flatMap(element => {
        const lat = element.lat ?? element.center?.lat, lon = element.lon ?? element.center?.lon;
        const tags = element.tags ?? {};
        const name = tags.name || tags["name:en"];
        if (lat === undefined || lon === undefined || !name) return [];
        const key = name.trim().toLowerCase();
        if (seen.has(key)) return [];
        seen.add(key);
        return [{ id: element.id, name, type: placeType(tags), lat, lon,
          distanceKm: distanceKm(anchor.latitude!, anchor.longitude!, lat, lon), driveDistanceKm: null, driveDurationMinutes: null, tags }];
      }).sort((a, b) => a.distanceKm - b.distanceKm).slice(0, 12);
      if (found.length) {
        try {
          const points = [[anchor.longitude!, anchor.latitude!], ...found.map(place => [place.lon, place.lat])];
          const coordinates = points.map(point => String(point[0]) + "," + String(point[1])).join(";");
          const tableResponse = await fetch("https://router.project-osrm.org/table/v1/driving/" + coordinates + "?sources=0&annotations=distance,duration", { signal: controller.signal });
          if (tableResponse.ok) {
            const table = await tableResponse.json() as { code?: string; distances?: Array<Array<number | null>>; durations?: Array<Array<number | null>> };
            if (table.code === "Ok") {
              const distances = table.distances?.[0] ?? [], durations = table.durations?.[0] ?? [];
              found.forEach((place, index) => {
                const distance = distances[index + 1], duration = durations[index + 1];
                if (distance != null && duration != null) {
                  place.driveDistanceKm = distance / 1000;
                  place.driveDurationMinutes = duration / 60;
                }
              });
              found.sort((a, b) => {
                if (a.driveDurationMinutes != null && b.driveDurationMinutes != null) return a.driveDurationMinutes - b.driveDurationMinutes;
                if (a.driveDurationMinutes != null) return -1;
                if (b.driveDurationMinutes != null) return 1;
                return a.distanceKm - b.distanceKm;
              });
            }
          }
        } catch {
          if (controller.signal.aborted) return;
        }
      }
      setPlaces(found);
      setStatus("ready");
      setMessage(found.length ? found.length + " nearby places found. Ranked by estimated driving time where routing data is available; straight-line distance is used otherwise." : "No named indoor places were found within 3 km. Try another activity location.");
    }).catch(error => {
      if (controller.signal.aborted) return;
      setStatus("error");
      setMessage(error instanceof Error ? error.message : "Nearby place search is temporarily unavailable.");
    });
    return () => controller.abort();
  }, [selectedDay, anchor]);


  async function addSelectedPlace(place: Place) {
    if (!selectedDay || !accessToken || !canAddToItinerary || addingPlaceId !== null) return;
    setAddingPlaceId(place.id);
    setAddMessage("");
    try {
      const response = await fetch(`${import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080"}/api/v1/trips/${tripId}/itinerary/days/${selectedDay.id}/activities`, {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}` },
        body: JSON.stringify({
          title: place.name,
          description: `Indoor alternative discovered from OpenStreetMap. Category: ${place.type.replaceAll("_", " ")}.`,
          placeName: place.name,
          latitude: place.lat,
          longitude: place.lon,
          durationMinutes: null,
          estimatedCost: null,
          currencyCode: null,
          sortOrder: 0,
          startTime: null,
          endTime: null
        })
      });
      if (!response.ok) {
        const detail = await response.text();
        throw new Error(detail || `Could not add this place (HTTP ${response.status}).`);
      }
      setPendingAddPlaceId(null);
      setAddMessage(`${place.name} was added to Day ${selectedDay.dayNumber}.`);
      await onAdded();
    } catch (error) {
      setAddMessage(error instanceof Error ? error.message : "Could not add this place.");
    } finally {
      setAddingPlaceId(null);
    }
  }


  async function replaceSelectedActivity(place: Place) {
    const original = selectedDay?.activities.find(activity => activity.id === replacementActivityId);
    if (!original || !accessToken || !canAddToItinerary || addingPlaceId !== null) return;
    setAddingPlaceId(place.id);
    setAddMessage("");
    try {
      const response = await fetch(`${import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080"}/api/v1/trips/${tripId}/itinerary/activities/${original.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}` },
        body: JSON.stringify({
          title: place.name,
          description: `Weather alternative selected by traveller. Replaces: ${original.title}. Category: ${place.type.replaceAll("_", " ")}.`,
          placeName: place.name,
          latitude: place.lat,
          longitude: place.lon,
          durationMinutes: original.durationMinutes,
          startTime: original.startTime,
          endTime: original.endTime,
          estimatedCost: original.estimatedCost,
          currencyCode: original.currencyCode,
          sortOrder: original.sortOrder
        })
      });
      if (!response.ok) {
        const detail = await response.text();
        throw new Error(detail || `Could not replace the activity (HTTP ${response.status}).`);
      }
      setPendingAddPlaceId(null);
      setReplacementMode(false);
      setReplacementActivityId("");
      setAddMessage(`${original.title} was replaced with ${place.name} on Day ${selectedDay?.dayNumber}. Its time and budget fields were preserved.`);
      await onAdded();
    } catch (error) {
      setAddMessage(error instanceof Error ? error.message : "Could not replace this activity.");
    } finally {
      setAddingPlaceId(null);
    }
  }

  if (!days.length) return null;
  return <section className="nearby-indoor-panel">
    <div className="nearby-indoor-heading">
      <div><p className="eyebrow">WEATHER BACKUP · OPENSTREETMAP</p><h2>Indoor places nearby</h2>
        <p>Find museums, galleries, libraries and other indoor options around a saved activity location.</p></div>
      <span className="nearby-indoor-radius">3 km search radius</span>
    </div>
    {weatherAffectedActivities.length > 0 && <section className="nearby-weather-suggestions" aria-label="Weather affected activities">
      <div><p className="eyebrow">WEATHER-AWARE ALTERNATIVES</p><h3>Outdoor activities needing a backup</h3>
        <p>These activities have moderate or high weather assessments. Nearby indoor places can be reviewed as alternatives.</p></div>
      {weatherAffectedActivities.map(({ activity, assessment }) => <article className="nearby-weather-risk" key={activity.id}>
        <div><strong>{activity.title}</strong><span className={`nearby-weather-severity ${assessment.severity}`}>{assessment.severity} weather risk</span>
          <p>{assessment.recommendation || assessment.summary}</p>
          <small>{assessment.precipitationProbabilityMax != null ? `Rain probability up to ${assessment.precipitationProbabilityMax}% · ` : ""}{assessment.precipitationSumMm != null ? `${assessment.precipitationSumMm} mm precipitation · ` : ""}{assessment.windSpeedMaxKmh != null ? `Wind up to ${assessment.windSpeedMaxKmh} km/h` : ""}</small>
        </div>
        {canAddToItinerary && <button type="button" disabled={places.length === 0 || addingPlaceId !== null} onClick={() => { setReplacementActivityId(activity.id); setReplacementMode(true); setPendingAddPlaceId(places[0]?.id ?? null); setAddMessage(""); }}>{places.length ? "Review nearest indoor option" : "Find indoor options first"}</button>}
      </article>)}
    </section>}
    <label className="nearby-indoor-day">Search around itinerary day
      <select value={selectedDay?.id ?? ""} onChange={event => setSelectedDayId(event.target.value)}>
        {days.map(day => <option key={day.id} value={day.id}>Day {day.dayNumber} · {day.date}</option>)}
      </select>
    </label>
    {anchor && <p className="nearby-indoor-anchor">Search centre: <strong>{anchor.placeName || anchor.title}</strong> · first activity with saved coordinates</p>}
    <div className="nearby-indoor-status" aria-live="polite">{status === "loading" ? "Searching OpenStreetMap…" : message}</div>
    <div className="nearby-indoor-map-wrap">
      <div ref={mapElement} className="nearby-indoor-map" aria-label="Map of nearby indoor places and selected itinerary activity" />
      <div className="nearby-indoor-map-legend"><span><i className="nearby-origin-key" /> Planned activity</span><span><i className="nearby-place-key" /> Indoor place</span></div>
      {selectedPlaceId !== null && <p className="nearby-indoor-route-status" aria-live="polite">{routeStatus === "loading" ? "Calculating estimated driving route…" : routeStatus === "ready" ? `Estimated drive: ${routeSummary ? `${routeSummary.distanceKm.toFixed(1)} km · ${Math.max(1, Math.round(routeSummary.durationMinutes))} min` : "route shown on map"}. Traffic conditions may change this estimate.` : routeStatus === "unavailable" ? "Driving route unavailable for this place." : "Select a place to see its route."}</p>}
    </div>
    {status === "ready" && places.length > 0 && <div className="nearby-indoor-list">
      {places.map(place => <article className={`nearby-indoor-card ${selectedPlaceId === place.id ? "is-selected" : ""}`} key={`${place.id}-${place.name}`}>
        <div><span className="nearby-indoor-type">{place.type.replaceAll("_", " ")}</span><h3>{place.name}</h3>
          <p>{place.driveDurationMinutes != null ? `Approx. ${Math.max(1, Math.round(place.driveDurationMinutes))} min drive · ${place.driveDistanceKm?.toFixed(1)} km by road` : `${place.distanceKm.toFixed(1)} km straight-line distance · driving estimate unavailable`}</p>
          {(() => {
            const availability = openingStatus(place.tags.opening_hours, selectedDay?.date, anchor?.startTime);
            return <small>{availability === "open" ? "Listed hours suggest open at the planned visit time" : availability === "closed" ? "Listed hours suggest closed at the planned visit time" : "Opening status unverified — check the venue before visiting"}{place.tags.opening_hours ? ` · OSM: ${place.tags.opening_hours}` : ""}</small>;
          })()}
        </div>
        <div className="nearby-indoor-actions"><button type="button" onClick={() => setSelectedPlaceId(selectedPlaceId === place.id ? null : place.id)}>{selectedPlaceId === place.id ? "Hide route" : "Show route"}</button>{canAddToItinerary && <button type="button" onClick={() => { setPendingAddPlaceId(pendingAddPlaceId === place.id ? null : place.id); setAddMessage(""); }}>Add to itinerary</button>}<a href={`https://www.openstreetmap.org/?mlat=${place.lat}&mlon=${place.lon}#map=17/${place.lat}/${place.lon}`} target="_blank" rel="noopener noreferrer">Open map ↗</a></div>
        {pendingAddPlaceId === place.id && <div className="nearby-indoor-confirm" role="group" aria-label={`Confirm adding ${place.name}`}>
          <div className="nearby-indoor-confirm-copy">
            {replacementMode ? <>
              <strong>Replace an existing activity?</strong>
              <select aria-label="Activity to replace" value={replacementActivityId} onChange={event => setReplacementActivityId(event.target.value)}>
                <option value="">Choose an activity on Day {selectedDay?.dayNumber}</option>
                {selectedDay?.activities.map(activity => <option key={activity.id} value={activity.id}>{activity.title}{activity.startTime ? ` · ${activity.startTime}` : ""}</option>)}
              </select>
              {selectedDay?.activities.find(activity => activity.id === replacementActivityId) && <p>Current: {selectedDay.activities.find(activity => activity.id === replacementActivityId)?.title}<br/>Proposed: {place.name}<br/>Existing time and budget values will be retained.</p>}
            </> : <>Add <strong>{place.name}</strong> to Day {selectedDay?.dayNumber}? This adds it as an unscheduled activity and keeps your existing plan unchanged.</>}
          </div>
          <button type="button" disabled={addingPlaceId !== null || (replacementMode && !replacementActivityId)} onClick={() => void (replacementMode ? replaceSelectedActivity(place) : addSelectedPlace(place))}>{addingPlaceId === place.id ? "Saving…" : replacementMode ? "Confirm replacement" : "Confirm add"}</button>
          <button type="button" disabled={addingPlaceId !== null} onClick={() => setReplacementMode(value => !value)}>{replacementMode ? "Add separately instead" : "Replace existing activity"}</button>
          <button type="button" disabled={addingPlaceId !== null} onClick={() => setPendingAddPlaceId(null)}>Cancel</button>
        </div>}
      </article>)}
    </div>}
    {addMessage && <p className="nearby-indoor-add-message" role="status">{addMessage}</p>}
    <p className="nearby-indoor-disclaimer">Place data © OpenStreetMap contributors. Results depend on community mapping and may be incomplete. Distances are straight-line estimates, not walking or driving distances. Verify opening times and suitability before visiting.</p>
  </section>;
}
