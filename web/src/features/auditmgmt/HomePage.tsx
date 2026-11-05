import { Skeleton } from '@/ui';
import { useAuditOptions } from './api';
import { AuditDashboardPage } from './DashboardPage';
import { AuditDepartmentViewPage } from './DepartmentViewPage';

/** Entry of Audit Management: the dashboard for the Audit Office and leadership, the read-only department view for everyone else. */
export function AuditHomePage() {
  const options = useAuditOptions();
  if (!options.data) return <Skeleton className="h-96 rounded-lg" />;
  return options.data.canDashboard ? <AuditDashboardPage /> : <AuditDepartmentViewPage />;
}
