import { FormEvent, useCallback, useEffect, useState } from "react";

const API_URL = import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080";

type AuthUser = { id: string; email: string; displayName: string; roles: string[] };
type AuthResponse = { accessToken: string; tokenType: string; expiresAt: string; user: AuthUser };
type Trip = {
  id: string; name: string; destination: string; startDate: string; endDate: string;
  currencyCode: string; ownerUserId: string; currentUserRole: string; memberCount: number;
  createdAt: string;
};
type Activity = {
  id: string; title: string; description: string | null; placeName: string | null;
  latitude: number | null; longitude: number | null; durationMinutes: number | null;
  estimatedCost: number | null; currencyCode: string | null; sortOrder: number;
};
type ItineraryDay = { id: string; dayNumber: number; date: string; activities: Activity[] };
type Itinerary = {
  id: string; tripId: string; version: number; isApproved: boolean;
  approvedAt: string | null; days: ItineraryDay[];
};
type SuggestedActivity = Omit<Activity, "id">;
type AiDraftDay = { dayNumber: number; date: string; theme: string; activities: SuggestedActivity[] };
type AiDraft = {
  id: string; tripId: string; status: string; createdAt: string;
  provider: string; model: string | null; warnings: string[];
  itinerary: { title: string; summary: string; days: AiDraftDay[] };
};
type ApiError = { message?: string; title?: string; errors?: Record<string, string[]> };

