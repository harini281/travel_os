type TripOverviewProps = {
  tripDates: string;
  currencyCode: string;
  role: string;
  plannedDays: number;
  savedActivities: number;
  travellers: number;
  onNavigate: (page: string) => void;
};

export default function TripOverview({
  tripDates,
  currencyCode,
  role,
  plannedDays,
  savedActivities,
  travellers,
  onNavigate,
}: TripOverviewProps) {
  return (
    <section className="trip-overview-panel" aria-label="Trip overview summary">
      <div className="trip-overview-stats">
        <article>
          <span>TRIP DATES</span>
          <strong>{tripDates}</strong>
          <small>{currencyCode} · {role}</small>
        </article>
        <article>
          <span>ITINERARY</span>
          <strong>{plannedDays} days planned</strong>
          <small>{savedActivities} activities saved</small>
        </article>
        <article>
          <span>TRAVELLERS</span>
          <strong>{travellers}</strong>
          <small>People in this journey</small>
        </article>
      </div>
      <div className="trip-quick-actions">
        <button type="button" onClick={() => onNavigate("itinerary")}>
          <span aria-hidden="true">▤</span>
          <strong>Plan your days</strong>
          <small>Activities, maps and schedule</small>
          <b>Open planner →</b>
        </button>
        <button type="button" onClick={() => onNavigate("flights")}>
          <span aria-hidden="true">✈</span>
          <strong>Manage transport</strong>
          <small>Flights and airport transfers</small>
          <b>View transport →</b>
        </button>
        <button type="button" onClick={() => onNavigate("group")}>
          <span aria-hidden="true">♧</span>
          <strong>Invite your group</strong>
          <small>Travellers and shared decisions</small>
          <b>Manage group →</b>
        </button>
        <button type="button" onClick={() => onNavigate("budget")}>
          <span aria-hidden="true">＄</span>
          <strong>Track spending</strong>
          <small>Budget and shared expenses</small>
          <b>Open finances →</b>
        </button>
      </div>
    </section>
  );
}
