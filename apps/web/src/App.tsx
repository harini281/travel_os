import { FormEvent, useCallback, useEffect, useState } from "react";

const API_URL = import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080";

type AuthUser = { id: string; email: string; displayName: string; roles: string[] };
type AuthResponse = { accessToken: string; tokenType: string; expiresAt: string; user: AuthUser };
type Trip = {
  id: string; name: string; destination: string; startDate: string; endDate: string;
  currencyCode: string; ownerUserId: string; currentUserRole: string; memberCount: number;
  createdAt: string;
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
    const validation = payload.errors
      ? Object.values(payload.errors).flat().join(" ")
      : "";
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
      setToken(null);
      setUser(null);
      setTrips([]);
      setError("Your session expired. Please sign in again.");
    });
  }, [token, loadProfile]);

  async function handleAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setBusy(true); setError("");
    try {
      const payload = authMode === "register"
        ? {
            email: String(form.get("email")),
            displayName: String(form.get("displayName")),
            password: String(form.get("password")),
          }
        : { email: String(form.get("email")), password: String(form.get("password")) };
      const result = await api<AuthResponse>(
        `/api/v1/auth/${authMode === "register" ? "register" : "login"}`,
        undefined,
        { method: "POST", body: JSON.stringify(payload) },
      );
      sessionStorage.setItem("travelos.accessToken", result.accessToken);
      setToken(result.accessToken);
      setUser(result.user);
      setNotice(authMode === "register" ? "Your TravelOS account is ready." : "Welcome back.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to sign in.");
    } finally {
      setBusy(false);
    }
  }

  function signOut() {
    sessionStorage.removeItem("travelos.accessToken");
    setToken(null); setUser(null); setTrips([]);
    setNotice("You have signed out.");
    setError("");
  }

  async function createTrip(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token) return;
    setBusy(true); setError("");
    try {
      const trip = await api<Trip>("/api/v1/trips", token, {
        method: "POST",
        body: JSON.stringify({ name, destination, startDate, endDate, currencyCode }),
      });
      setTrips(current => [...current, trip].sort((a, b) => a.startDate.localeCompare(b.startDate)));
      setName(""); setDestination(""); setStartDate(""); setEndDate("");
      setCurrencyCode("USD"); setShowCreate(false);
      setNotice("Your trip has been created.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to create trip.");
    } finally {
      setBusy(false);
    }
  }

  async function deleteTrip(trip: Trip) {
    if (!token || !window.confirm(`Delete "${trip.name}"? This cannot be undone.`)) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${trip.id}`, token, { method: "DELETE" });
      setTrips(current => current.filter(item => item.id !== trip.id));
      setNotice("Trip deleted.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to delete trip.");
    }
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

  return (
    <main className="dashboard">
      <aside className="sidebar">
        <a className="brand" href="/">travel<span>OS</span></a>
        <div className="side-label">WORKSPACE</div>
        <a className="side-link selected" href="#trips"><span>▦</span> My trips</a>
        <div className="side-note"><span className="side-dot" /> Your travel space, all together.</div>
        <div className="sidebar-bottom">
          <div className="avatar">{initials(user.displayName)}</div>
          <div className="user-meta"><strong>{user.displayName}</strong><span>{user.email}</span></div>
          <button className="icon-button" onClick={signOut} title="Sign out" aria-label="Sign out">↗</button>
        </div>
      </aside>
      <section className="main-panel" id="trips">
        <header className="topbar">
          <span className="breadcrumb">Workspace <b>/</b> My trips</span>
          <span className="preview-tag">TRAVELOS PREVIEW</span>
        </header>
        <div className="content-wrap">
          {notice && <div className="alert success">{notice}<button onClick={() => setNotice("")}>×</button></div>}
          {error && <div className="alert error">{error}<button onClick={() => setError("")}>×</button></div>}
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
            </article>)}</section>}
          <footer className="dashboard-footer">TRAVELOS · THOUGHTFULLY CONNECTED TRAVEL</footer>
        </div>
      </section>
    </main>
  );
}
