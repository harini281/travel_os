import "./admin.css";
import { FormEvent, useEffect, useMemo, useState } from "react";

const API_URL = import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080";
const ADMIN_SESSION_KEY = "travelos.admin.accessToken";

type AdminUser = { id: string; email: string; displayName: string; roles: string[] };
type AuthResponse = { accessToken: string; tokenType: string; expiresAt: string; user: AdminUser };
type Section = "overview" | "users" | "trips" | "places" | "analytics" | "settings";
type AdminDashboardData = { registeredTravellers: number; tripsCreated: number; placesWithCoordinates: number; aiDraftsCreated: number; generatedAt: string };
type PagedAdmin<T> = { items: T[]; page: number; pageSize: number; total: number };
type AdminUserRow = { id: string; email: string; displayName: string; createdAt: string; emailConfirmed: boolean; isLockedOut: boolean; roles: string[] };
type AdminTripRow = { id: string; name: string; destination: string; startDate: string; endDate: string; ownerUserId: string; memberCount: number; createdAt: string };


const navigation: { id: Section; label: string; icon: string; group: string }[] = [
  { id: "overview", label: "Overview", icon: "◫", group: "WORKSPACE" },
  { id: "users", label: "User management", icon: "♙", group: "MANAGEMENT" },
  { id: "trips", label: "Trip management", icon: "⌖", group: "MANAGEMENT" },
  { id: "places", label: "Places & discovery", icon: "◇", group: "MANAGEMENT" },
  { id: "analytics", label: "Analytics", icon: "▥", group: "INSIGHTS" },
  { id: "settings", label: "Settings", icon: "⚙", group: "SYSTEM" },
];

const pageCopy: Record<Section, { eyebrow: string; title: string; description: string }> = {
  overview: { eyebrow: "PLATFORM OVERVIEW", title: "Good to see you.", description: "A live snapshot of traveller accounts, trips and saved itinerary data across TravelOS." },
  users: { eyebrow: "ACCOUNT OPERATIONS", title: "User management", description: "Search and review traveller accounts across the platform. Account changes remain restricted until audited write operations are added." },
  trips: { eyebrow: "TRAVEL OPERATIONS", title: "Trip management", description: "Search and inspect trips across the platform without changing traveller-owned plans." },
  places: { eyebrow: "DISCOVERY QUALITY", title: "Places & discovery", description: "Manage place records, categories and verification quality for destination recommendations." },
  analytics: { eyebrow: "PLATFORM INSIGHTS", title: "Analytics", description: "Understand platform adoption, trip activity and recommendation quality once analytics endpoints are connected." },
  settings: { eyebrow: "PLATFORM CONFIGURATION", title: "Settings", description: "Manage operational configuration and administrative access policies. Sensitive settings will remain server-managed." },
};

async function request<T>(path: string, token?: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error("Your session is invalid or has expired. Please sign in again.");
    if (response.status === 403) throw new Error("Your account does not have platform administrator access.");
    const body = await response.json().catch(() => null);
    throw new Error(body?.message ?? `Request failed (${response.status}).`);
  }
  return response.json() as Promise<T>;
}

function isPlatformAdmin(user: AdminUser) {
  return user.roles.some(role => ["admin", "platformadmin", "administrator"].includes(role.toLowerCase()));
}

function Brand() {
  return <a className="admin-brand" href="/admin" aria-label="TravelOS Admin home"><span className="admin-brand-mark">T</span><span>travelos<span className="admin-brand-light">.</span><small>ADMIN CONSOLE</small></span></a>;
}

