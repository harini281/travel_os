type ItineraryDashboardProps = {
  plannedDays: number;
  activityCount: number;
  canEdit: boolean;
  busy: boolean;
  hasActivities: boolean;
  onAddDay: () => void;
  onApprove: () => void;
};

export default function ItineraryDashboard({
  plannedDays,
  activityCount,
  canEdit,
  busy,
  hasActivities,
  onAddDay,
  onApprove,
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
            <button type="button" className="btn btn-quiet" onClick={onAddDay}>
              ＋ Add a day
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy || !hasActivities}
              onClick={onApprove}
            >
              Approve plan
            </button>
          </div>
        )}
      </div>
    </section>
  );
}
