type ItineraryDashboardProps = {
  plannedDays: number;
  activityCount: number;
  canEdit: boolean;
  busy: boolean;
  hasActivities: boolean;
  showAddDay: boolean;
  onToggleAddDay: () => void;
  onApprove: () => void;
  onCancelAddDay: () => void;
  children: React.ReactNode;
};

export default function ItineraryDashboard({
  plannedDays,
  activityCount,
  canEdit,
  busy,
  hasActivities,
  showAddDay,
  onToggleAddDay,
  onApprove,
  onCancelAddDay,
  children,
}: ItineraryDashboardProps) {
  return (
    <section className="itinerary-dashboard" aria-label="Itinerary dashboard">
      <div className="itinerary-toolbar">
        <div>
          <strong>{plannedDays} planned days</strong>
          <span>{activityCount} activities</span>
        </div>
        {canEdit && (
          <div className="toolbar-actions">
            <button type="button" className="btn btn-quiet" onClick={onToggleAddDay}>
              ＋ Add a day
            </button>
            <button type="button" className="btn btn-primary" disabled={busy || !hasActivities} onClick={onApprove}>
              Approve plan
            </button>
          </div>
        )}
      </div>
      {showAddDay && (
        <div className="itinerary-add-day-slot">
          {children}
          <button type="button" className="btn btn-quiet" onClick={onCancelAddDay}>Close add-day form</button>
        </div>
      )}
    </section>
  );
}
