import type { ReactNode } from "react";

type GroupDashboardProps = {
  memberCount: number;
  openPollCount: number;
  completedPreferenceCount: number;
  children: ReactNode;
};

export default function GroupDashboard({
  memberCount,
  openPollCount,
  completedPreferenceCount,
  children,
}: GroupDashboardProps) {
  return (
    <div className="group-dashboard" aria-label="Trip group dashboard">
      <header className="group-dashboard-heading">
        <p className="eyebrow">TRAVEL TOGETHER</p>
        <h2>Your travel group</h2>
        <div className="group-dashboard-summary">
          <span><strong>{memberCount}</strong> travellers</span>
          <span><strong>{completedPreferenceCount}</strong> preferences shared</span>
          <span><strong>{openPollCount}</strong> open decisions</span>
        </div>
      </header>
      {children}
    </div>
  );
}
