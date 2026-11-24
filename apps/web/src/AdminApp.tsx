import { FormEvent, useEffect, useMemo, useState } from "react";

const API_URL = import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080";
const ADMIN_SESSION_KEY = "travelos.admin.accessToken";

type AdminUser = { id: string; email: string; displayName: string; roles: string[] };
type AuthResponse = { accessToken: string; tokenType: string; expiresAt: string; user: AdminUser };
type Section = "overview" | "users" | "trips" | "places" | "analytics" | "settings";

const navigation: { id: Section; label: string; icon: string; group: string }[] = [
  { id: "overview", label: "Overview", icon: "◫", group: "WORKSPACE" },
  { id: "users", label: "User management", icon: "♙", group: "MANAGEMENT" },
  { id: "trips", label: "Trip management", icon: "⌖", group: "MANAGEMENT" },
  { id: "places", label: "Places & discovery", icon: "◇", group: "MANAGEMENT" },
  { id: "analytics", label: "Analytics", icon: "▥", group: "INSIGHTS" },
  { id: "settings", label: "Settings", icon: "⚙", group: "SYSTEM" },
];

const pageCopy: Record<Section, { eyebrow: string; title: string; description: string }> = {
  overview: { eyebrow: "PLATFORM OVERVIEW", title: "Good to see you.", description: "A clear view of your TravelOS platform will live here. Connect the administration APIs to bring live operations data into this workspace." },
  users: { eyebrow: "ACCOUNT OPERATIONS", title: "User management", description: "Review traveller accounts, account status and role assignments. User-management actions will be enabled after the protected admin API is implemented." },
  trips: { eyebrow: "TRAVEL OPERATIONS", title: "Trip management", description: "Monitor trips across the platform and review reported or flagged travel content." },
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

function Overview() {
  const cards = [
    { label: "Registered travellers", value: "—", note: "Waiting for admin users API", icon: "♙" },
    { label: "Trips created", value: "—", note: "Waiting for platform trips API", icon: "⌖" },
    { label: "Places in catalogue", value: "—", note: "Waiting for places service", icon: "◇" },
    { label: "AI recommendations", value: "—", note: "Waiting for AI usage metrics", icon: "✳" },
  ];
  return <>
    <div className="admin-stat-grid">{cards.map(card => <article className="admin-stat-card" key={card.label}><div className="admin-stat-top"><span>{card.label}</span><i>{card.icon}</i></div><strong>{card.value}</strong><small>{card.note}</small></article>)}</div>
    <div className="admin-overview-grid">
      <section className="admin-panel admin-health-panel">
        <div className="admin-panel-heading"><div><span className="admin-kicker">SERVICE VISIBILITY</span><h3>Platform services</h3></div><span className="admin-status-pending">Integration pending</span></div>
        {["Platform API", "PostgreSQL database", "AI planning service", "Weather provider", "Maps & routing"].map((item, index) => <div className="admin-service-row" key={item}><span className="admin-service-icon">{["⌘", "▤", "✳", "☼", "⌖"][index]}</span><span>{item}<small>Health check integration planned</small></span><b>Not connected</b></div>)}
      </section>
      <section className="admin-panel admin-quick-panel">
        <span className="admin-kicker">ADMIN WORKSPACE</span><h3>Start with the essentials</h3><p>Each management area has a dedicated home. Live records and actions will be connected only through administrator-authorized APIs.</p>
        <div className="admin-quick-links">{navigation.filter(item => item.id !== "overview").slice(0, 4).map(item => <button key={item.id} onClick={() => window.dispatchEvent(new CustomEvent("travelos-admin-navigate", { detail: item.id }))}><span>{item.icon}</span>{item.label}<b>→</b></button>)}</div>
      </section>
    </div>
    <section className="admin-panel admin-activity-panel"><div className="admin-panel-heading"><div><span className="admin-kicker">RECENT OPERATIONS</span><h3>Activity overview</h3></div><span className="admin-muted-label">Live audit feed not connected</span></div><div className="admin-empty-state"><span>◷</span><strong>Your platform activity will appear here</strong><p>Once the audit-log endpoint is available, this space will show administrator actions and important platform events.</p></div></section>
  </>;
}

function PlaceholderSection({ section }: { section: Exclude<Section, "overview"> }) {
  const copy = pageCopy[section];
  const notes: Record<Exclude<Section, "overview">, string[]> = {
    users: ["Search and filter accounts", "Review roles and account status", "Suspend or restore accounts with an audit reason"],
    trips: ["Search trips by destination or owner", "Review reports and flagged content", "Inspect trip status without changing traveller data"],
    places: ["Review place source and coordinates", "Verify categories and opening-hour evidence", "Resolve duplicate or low-quality place records"],
    analytics: ["Traveller growth and active usage", "Trip creation and completion trends", "Recommendation acceptance and quality"],
    settings: ["Administrator access policy", "Provider configuration status", "Audit retention and security controls"],
  };
  return <section className="admin-panel admin-coming-panel"><div className="admin-coming-icon">{navigation.find(item => item.id === section)?.icon}</div><span className="admin-kicker">PHASE 01 FOUNDATION</span><h3>{copy.title} workspace is ready for integration</h3><p>{copy.description}</p><div className="admin-capability-list">{notes[section].map(note => <div key={note}><span>✓</span>{note}</div>)}</div><div className="admin-integration-callout"><strong>Why is this section empty?</strong><p>The current TravelOS backend does not expose these platform-wide admin endpoints yet. This interface intentionally avoids showing invented records or offering actions that are not securely backed by the API.</p></div></section>;
}

function AdminWorkspace({ user, onSignOut }: { user: AdminUser; onSignOut: () => void }) {
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
        {section === "overview" ? <Overview /> : <PlaceholderSection section={section} />}
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
  if (user && token) return <AdminWorkspace user={user} onSignOut={signOut} />;
  return <><AdminLogin onAuthenticated={authenticate} />{sessionError && <div className="admin-session-message" role="status">{sessionError}</div>}</>;
}
