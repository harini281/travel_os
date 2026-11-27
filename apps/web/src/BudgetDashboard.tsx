import type { ReactNode } from "react";

type BudgetDashboardProps = {
  expenseCount: number;
  reportingCurrency: string;
  children: ReactNode;
};

export default function BudgetDashboard({
  expenseCount,
  reportingCurrency,
  children,
}: BudgetDashboardProps) {
  return (
    <div className="budget-dashboard" aria-label="Trip budget and shared expenses">
      <header className="budget-dashboard-heading">
        <p className="eyebrow">TRIP FINANCES</p>
        <h2>Budget & shared expenses</h2>
        <div className="budget-dashboard-summary">
          <span><strong>{expenseCount}</strong> recorded expenses</span>
          <span>Reporting in <strong>{reportingCurrency}</strong></span>
        </div>
      </header>
      {children}
    </div>
  );
}
