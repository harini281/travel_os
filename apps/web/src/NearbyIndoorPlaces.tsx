import { useEffect, useMemo, useState } from "react";

type IndoorActivity = {
  id: string; title: string; placeName: string | null;
  latitude: number | null; longitude: number | null;
};
type IndoorDay = { id: string; dayNumber: number; date: string; activities: IndoorActivity[] };
type Place = { id: number; name: string; type: string; lat: number; lon: number; distanceKm: number; tags: Record<string, string> };
type Props = { days: IndoorDay[] };

function distanceKm(aLat: number, aLon: number, bLat: number, bLon: number) {
  const radians = (degrees: number) => degrees * Math.PI / 180;
  const dLat = radians(bLat - aLat), dLon = radians(bLon - aLon);
  const value = Math.sin(dLat / 2) ** 2 +
    Math.cos(radians(aLat)) * Math.cos(radians(bLat)) * Math.sin(dLon / 2) ** 2;
  return 6371 * 2 * Math.atan2(Math.sqrt(value), Math.sqrt(1 - value));
}

function placeType(tags: Record<string, string>) {
  return tags.amenity || tags.tourism || tags.leisure || tags.shop || "indoor attraction";
}

export default function NearbyIndoorPlaces({ days }: Props) {
  const [selectedDayId, setSelectedDayId] = useState(days[0]?.id ?? "");
  const [places, setPlaces] = useState<Place[]>([]);
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [message, setMessage] = useState("");
  const selectedDay = useMemo(() => days.find(day => day.id === selectedDayId) ?? days[0] ?? null, [days, selectedDayId]);
  const anchor = useMemo(() => selectedDay?.activities.find(activity =>
    activity.latitude !== null && activity.longitude !== null &&
    Number.isFinite(activity.latitude) && Number.isFinite(activity.longitude)
  ) ?? null, [selectedDay]);

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
          distanceKm: distanceKm(anchor.latitude!, anchor.longitude!, lat, lon), tags }];
      }).sort((a, b) => a.distanceKm - b.distanceKm).slice(0, 12);
      setPlaces(found);
      setStatus("ready");
      setMessage(found.length ? `${found.length} nearby indoor and cultural places found within 3 km.` : "No named indoor places were found within 3 km. Try another activity location.");
    }).catch(error => {
      if (controller.signal.aborted) return;
      setStatus("error");
      setMessage(error instanceof Error ? error.message : "Nearby place search is temporarily unavailable.");
    });
    return () => controller.abort();
  }, [selectedDay, anchor]);

  if (!days.length) return null;
  return <section className="nearby-indoor-panel">
    <div className="nearby-indoor-heading">
      <div><p className="eyebrow">WEATHER BACKUP · OPENSTREETMAP</p><h2>Indoor places nearby</h2>
        <p>Find museums, galleries, libraries and other indoor options around a saved activity location.</p></div>
      <span className="nearby-indoor-radius">3 km search radius</span>
    </div>
    <label className="nearby-indoor-day">Search around itinerary day
      <select value={selectedDay?.id ?? ""} onChange={event => setSelectedDayId(event.target.value)}>
        {days.map(day => <option key={day.id} value={day.id}>Day {day.dayNumber} · {day.date}</option>)}
      </select>
    </label>
    {anchor && <p className="nearby-indoor-anchor">Search centre: <strong>{anchor.placeName || anchor.title}</strong> · first activity with saved coordinates</p>}
    <div className="nearby-indoor-status" aria-live="polite">{status === "loading" ? "Searching OpenStreetMap…" : message}</div>
    {status === "ready" && places.length > 0 && <div className="nearby-indoor-list">
      {places.map(place => <article className="nearby-indoor-card" key={`${place.id}-${place.name}`}>
        <div><span className="nearby-indoor-type">{place.type.replaceAll("_", " ")}</span><h3>{place.name}</h3>
          <p>{place.distanceKm.toFixed(1)} km from the selected activity</p>
          {place.tags.opening_hours && <small>Listed hours: {place.tags.opening_hours}</small>}
          {!place.tags.opening_hours && <small>Opening hours not listed — check before visiting</small>}
        </div>
        <a href={`https://www.openstreetmap.org/?mlat=${place.lat}&mlon=${place.lon}#map=17/${place.lat}/${place.lon}`} target="_blank" rel="noopener noreferrer">View map ↗</a>
      </article>)}
    </div>}
    <p className="nearby-indoor-disclaimer">Place data © OpenStreetMap contributors. Results depend on community mapping and may be incomplete. Distances are straight-line estimates, not walking or driving distances. Verify opening times and suitability before visiting.</p>
  </section>;
}
