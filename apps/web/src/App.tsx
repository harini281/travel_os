export default function App() {
  return (
    <main className="shell">
      <nav className="nav">
        <a className="brand" href="/">travel<span>OS</span></a>
        <span className="tag">PRIVATE PREVIEW · FOUNDATION</span>
      </nav>
      <section className="hero">
        <p className="eyebrow">THE INTELLIGENT OPERATING SYSTEM FOR TRAVEL</p>
        <h1>Every journey,<br /><em>thoughtfully connected.</em></h1>
        <p className="intro">Discover, plan, coordinate and manage travel in one connected place. The TravelOS foundation is running.</p>
        <div className="actions">
          <a className="primary" href="http://localhost:8080/health" target="_blank" rel="noreferrer">Platform API health ↗</a>
          <a className="secondary" href="http://localhost:8001/docs" target="_blank" rel="noreferrer">AI API docs ↗</a>
        </div>
      </section>
      <section className="grid">
        <article><small>01 / PLAN</small><h2>Trips & itineraries</h2><p>Build a trip around dates, preferences, people and budget.</p></article>
        <article><small>02 / INTELLIGENCE</small><h2>AI travel assistant</h2><p>Structured suggestions, reviewed by travellers before approval.</p></article>
        <article><small>03 / COORDINATE</small><h2>Group travel</h2><p>Bring preferences, decisions and shared costs together.</p></article>
      </section>
      <footer>TravelOS · Stage 1 foundation · Demo content only</footer>
    </main>
  );
}
