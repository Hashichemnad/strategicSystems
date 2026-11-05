import { getRouteApi } from '@tanstack/react-router';
import { DashboardBuilderPage } from './DashboardBuilderPage';
import { DashboardPage } from './DashboardPage';
import { ReportBuilderPage } from './ReportBuilderPage';

const dashboardApi = getRouteApi('/app/dashboards/$key');
const reportApi = getRouteApi('/app/reports/$reportId');

export function DashboardRoutePage() {
  const { key } = dashboardApi.useParams();
  const search = dashboardApi.useSearch();
  return <DashboardPage key={key} dashboardKey={key} search={search} />;
}

export function NewReportRoutePage() {
  return <ReportBuilderPage />;
}

export function ReportRoutePage() {
  const { reportId } = reportApi.useParams();
  return <ReportBuilderPage key={reportId} reportId={reportId} />;
}

export function DashboardBuilderRoutePage() {
  return <DashboardBuilderPage />;
}