async function api<T>(path: string, token?: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  });
  if (!response.ok) {
    const payload = await response.json().catch(() => ({})) as ApiError;
    const validation = payload.errors ? Object.values(payload.errors).flat().join(" ") : "";
    throw new Error(validation || payload.message || payload.title || `Request failed (${response.status})`);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

function formatDate(value: string) {
  return new Date(`${value}T12:00:00`).toLocaleDateString(undefined, {
    day: "numeric", month: "short", year: "numeric",
  });
}
function initials(name: string) {
  return name.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toUpperCase()).join("");
}
function dayNumberFor(date: string, tripStart: string) {
  const selected = new Date(`${date}T00:00:00Z`).getTime();
  const start = new Date(`${tripStart}T00:00:00Z`).getTime();
  return Math.floor((selected - start) / 86400000) + 1;
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem("travelos.accessToken"));
  const [user, setUser] = useState<AuthUser | null>(null);
  const [trips, setTrips] = useState<Trip[]>([]);
  const [authMode, setAuthMode] = useState<"login" | "register">("login");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [destination, setDestination] = useState("");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [currencyCode, setCurrencyCode] = useState("USD");

  const [selectedTrip, setSelectedTrip] = useState<Trip | null>(null);
  const [itinerary, setItinerary] = useState<Itinerary | null>(null);
  const [loadingItinerary, setLoadingItinerary] = useState(false);
  const [showAddDay, setShowAddDay] = useState(false);
  const [newDayDate, setNewDayDate] = useState("");
  const [activeDayId, setActiveDayId] = useState<string | null>(null);
  const [activityTitle, setActivityTitle] = useState("");
  const [activityPlace, setActivityPlace] = useState("");
  const [activityDescription, setActivityDescription] = useState("");
  const [activityDuration, setActivityDuration] = useState("");
  const [activityCost, setActivityCost] = useState("");
  const [aiDrafts, setAiDrafts] = useState<AiDraft[]>([]);
  const [travellerCount, setTravellerCount] = useState(1);
  const [interestsText, setInterestsText] = useState("");
  const [travelPace, setTravelPace] = useState<"relaxed" | "balanced" | "fast">("balanced");

  const loadTrips = useCallback(async (accessToken: string) => {
    const data = await api<Trip[]>("/api/v1/trips", accessToken);
    setTrips(data);
  }, []);
  const loadProfile = useCallback(async (accessToken: string) => {
    const profile = await api<AuthUser>("/api/v1/auth/me", accessToken);
    setUser(profile);
    await loadTrips(accessToken);
  }, [loadTrips]);

  useEffect(() => {
    if (!token) return;
    loadProfile(token).catch(() => {
      sessionStorage.removeItem("travelos.accessToken");
      setToken(null); setUser(null); setTrips([]);
      setError("Your session expired. Please sign in again.");
    });
  }, [token, loadProfile]);

  async function loadItinerary(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    setLoadingItinerary(true); setError("");
    try {
      const result = await api<Itinerary | undefined>(
        `/api/v1/trips/${trip.id}/itinerary`, accessToken,
      );
      setItinerary(result ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load itinerary.");
    } finally {
      setLoadingItinerary(false);
    }
  }

  async function loadDrafts(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<AiDraft[]>(`/api/v1/trips/${trip.id}/itinerary/drafts`, accessToken);
      setAiDrafts(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load AI drafts.");
    }
  }

  async function openTrip(trip: Trip) {
    setSelectedTrip(trip); setItinerary(null); setAiDrafts([]);
    setShowAddDay(false); setActiveDayId(null);
    await Promise.all([loadItinerary(trip), loadDrafts(trip)]);
  }

  function closeTrip() {
    setSelectedTrip(null); setItinerary(null); setAiDrafts([]);
    setShowAddDay(false); setActiveDayId(null); setError("");
  }

  async function handleAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setBusy(true); setError("");
    try {
      const payload = authMode === "register"
        ? { email: String(form.get("email")), displayName: String(form.get("displayName")), password: String(form.get("password")) }
        : { email: String(form.get("email")), password: String(form.get("password")) };
      const result = await api<AuthResponse>(
        `/api/v1/auth/${authMode === "register" ? "register" : "login"}`,
        undefined, { method: "POST", body: JSON.stringify(payload) },
      );
      sessionStorage.setItem("travelos.accessToken", result.accessToken);
      setToken(result.accessToken); setUser(result.user);
      setNotice(authMode === "register" ? "Your TravelOS account is ready." : "Welcome back.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to sign in.");
    } finally { setBusy(false); }
  }

  function signOut() {
    sessionStorage.removeItem("travelos.accessToken");
    setToken(null); setUser(null); setTrips([]); setSelectedTrip(null);
    setNotice("You have signed out."); setError("");
  }

  async function createTrip(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!token) return;
    setBusy(true); setError("");
    try {
      const trip = await api<Trip>("/api/v1/trips", token, {
        method: "POST", body: JSON.stringify({ name, destination, startDate, endDate, currencyCode }),
      });
      setTrips(current => [...current, trip].sort((a, b) => a.startDate.localeCompare(b.startDate)));
      setName(""); setDestination(""); setStartDate(""); setEndDate("");
      setCurrencyCode("USD"); setShowCreate(false); setNotice("Your trip has been created.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to create trip.");
    } finally { setBusy(false); }
  }

  async function deleteTrip(trip: Trip) {
    if (!token || !window.confirm(`Delete "${trip.name}"? This cannot be undone.`)) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${trip.id}`, token, { method: "DELETE" });
      setTrips(current => current.filter(item => item.id !== trip.id));
      setNotice("Trip deleted.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to delete trip."); }
  }

  async function createItinerary() {
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api<Itinerary>(`/api/v1/trips/${selectedTrip.id}/itinerary`, token, { method: "POST" });
      await loadItinerary(selectedTrip);
      setNotice("Your itinerary workspace is ready.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to create itinerary."); }
    finally { setBusy(false); }
  }

  async function addDay(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    const dayNumber = dayNumberFor(newDayDate, selectedTrip.startDate);
    setBusy(true); setError("");
    try {
      await api<ItineraryDay>(`/api/v1/trips/${selectedTrip.id}/itinerary/days`, token, {
        method: "POST", body: JSON.stringify({ dayNumber, date: newDayDate }),
      });
      await loadItinerary(selectedTrip);
      setNewDayDate(""); setShowAddDay(false); setNotice("Day added to your itinerary.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to add day."); }
    finally { setBusy(false); }
  }

  async function addActivity(event: FormEvent<HTMLFormElement>, dayId: string) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api<Activity>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/days/${dayId}/activities`, token,
        { method: "POST", body: JSON.stringify({
          title: activityTitle, description: activityDescription || null,
          placeName: activityPlace || null, latitude: null, longitude: null,
          durationMinutes: activityDuration ? Number(activityDuration) : null,
          estimatedCost: activityCost ? Number(activityCost) : null,
          currencyCode: activityCost ? selectedTrip.currencyCode : null, sortOrder: 0,
        }) },
      );
      await loadItinerary(selectedTrip);
      setActivityTitle(""); setActivityPlace(""); setActivityDescription("");
      setActivityDuration(""); setActivityCost(""); setActiveDayId(null);
      setNotice("Activity added.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to add activity."); }
    finally { setBusy(false); }
  }

  async function deleteActivity(activityId: string) {
    if (!token || !selectedTrip || !window.confirm("Remove this activity from the itinerary?")) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/itinerary/activities/${activityId}`, token, { method: "DELETE" });
      await loadItinerary(selectedTrip); setNotice("Activity removed.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to remove activity."); }
  }

  async function approveItinerary() {
    if (!token || !selectedTrip) return;
    if (!window.confirm("Approve this itinerary? It will become read-only.")) return;
    setBusy(true); setError("");
    try {
      const approved = await api<Itinerary>(`/api/v1/trips/${selectedTrip.id}/itinerary/approve`, token, { method: "POST" });
      setItinerary(approved); setNotice("Itinerary approved.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to approve itinerary."); }
    finally { setBusy(false); }
  }


  async function generateAiDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      const draft = await api<AiDraft>(`/api/v1/trips/${selectedTrip.id}/itinerary/drafts`, token, {
        method: "POST",
        body: JSON.stringify({
          travellerCount,
          budgetAmount: null,
          interests: interestsText.split(",").map(value => value.trim()).filter(Boolean),
          travelPace,
        }),
      });
      setAiDrafts(current => [draft, ...current]);
      setNotice("AI itinerary draft generated. Review it before applying.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "AI itinerary generation is unavailable.");
    } finally { setBusy(false); }
  }

  async function applyAiDraft(draftId: string) {
    if (!token || !selectedTrip) return;
    if (!window.confirm("Apply this AI draft to your trip? You can review and edit it before approving.")) return;
    setBusy(true); setError("");
    try {
      const updated = await api<Itinerary>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/drafts/${draftId}/apply`,
        token, { method: "POST" },
      );
      setItinerary(updated);
      await loadDrafts(selectedTrip);
      setNotice("AI draft applied as a new editable itinerary version. It is not approved.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to apply AI draft.");
    } finally { setBusy(false); }
  }

  if (!token || !user) {
    return (
      <main className="auth-layout">
        <section className="auth-story">
          <a className="brand" href="/">travel<span>OS</span></a>
          <div className="story-copy">
            <p className="eyebrow">YOUR JOURNEY, IN ONE PLACE</p>
            <h1>Travel well.<br /><em>Worry less.</em></h1>
            <p>One thoughtful space to plan journeys, bring people together and keep every detail in view.</p>
          </div>
          <div className="story-foot"><span>01 — PLAN</span><span>02 — CONNECT</span><span>03 — EXPLORE</span></div>
        </section>
        <section className="auth-panel">
          <div className="auth-card">
            <p className="eyebrow">WELCOME TO TRAVELOS</p>
            <h2>{authMode === "login" ? "Welcome back" : "Create your account"}</h2>
            <p className="muted">{authMode === "login" ? "Sign in to pick up where your next journey begins." : "Start bringing your travel plans together."}</p>
            <div className="auth-tabs">
              <button className={authMode === "login" ? "active" : ""} onClick={() => { setAuthMode("login"); setError(""); }}>Sign in</button>
              <button className={authMode === "register" ? "active" : ""} onClick={() => { setAuthMode("register"); setError(""); }}>Create account</button>
            </div>
            <form onSubmit={handleAuth} className="form-stack">
              {authMode === "register" && <label>Full name<input name="displayName" placeholder="Your name" minLength={2} maxLength={120} required /></label>}
              <label>Email address<input name="email" type="email" placeholder="you@example.com" autoComplete="email" required /></label>
              <label>Password<input name="password" type="password" placeholder={authMode === "register" ? "At least 10 characters" : "Your password"} minLength={authMode === "register" ? 10 : undefined} autoComplete={authMode === "register" ? "new-password" : "current-password"} required /></label>
              {authMode === "register" && <p className="field-hint">Use at least 10 characters with uppercase, lowercase, a number and a symbol.</p>}
              {error && <p className="alert error">{error}</p>}
              <button className="btn btn-primary btn-wide" disabled={busy}>{busy ? "Please wait…" : authMode === "login" ? "Sign in →" : "Create account →"}</button>
            </form>
            <p className="auth-legal">By continuing, you agree to use TravelOS responsibly. Account verification and password recovery are not available in this preview.</p>
          </div>
        </section>
      </main>
    );
  }

  const canEditItinerary = selectedTrip?.currentUserRole === "owner" && !itinerary?.isApproved;

  return (
    <main className="dashboard">
      <aside className="sidebar">
        <a className="brand" href="/">travel<span>OS</span></a>
        <div className="side-label">WORKSPACE</div>
        <button className={selectedTrip ? "side-link" : "side-link selected"} onClick={closeTrip}><span>▦</span> My trips</button>
        <div className="side-note"><span className="side-dot" /> Your travel space, all together.</div>
        <div className="sidebar-bottom">
          <div className="avatar">{initials(user.displayName)}</div>
          <div className="user-meta"><strong>{user.displayName}</strong><span>{user.email}</span></div>
          <button className="icon-button" onClick={signOut} title="Sign out" aria-label="Sign out">↗</button>
        </div>
      </aside>
      <section className="main-panel">
        <header className="topbar">
          <span className="breadcrumb">Workspace <b>/</b> {selectedTrip ? selectedTrip.name : "My trips"}</span>
          <span className="preview-tag">TRAVELOS PREVIEW</span>
        </header>
        <div className="content-wrap">
          {notice && <div className="alert success">{notice}<button onClick={() => setNotice("")}>×</button></div>}
          {error && <div className="alert error">{error}<button onClick={() => setError("")}>×</button></div>}

          {!selectedTrip ? <>
            <section className="welcome-row">
              <div><p className="eyebrow">YOUR TRAVEL SPACE</p><h1>Good to see you, {user.displayName.split(" ")[0]}.</h1><p className="muted">Every great journey starts with a plan. What's next?</p></div>
              <button className="btn btn-primary" onClick={() => { setShowCreate(v => !v); setError(""); }}>＋ Plan a trip</button>
            </section>
            <section className="stats-row">
              <article><span>YOUR TRIPS</span><strong>{trips.length.toString().padStart(2, "0")}</strong><small>Trips in your workspace</small></article>
              <article><span>UPCOMING</span><strong>{trips.filter(t => t.startDate >= new Date().toISOString().slice(0, 10)).length.toString().padStart(2, "0")}</strong><small>Journeys ahead</small></article>
              <article><span>AS A GROUP</span><strong>{trips.filter(t => t.memberCount > 1).length.toString().padStart(2, "0")}</strong><small>Trips with other travellers</small></article>
            </section>
            {showCreate && <section className="create-card">
              <div className="section-heading"><div><p className="eyebrow">NEW JOURNEY</p><h2>Let's plan somewhere.</h2></div><button className="icon-button" onClick={() => setShowCreate(false)} aria-label="Close form">×</button></div>
              <form className="trip-form" onSubmit={createTrip}>
                <label>Trip name<input value={name} onChange={e => setName(e.target.value)} placeholder="A summer to remember" maxLength={160} required /></label>
                <label>Destination<input value={destination} onChange={e => setDestination(e.target.value)} placeholder="e.g. Kyoto, Japan" maxLength={160} required /></label>
                <label>Start date<input type="date" value={startDate} onChange={e => setStartDate(e.target.value)} required /></label>
                <label>End date<input type="date" value={endDate} min={startDate || undefined} onChange={e => setEndDate(e.target.value)} required /></label>
                <label>Currency<select value={currencyCode} onChange={e => setCurrencyCode(e.target.value)}><option value="USD">USD — US Dollar</option><option value="LKR">LKR — Sri Lankan Rupee</option><option value="EUR">EUR — Euro</option><option value="GBP">GBP — Pound Sterling</option><option value="JPY">JPY — Japanese Yen</option><option value="AUD">AUD — Australian Dollar</option><option value="INR">INR — Indian Rupee</option><option value="SGD">SGD — Singapore Dollar</option></select></label>
                <div className="form-actions"><button type="button" className="btn btn-quiet" onClick={() => setShowCreate(false)}>Cancel</button><button className="btn btn-primary" disabled={busy}>{busy ? "Creating…" : "Create trip →"}</button></div>
              </form>
            </section>}
            <div className="section-heading trips-heading"><div><p className="eyebrow">COLLECTION</p><h2>Your journeys</h2></div><span className="count-label">{trips.length} {trips.length === 1 ? "TRIP" : "TRIPS"}</span></div>
            {trips.length === 0 ? <section className="empty-state"><div className="empty-icon">✳</div><h3>Your next story starts here.</h3><p>You haven't created a trip yet. Start with a destination and a few dates — you can shape the details as you go.</p><button className="btn btn-primary" onClick={() => setShowCreate(true)}>Create your first trip →</button></section> :
              <section className="trip-grid">{trips.map(trip => <article className="trip-card" key={trip.id}>
                <div className="trip-card-top"><span className="trip-mark">✳</span><span className={trip.currentUserRole === "owner" ? "role owner" : "role"}>{trip.currentUserRole}</span></div>
                <p className="trip-destination">{trip.destination}</p><h3>{trip.name}</h3>
                <div className="trip-dates"><span>◷</span>{formatDate(trip.startDate)} — {formatDate(trip.endDate)}</div>
                <div className="trip-card-foot"><span>{trip.memberCount} {trip.memberCount === 1 ? "traveller" : "travellers"} · {trip.currencyCode}</span>{trip.currentUserRole === "owner" && <button onClick={() => deleteTrip(trip)} className="delete-link">Delete</button>}</div>
                <button className="open-trip-link" onClick={() => openTrip(trip)}>Open itinerary →</button>
              </article>)}</section>}
          </> : <>
            <button className="back-link" onClick={closeTrip}>← Back to all trips</button>
            <section className="itinerary-hero">
              <div><p className="eyebrow">YOUR JOURNEY · {selectedTrip.currencyCode}</p><h1>{selectedTrip.name}</h1><p className="muted">{selectedTrip.destination} <span>·</span> {formatDate(selectedTrip.startDate)} — {formatDate(selectedTrip.endDate)}</p></div>
              {itinerary && <span className={itinerary.isApproved ? "approval-pill approved" : "approval-pill"}>{itinerary.isApproved ? "✓ APPROVED" : "DRAFT PLAN"}</span>}
            </section>

            {selectedTrip.currentUserRole === "owner" && <section className="ai-planner-card">
              <div className="ai-planner-heading"><div><p className="eyebrow">TRAVELOS INTELLIGENCE</p><h2>Start with an AI draft.</h2><p>Generate a suggested day-by-day plan, review it, then decide whether to apply it.</p></div><span className="ai-spark">✳</span></div>
              <form className="ai-options" onSubmit={generateAiDraft}>
                <label>Travellers<input type="number" min="1" max="30" value={travellerCount} onChange={e => setTravellerCount(Number(e.target.value))} /></label>
                <label>Interests<input value={interestsText} onChange={e => setInterestsText(e.target.value)} placeholder="Food, culture, nature…" /></label>
                <label>Travel pace<select value={travelPace} onChange={e => setTravelPace(e.target.value as "relaxed" | "balanced" | "fast")}><option value="relaxed">Relaxed</option><option value="balanced">Balanced</option><option value="fast">Fast-paced</option></select></label>
                <button className="btn btn-primary" disabled={busy}>{busy ? "Generating…" : "✳ Generate AI draft"}</button>
              </form>
              <p className="ai-disclaimer">AI suggestions can be inaccurate. Verify places, hours, routes and costs. Generation requires a configured AI provider.</p>
            </section>}
            {aiDrafts.length > 0 && <section className="ai-drafts-section">
              <div className="section-heading"><div><p className="eyebrow">REVIEW BEFORE APPLYING</p><h2>AI itinerary drafts</h2></div><span className="count-label">{aiDrafts.filter(d => d.status === "pending_review").length} TO REVIEW</span></div>
              {aiDrafts.map(draft => <article className="ai-draft-card" key={draft.id}>
                <div className="ai-draft-top"><div><span className="draft-provider">{draft.provider}{draft.model ? ` · ${draft.model}` : ""}</span><h3>{draft.itinerary.title}</h3></div><span className={draft.status === "applied" ? "role owner" : "role"}>{draft.status.replace("_", " ")}</span></div>
                <p className="ai-draft-summary">{draft.itinerary.summary}</p>
                <div className="ai-draft-days">{draft.itinerary.days.map(day => <div key={day.dayNumber}><strong>Day {day.dayNumber} · {formatDate(day.date)}</strong><span>{day.theme}</span><small>{day.activities.map(activity => activity.title).join(" · ")}</small></div>)}</div>
                {draft.warnings?.map((warning, index) => <p className="ai-warning" key={index}>ⓘ {warning}</p>)}
                {draft.status === "pending_review" && selectedTrip.currentUserRole === "owner" && <button className="btn btn-primary apply-draft-button" disabled={busy} onClick={() => applyAiDraft(draft.id)}>Apply this draft →</button>}
              </article>)}
            </section>}

            {loadingItinerary ? <section className="empty-state"><p>Loading your itinerary…</p></section> :
              !itinerary ? <section className="empty-state itinerary-empty"><div className="empty-icon">✳</div><h3>Your itinerary starts here.</h3><p>Create a day-by-day plan for {selectedTrip.destination}. Add places and activities, then approve it when you're happy.</p>{selectedTrip.currentUserRole === "owner" && <button className="btn btn-primary" disabled={busy} onClick={createItinerary}>Create itinerary →</button>}</section> :
              <>
                <section className="itinerary-toolbar">
                  <div><strong>{itinerary.days.length} planned days</strong><span>{itinerary.days.reduce((sum, day) => sum + day.activities.length, 0)} activities</span></div>
                  {canEditItinerary && <div className="toolbar-actions"><button className="btn btn-quiet" onClick={() => setShowAddDay(v => !v)}>＋ Add a day</button><button className="btn btn-primary" disabled={busy || itinerary.days.every(day => day.activities.length === 0)} onClick={approveItinerary}>Approve plan</button></div>}
                </section>
                {showAddDay && canEditItinerary && <form className="add-day-form" onSubmit={addDay}><label>Choose a date within your trip<input type="date" min={selectedTrip.startDate} max={selectedTrip.endDate} value={newDayDate} onChange={e => setNewDayDate(e.target.value)} required /></label><button className="btn btn-primary" disabled={busy || !newDayDate}>Add day</button><button type="button" className="btn btn-quiet" onClick={() => setShowAddDay(false)}>Cancel</button></form>}
                {itinerary.isApproved && <div className="approved-note">This itinerary has been approved and is now read-only.</div>}
                {itinerary.days.length === 0 ? <section className="empty-state"><h3>No days added yet.</h3><p>Add a date from your trip to start building your itinerary.</p></section> :
                  <section className="day-list">{itinerary.days.map(day => <article className="day-card" key={day.id}>
                    <div className="day-date"><span>DAY {day.dayNumber.toString().padStart(2, "0")}</span><strong>{formatDate(day.date)}</strong></div>
                    <div className="day-content">
                      {day.activities.length === 0 ? <p className="no-activities">Nothing planned yet. Add your first activity.</p> :
                        <div className="activity-list">{day.activities.map(activity => <div className="activity-row" key={activity.id}>
                          <span className="activity-dot">•</span><div className="activity-info"><strong>{activity.title}</strong>{activity.placeName && <span>{activity.placeName}</span>}{activity.description && <p>{activity.description}</p>}</div>
                          <div className="activity-meta">{activity.durationMinutes && <span>{activity.durationMinutes} min</span>}{activity.estimatedCost !== null && <span>{activity.estimatedCost.toLocaleString()} {activity.currencyCode}</span>}</div>
                          {canEditItinerary && <button className="delete-link" onClick={() => deleteActivity(activity.id)}>Remove</button>}
                        </div>)}</div>}
                      {canEditItinerary && (activeDayId === day.id ?
                        <form className="activity-form" onSubmit={event => addActivity(event, day.id)}>
                          <label>Activity name<input value={activityTitle} onChange={e => setActivityTitle(e.target.value)} placeholder="e.g. Explore the old town" maxLength={180} required /></label>
                          <label>Place<input value={activityPlace} onChange={e => setActivityPlace(e.target.value)} placeholder="Place or attraction" maxLength={200} /></label>
                          <label className="wide-field">Notes<textarea value={activityDescription} onChange={e => setActivityDescription(e.target.value)} placeholder="A little detail about this stop…" maxLength={2000} rows={2} /></label>
                          <label>Duration (minutes)<input type="number" min="1" value={activityDuration} onChange={e => setActivityDuration(e.target.value)} placeholder="90" /></label>
                          <label>Estimated cost ({selectedTrip.currencyCode})<input type="number" min="0" step="0.01" value={activityCost} onChange={e => setActivityCost(e.target.value)} placeholder="0.00" /></label>
                          <div className="form-actions wide-field"><button type="button" className="btn btn-quiet" onClick={() => setActiveDayId(null)}>Cancel</button><button className="btn btn-primary" disabled={busy}>{busy ? "Adding…" : "Save activity"}</button></div>
                        </form> :
                        <button className="add-activity-link" onClick={() => setActiveDayId(day.id)}>＋ Add activity</button>)}
                    </div>
                  </article>)}</section>}
              </>}
          </>}
          <footer className="dashboard-footer">TRAVELOS · THOUGHTFULLY CONNECTED TRAVEL</footer>
        </div>
      </section>
    </main>
  );
}