function AdminLogin({ onAuthenticated }: { onAuthenticated: (token: string, user: AdminUser) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const auth = await request<AuthResponse>("/api/v1/auth/login", undefined, {
        method: "POST",
        body: JSON.stringify({ email, password }),
      });
      if (!isPlatformAdmin(auth.user)) {
        setError("This account is not assigned a platform administrator role. Ask an existing administrator to provision access.");
        return;
      }
      onAuthenticated(auth.accessToken, auth.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to sign in. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="admin-auth-shell">
      <section className="admin-auth-story">
        <Brand />
        <div className="admin-auth-message">
          <span className="admin-kicker">THE OPERATIONS DESK</span>
          <h1>Thoughtful travel.<br /><em>Confidently managed.</em></h1>
          <p>One considered workspace for the people looking after the TravelOS experience.</p>
        </div>
        <div className="admin-auth-foot"><span>TRAVELOS PLATFORM</span><span>SECURE ADMINISTRATION</span></div>
      </section>
      <section className="admin-auth-panel">
        <form className="admin-login-card" onSubmit={submit}>
          <div className="admin-login-emblem">✳</div>
          <span className="admin-kicker">WELCOME BACK</span>
          <h2>Administrator sign in</h2>
          <p>Use your authorized TravelOS administrator account to continue.</p>
          {error && <div className="admin-alert" role="alert">{error}</div>}
          <label>Email address<input type="email" autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} placeholder="you@company.com" required /></label>
          <label>Password<input type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} placeholder="Enter your password" required /></label>
          <button className="admin-primary-button" type="submit" disabled={busy}>{busy ? "Verifying access…" : "Sign in to console"} <span>↗</span></button>
          <div className="admin-login-note"><span>▣</span> Access is restricted to accounts with a platform administrator role.</div>
          <a className="admin-back-link" href="/">← Back to TravelOS</a>
        </form>
      </section>
    </main>
  );
}

