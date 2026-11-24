type FlightsDashboardProps = {
  children: React.ReactNode;
};

export default function FlightsDashboard({ children }: FlightsDashboardProps) {
  return (
    <div className="flights-dashboard" aria-label="Flights and airport transfers">
      <header className="flights-dashboard-heading">
        <p className="eyebrow">TRAVEL OPERATIONS</p>
        <h2>Flights & airport transfers</h2>
        <p className="muted">Manage saved flight details, ground transport and connection checks in one place.</p>
      </header>
      {children}
    </div>
  );
}