function Overview({ token }: { token: string }) {
  const [data, setData] = useState<AdminDashboardData | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let active = true;
    request<AdminDashboardData>("/api/v1/admin/dashboard", token)
      .then(result => { if (active) setData(result); })
      .catch(err => { if (active) setError(err instanceof Error ? err.message : "Unable to load dashboard."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [token]);
  const cards = [
    { label: "Registered travellers", value: data?.registeredTravellers, icon: "♙" },
    { label: "Trips created", value: data?.tripsCreated, icon: "⌖" },
    { label: "Places with coordinates", value: data?.placesWithCoordinates, icon: "◇" },
    { label: "AI itinerary drafts", value: data?.aiDraftsCreated, icon: "✳" },
  ];
  return <>
    {error && <div className="admin-alert" role="alert">{error}</div>}
    <div className="admin-stat-grid">{cards.map(card => <article className="admin-stat-card" key={card.label}><div className="admin-stat-top"><span>{card.label}</span><i>{card.icon}</i></div><strong>{loading ? "…" : (card.value ?? 0).toLocaleString()}</strong><small>Live database count</small></article>)}</div>
    <section className="admin-panel admin-activity-panel"><div className="admin-panel-heading"><div><span className="admin-kicker">DATA FRESHNESS</span><h3>Platform overview</h3></div><span className="admin-muted-label">{data ? `Updated ${new Date(data.generatedAt).toLocaleString()}` : "Waiting for data"}</span></div><div className="admin-empty-state"><span>✓</span><strong>{data ? "Connected to the protected admin API" : loading ? "Loading platform metrics…" : "No overview data available"}</strong><p>Counts are read from the TravelOS database. AI drafts are not the same as accepted recommendations, and place counts represent distinct saved place names with coordinates.</p></div></section>
  </>;
}

function PlaceholderSection({ section, token }: { section: Exclude<Section, "overview">; token: string }) {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [users, setUsers] = useState<PagedAdmin<AdminUserRow> | null>(null);
  const [trips, setTrips] = useState<PagedAdmin<AdminTripRow> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    if (section !== "users" && section !== "trips") return;
    let active = true;
    setLoading(true); setError("");
    const query = search.trim() ? `?search=${encodeURIComponent(search.trim())}&page=${page}&pageSize=25` : `?page=${page}&pageSize=25`;
    const task = section === "users"
      ? request<PagedAdmin<AdminUserRow>>(`/api/v1/admin/users${query}`, token).then(value => { if (active) setUsers(value); })
      : request<PagedAdmin<AdminTripRow>>(`/api/v1/admin/trips${query}`, token).then(value => { if (active) setTrips(value); });
    task.catch(err => { if (active) setError(err instanceof Error ? err.message : "Unable to load records."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [section, search, page, token]);

  if (section === "users" || section === "trips") {
    const rows = section === "users" ? users?.items ?? [] : trips?.items ?? [];
    const total = section === "users" ? users?.total ?? 0 : trips?.total ?? 0;
    return <section className="admin-panel admin-coming-panel">
      <div className="admin-panel-heading"><div><span className="admin-kicker">READ-ONLY ADMIN DIRECTORY</span><h3>{section === "users" ? "Traveller accounts" : "All platform trips"}</h3></div><span className="admin-muted-label">{total.toLocaleString()} records</span></div>
      <label className="admin-search-label">Search {section}<input value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} placeholder={section === "users" ? "Name or email address" : "Trip name or destination"} /></label>
      {error && <div className="admin-alert" role="alert">{error}</div>}
      {loading ? <div className="admin-empty-state">Loading records…</div> : rows.length === 0 ? <div className="admin-empty-state"><strong>No matching records</strong><p>Try a different search term.</p></div> : <div className="admin-table-wrap"><table className="admin-data-table"><thead><tr>{section === "users" ? <><th>Traveller</th><th>Email</th><th>Platform role</th><th>Verification</th><th>Account</th><th>Joined</th></> : <><th>Trip</th><th>Destination</th><th>Dates</th><th>Members</th></>}</tr></thead><tbody>{section === "users" ? (users?.items ?? []).map(row => <tr key={row.id}><td>{row.displayName}</td><td>{row.email}</td><td>{row.roles.length ? row.roles.join(", ") : "No role"}</td><td><span className={`admin-status-pill ${row.emailConfirmed ? "is-good" : "is-pending"}`}>{row.emailConfirmed ? "Verified" : "Unverified"}</span></td><td><span className={`admin-status-pill ${row.isLockedOut ? "is-blocked" : "is-good"}`}>{row.isLockedOut ? "Locked" : "Active"}</span></td><td>{new Date(row.createdAt).toLocaleDateString()}</td></tr>) : (trips?.items ?? []).map(row => <tr key={row.id}><td>{row.name}</td><td>{row.destination}</td><td>{row.startDate} – {row.endDate}</td><td>{row.memberCount}</td></tr>)}</tbody></table></div>}
      <div className="admin-directory-footer"><p className="admin-muted-label">Showing {rows.length ? (page - 1) * 25 + 1 : 0}–{Math.min(page * 25, total)} of {total}. Search is performed by the server. This release is intentionally read-only.</p><div className="admin-pagination"><button type="button" disabled={page <= 1 || loading} onClick={() => setPage(value => Math.max(1, value - 1))}>Previous</button><span>Page {page}</span><button type="button" disabled={page * 25 >= total || loading} onClick={() => setPage(value => value + 1)}>Next</button></div></div>
    </section>;
  }
  const copy = pageCopy[section];
  const notes: Record<Exclude<Section, "overview" | "users" | "trips">, string[]> = {
    places: ["Review place source and coordinates", "Verify categories and opening-hour evidence", "Resolve duplicate or low-quality place records"],
    analytics: ["Traveller growth and active usage", "Trip creation and completion trends", "Recommendation acceptance and quality"],
    settings: ["Administrator access policy", "Provider configuration status", "Audit retention and security controls"],
  };
  return <section className="admin-panel admin-coming-panel"><div className="admin-coming-icon">{navigation.find(item => item.id === section)?.icon}</div><span className="admin-kicker">PHASE 01 FOUNDATION</span><h3>{copy.title} workspace is ready for integration</h3><p>{copy.description}</p><div className="admin-capability-list">{notes[section].map(note => <div key={note}><span>✓</span>{note}</div>)}</div><div className="admin-integration-callout"><strong>Next integration</strong><p>This area needs its own data model and protected endpoints. No unsupported management actions are exposed yet.</p></div></section>;
}

function AdminWorkspace({ user, token, onSignOut }: { user: AdminUser; token: string; onSignOut: () => void }) {
  const [section, setSection] = useState<Section>("overview");
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const copy = pageCopy[section];
  const initials = useMemo(() => user.displayName.split(/\s+/).map(part => part[0]).slice(0, 2).join("").toUpperCase(), [user.displayName]);

  useEffect(() => {
    const handler = (event: Event) => setSection((event as CustomEvent<Section>).detail);
    window.addEventListener("travelos-admin-navigate", handler);
    return () => window.removeEventListener("travelos-admin-navigate", handler);
  }, []);

  function chooseSection(next: Section) {
    setSection(next);
    setMobileNavOpen(false);
  }

  return <div className="admin-app-shell">
    {mobileNavOpen && <button className="admin-mobile-scrim" aria-label="Close navigation" onClick={() => setMobileNavOpen(false)} />}
    <aside className={`admin-sidebar ${mobileNavOpen ? "is-open" : ""}`}>
      <Brand />
      <div className="admin-sidebar-caption">MANAGEMENT</div>
      {navigation.map((item, index) => <div key={item.id}>
        {(index === 0 || navigation[index - 1].group !== item.group) && <div className="admin-nav-group">{item.group}</div>}
        <button className={`admin-nav-item ${section === item.id ? "is-active" : ""}`} onClick={() => chooseSection(item.id)}><span>{item.icon}</span>{item.label}{section === item.id && <i />}</button>
      </div>)}
      <div className="admin-sidebar-bottom"><div className="admin-sidebar-tip"><span>✦</span><strong>Made for thoughtful operations</strong><p>Keep the traveller experience clear, reliable and welcoming.</p></div><div className="admin-profile"><span className="admin-avatar">{initials || "A"}</span><span className="admin-profile-copy"><strong>{user.displayName}</strong><small>Platform administrator</small></span><button onClick={onSignOut} title="Sign out" aria-label="Sign out">↗</button></div></div>
    </aside>
    <main className="admin-main">
      <header className="admin-topbar"><button className="admin-mobile-menu" onClick={() => setMobileNavOpen(true)} aria-label="Open navigation">☰</button><div className="admin-breadcrumb">TravelOS <span>/</span> Administration <span>/</span> <strong>{copy.title}</strong></div><div className="admin-top-actions"><span className="admin-environment"><i /> ADMIN WORKSPACE</span><span className="admin-top-avatar">{initials || "A"}</span></div></header>
      <div className="admin-content">
        <section className="admin-page-heading"><div><span className="admin-kicker">{copy.eyebrow}</span><h1>{copy.title}</h1><p>{copy.description}</p></div><div className="admin-heading-meta"><span>TRAVELOS CONSOLE</span><strong>Phase 01 · Foundation</strong></div></section>
        {section === "overview" ? <Overview token={token} /> : <PlaceholderSection section={section} token={token} />}
        <footer className="admin-footer"><span>TRAVELOS · THOUGHTFULLY CONNECTED TRAVEL</span><span>ADMIN FOUNDATION · PHASE 01</span></footer>
      </div>
    </main>
  </div>;
}

export default function AdminApp() {
  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem(ADMIN_SESSION_KEY));
  const [user, setUser] = useState<AdminUser | null>(null);
  const [checking, setChecking] = useState(Boolean(token));
  const [sessionError, setSessionError] = useState("");

  useEffect(() => {
    if (!token) { setChecking(false); return; }
    let active = true;
    request<AdminUser>("/api/v1/auth/me", token)
      .then(profile => {
        if (!active) return;
        if (!isPlatformAdmin(profile)) throw new Error("This account no longer has platform administrator access.");
        setUser(profile);
      })
      .catch(error => {
        if (!active) return;
        sessionStorage.removeItem(ADMIN_SESSION_KEY);
        setToken(null);
        setUser(null);
        setSessionError(error instanceof Error ? error.message : "Please sign in again.");
      })
      .finally(() => { if (active) setChecking(false); });
    return () => { active = false; };
  }, [token]);

  function authenticate(accessToken: string, adminUser: AdminUser) {
    sessionStorage.setItem(ADMIN_SESSION_KEY, accessToken);
    setToken(accessToken);
    setUser(adminUser);
  }

  function signOut() {
    sessionStorage.removeItem(ADMIN_SESSION_KEY);
    setToken(null);
    setUser(null);
    setSessionError("");
  }

  if (checking) return <main className="admin-loading"><span className="admin-loading-mark">T</span><p>Verifying administrator access…</p></main>;
  if (user && token) return <AdminWorkspace user={user} token={token} onSignOut={signOut} />;
  return <><AdminLogin onAuthenticated={authenticate} />{sessionError && <div className="admin-session-message" role="status">{sessionError}</div>}</>;
}
