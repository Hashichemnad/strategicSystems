import type { QueryClient } from '@tanstack/react-query';
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  Outlet,
  redirect,
} from '@tanstack/react-router';
import { z } from 'zod';
import { ApiError } from '@/api/client';
import { meQueryOptions } from '@/features/auth/session';
import { HomePage } from '@/features/home/HomePage';
import { NotFoundPage } from '@/features/home/NotFoundPage';
import { AppShell } from '@/layout/AppShell';
import { AuthLayout } from '@/layout/AuthLayout';

export interface RouterContext {
  queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  component: Outlet,
  notFoundComponent: NotFoundPage,
});

// ---- Public sign-in pages ------------------------------------------------------------------------------

const publicLayout = createRoute({ getParentRoute: () => rootRoute, id: 'public', component: AuthLayout });

const redirectParam = z.string().max(2000).optional().catch(undefined);
const methodsParam = z.string().max(100).optional().catch(undefined);

export const loginRoute = createRoute({
  getParentRoute: () => publicLayout,
  path: '/login',
  validateSearch: z.object({
    redirect: redirectParam,
    error: z.string().max(100).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/auth/LoginPage'), 'LoginPage'),
});

export const mfaRoute = createRoute({
  getParentRoute: () => publicLayout,
  path: '/login/mfa',
  validateSearch: z.object({ redirect: redirectParam, methods: methodsParam }),
  component: lazyRouteComponent(() => import('@/features/auth/MfaPage'), 'MfaPage'),
});

export const enrolRoute = createRoute({
  getParentRoute: () => publicLayout,
  path: '/login/enrol',
  validateSearch: z.object({ redirect: redirectParam, methods: methodsParam }),
  component: lazyRouteComponent(() => import('@/features/auth/EnrolPage'), 'EnrolPage'),
});

const forgotRoute = createRoute({
  getParentRoute: () => publicLayout,
  path: '/forgot-password',
  component: lazyRouteComponent(() => import('@/features/auth/ForgotPasswordPage'), 'ForgotPasswordPage'),
});

export const resetRoute = createRoute({
  getParentRoute: () => publicLayout,
  path: '/reset-password',
  validateSearch: z.object({
    user: z.string().max(64).catch(''),
    token: z.string().max(4096).catch(''),
    invite: z.coerce.number().optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/auth/ResetPasswordPage'), 'ResetPasswordPage'),
});

// ---- Signed-in application -----------------------------------------------------------------------------

/** Everything below needs a session: load the user once, or send them to the login page and back afterwards. */
const appLayout = createRoute({
  getParentRoute: () => rootRoute,
  id: 'app',
  component: AppShell,
  beforeLoad: async ({ context, location }) => {
    try {
      await context.queryClient.query({ ...meQueryOptions, staleTime: 'static' });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        // eslint-disable-next-line @typescript-eslint/only-throw-error -- TanStack Router redirects are thrown
        throw redirect({ to: '/login', search: { redirect: location.href } });
      }
      throw error;
    }
  },
});

const homeRoute = createRoute({ getParentRoute: () => appLayout, path: '/', component: HomePage });

/** OAuth consent for MCP clients (docs/design/mcp.md §2): a signed-in page in the sign-in layout. */
const consentLayout = createRoute({
  getParentRoute: () => rootRoute,
  id: 'oauth',
  component: AuthLayout,
  beforeLoad: appLayout.options.beforeLoad,
});

const oauthParam = z.string().max(2000).optional().catch(undefined);

export const consentRoute = createRoute({
  getParentRoute: () => consentLayout,
  path: '/oauth/consent',
  validateSearch: z.object({
    client_id: oauthParam,
    redirect_uri: oauthParam,
    response_type: oauthParam,
    scope: oauthParam,
    state: oauthParam,
    code_challenge: oauthParam,
    code_challenge_method: oauthParam,
    resource: oauthParam,
  }),
  component: lazyRouteComponent(() => import('@/features/mcp/ConsentPage'), 'ConsentPage'),
});

/** List state lives in typed search params, so views are shareable links (and saved views later). */
export const sampleSearchSchema = z.object({
  page: z.coerce.number().int().min(1).catch(1),
  search: z.string().max(200).optional().catch(undefined),
  status: z.enum(['active', 'inactive']).optional().catch(undefined),
  sort: z.string().max(50).catch('-createdAt'),
});

export type SampleSearch = z.infer<typeof sampleSearchSchema>;

export const sampleRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/sample',
  validateSearch: (search: Record<string, unknown>): SampleSearch =>
    sampleSearchSchema.parse({ page: 1, sort: '-createdAt', ...search }),
  // Each system's pages load on demand, so the shell stays small as modules are added.
  component: lazyRouteComponent(() => import('@/features/sample/SampleListPage'), 'SampleListPage'),
});

export const sampleItemRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/sample/items/$itemId',
  validateSearch: z.object({
    tab: z.enum(['overview', 'approvals', 'tasks', 'teams', 'documents', 'history']).catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/sample/SampleItemPage'), 'SampleItemPage'),
});

// Task Management (Phase 6, docs/design/tasks.md): My Tasks (list / Kanban / calendar), task page, assignment group, dashboard.
export const tasksSearchSchema = z.object({
  tab: z.enum(['assigned', 'created', 'follow-up', 'team']).catch('assigned'),
  view: z.enum(['list', 'kanban', 'calendar']).catch('list'),
  page: z.coerce.number().int().min(1).catch(1),
  sort: z.string().max(50).catch('dueDate'),
  search: z.string().max(200).optional().catch(undefined),
  status: z.string().max(50).optional().catch(undefined),
  priority: z.enum(['high', 'medium', 'low']).optional().catch(undefined),
  indicator: z.enum(['on_time', 'due_soon', 'overdue', 'on_hold']).optional().catch(undefined),
  openOnly: z.boolean().catch(true),
  month: z
    .string()
    .regex(/^\d{4}-\d{2}$/u)
    .optional()
    .catch(undefined),
  new: z.boolean().optional().catch(undefined),
});

export type TasksSearch = z.infer<typeof tasksSearchSchema>;

export const tasksRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/tasks',
  validateSearch: (search: Record<string, unknown>): TasksSearch =>
    tasksSearchSchema.parse({
      tab: 'assigned',
      view: 'list',
      page: 1,
      sort: 'dueDate',
      openOnly: true,
      ...search,
    }),
  component: lazyRouteComponent(() => import('@/features/tasks/TasksPage'), 'TasksPage'),
});

export const taskRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/tasks/$taskId',
  validateSearch: z.object({
    tab: z.enum(['overview', 'timeline', 'comments', 'documents', 'followUps', 'history']).catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/tasks/TaskPage'), 'TaskPage'),
});

export const taskGroupRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/tasks/groups/$groupId',
  component: lazyRouteComponent(() => import('@/features/tasks/TaskGroupPage'), 'TaskGroupPage'),
});

export const taskDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/tasks/dashboard',
  component: lazyRouteComponent(() => import('@/features/tasks/TaskDashboardPage'), 'TaskDashboardPage'),
});

// Teams Management (Phase 7, docs/design/teams.md): register, formation request, team page, meeting, evaluation, dashboard.
export const teamsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams',
  validateSearch: z.object({
    view: z.enum(['all', 'mine', 'leading', 'pending-evaluation']).catch('all'),
    page: z.coerce.number().int().min(1).catch(1),
    type: z.string().max(100).optional().catch(undefined),
    status: z.string().max(50).optional().catch(undefined),
    search: z.string().max(200).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/teams/TeamsPage'), 'TeamsPage'),
});

export const teamNewRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/new',
  validateSearch: z.object({
    linkedType: z.string().max(100).optional().catch(undefined),
    linkedId: z.uuid().optional().catch(undefined),
    type: z.string().max(100).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/teams/TeamFormPage'), 'TeamFormPage'),
});

export const teamDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/dashboard',
  component: lazyRouteComponent(() => import('@/features/teams/TeamDashboardPage'), 'TeamDashboardPage'),
});

export const teamRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/$teamId',
  validateSearch: z.object({
    tab: z
      .enum([
        'overview',
        'members',
        'meetings',
        'recommendations',
        'tasks',
        'reports',
        'documents',
        'evaluations',
        'approvals',
        'history',
      ])
      .catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/teams/TeamPage'), 'TeamPage'),
});

export const teamEditRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/$teamId/edit',
  component: lazyRouteComponent(() => import('@/features/teams/TeamFormPage'), 'TeamFormPage'),
});

export const teamMeetingRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/meetings/$meetingId',
  component: lazyRouteComponent(() => import('@/features/teams/MeetingPage'), 'MeetingPage'),
});

export const teamEvaluationRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/evaluations/$evaluationId',
  component: lazyRouteComponent(() => import('@/features/teams/EvaluationPage'), 'EvaluationPage'),
});

export const teamRecommendationRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/teams/recommendations/$recommendationId',
  component: lazyRouteComponent(
    () => import('@/features/teams/RecommendationRedirect'),
    'RecommendationRedirect',
  ),
});

// Portfolio Management (Phase 8, docs/design/portfolio.md): program requests, the request wizard, projects / initiatives.
export const portfolioRequestsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/requests',
  validateSearch: z.object({
    view: z.enum(['all', 'mine', 'in-approval', 'returned']).catch('all'),
    page: z.coerce.number().int().min(1).catch(1),
    status: z.string().max(50).optional().catch(undefined),
    classification: z.string().max(50).optional().catch(undefined),
    band: z.string().max(60).optional().catch(undefined),
    search: z.string().max(200).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/portfolio/RequestsPage'), 'RequestsPage'),
});

export const portfolioRequestNewRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/requests/new',
  validateSearch: z.object({ mode: z.enum(['manual', 'document']).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/portfolio/RequestFormPage'), 'RequestFormPage'),
});

export const portfolioRequestRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/requests/$requestId',
  validateSearch: z.object({
    tab: z
      .enum(['overview', 'assessment', 'approvals', 'files', 'documents', 'tasks', 'history'])
      .catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/portfolio/RequestPage'), 'RequestPage'),
});

export const portfolioRequestEditRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/requests/$requestId/edit',
  component: lazyRouteComponent(() => import('@/features/portfolio/RequestFormPage'), 'RequestFormPage'),
});

export const portfolioProjectsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/projects',
  validateSearch: z.object({
    page: z.coerce.number().int().min(1).catch(1),
    classification: z.string().max(50).optional().catch(undefined),
    search: z.string().max(200).optional().catch(undefined),
    view: z.enum(['all', 'mine', 'unclaimed']).optional().catch(undefined),
    status: z.string().max(50).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/portfolio/ProjectPages'), 'ProjectsPage'),
});

export const portfolioProjectRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/projects/$projectId',
  validateSearch: z.object({ tab: z.string().max(40).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/portfolio/project/ProjectPage'), 'ProjectPage'),
});

// Portfolio views (Phase 10, docs/design/portfolio.md §36, §38, §35): dashboard, portfolio Gantt, portfolios & programmes.
export const portfolioDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/dashboard',
  component: lazyRouteComponent(
    () => import('@/features/portfolio/views/PortfolioDashboardPage'),
    'PortfolioDashboardPage',
  ),
});

export const portfolioGanttRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/gantt',
  component: lazyRouteComponent(
    () => import('@/features/portfolio/views/PortfolioGanttPage'),
    'PortfolioGanttPage',
  ),
});

export const portfolioGroupsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/portfolios',
  component: lazyRouteComponent(
    () => import('@/features/portfolio/views/PortfolioGroupsPage'),
    'PortfolioGroupsPage',
  ),
});

// Phase 11 (docs/design/portfolio.md §58–§62): vendor register, capacity view, quarterly portfolio reviews.
export const portfolioVendorsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/vendors',
  component: lazyRouteComponent(() => import('@/features/portfolio/vendors/VendorsPage'), 'VendorsPage'),
});

export const portfolioVendorRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/vendors/$vendorId',
  component: lazyRouteComponent(() => import('@/features/portfolio/vendors/VendorPage'), 'VendorPage'),
});

export const portfolioCapacityRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/capacity',
  component: lazyRouteComponent(() => import('@/features/portfolio/governance/CapacityPage'), 'CapacityPage'),
});

export const portfolioReviewsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/reviews',
  component: lazyRouteComponent(() => import('@/features/portfolio/governance/ReviewsPage'), 'ReviewsPage'),
});

export const portfolioReviewRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/portfolio/reviews/$reviewId',
  component: lazyRouteComponent(() => import('@/features/portfolio/governance/ReviewPage'), 'ReviewPage'),
});

// Wasel vendor portal (Phase 11, ADR 0110): its own area, layout and session; never the internal app shell.
const waselRoot = createRoute({
  getParentRoute: () => rootRoute,
  id: 'wasel',
  component: lazyRouteComponent(() => import('@/features/wasel/WaselLayout'), 'WaselLayout'),
});

export const waselLoginRoute = createRoute({
  getParentRoute: () => waselRoot,
  path: '/wasel/login',
  validateSearch: z.object({ redirect: redirectParam }),
  component: lazyRouteComponent(() => import('@/features/wasel/WaselAuthPages'), 'WaselLoginPage'),
});

export const waselMfaRoute = createRoute({
  getParentRoute: () => waselRoot,
  path: '/wasel/login/mfa',
  validateSearch: z.object({ redirect: redirectParam, methods: methodsParam, enrol: z.coerce.number().optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/wasel/WaselAuthPages'), 'WaselMfaPage'),
});

export const waselSetPasswordRoute = createRoute({
  getParentRoute: () => waselRoot,
  path: '/wasel/set-password',
  validateSearch: z.object({
    user: z.string().max(64).catch(''),
    token: z.string().max(4096).catch(''),
    invite: z.coerce.number().optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/wasel/WaselAuthPages'), 'WaselSetPasswordPage'),
});

export const waselHomeRoute = createRoute({
  getParentRoute: () => waselRoot,
  path: '/wasel',
  component: lazyRouteComponent(() => import('@/features/wasel/WaselPortalPages'), 'WaselHomePage'),
});

export const waselProjectRoute = createRoute({
  getParentRoute: () => waselRoot,
  path: '/wasel/projects/$projectId',
  validateSearch: z.object({ tab: z.string().max(40).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/wasel/WaselPortalPages'), 'WaselProjectPage'),
});

// Performance Management (Phase 12, docs/design/kpi.md): dashboard, cards, new year, plans, results, approvals, scorecards, map, reports.
export const kpiDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi',
  component: lazyRouteComponent(() => import('@/features/kpi/DashboardPage'), 'KpiDashboardPage'),
});

export const kpiCardsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/cards',
  validateSearch: z.object({
    page: z.coerce.number().int().min(1).optional().catch(undefined),
    search: z.string().max(200).optional().catch(undefined),
    unitId: z.uuid().optional().catch(undefined),
    type: z.string().max(100).optional().catch(undefined),
    objective: z.uuid().optional().catch(undefined),
    status: z.string().max(20).optional().catch(undefined),
    gem: z.string().max(100).optional().catch(undefined),
    star: z.boolean().optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/kpi/CardsPage'), 'KpiCardsPage'),
});

export const kpiCardNewRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/cards/new',
  component: lazyRouteComponent(() => import('@/features/kpi/CardFormPage'), 'KpiCardFormPage'),
});

export const kpiCardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/cards/$cardId',
  validateSearch: z.object({
    tab: z
      .enum([
        'details',
        'measuring',
        'alignment',
        'plans',
        'performance',
        'documents',
        'linked',
        'versions',
        'tasks',
        'history',
      ])
      .optional()
      .catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/kpi/CardPage'), 'KpiCardPage'),
});

export const kpiCardEditRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/cards/$cardId/edit',
  component: lazyRouteComponent(() => import('@/features/kpi/CardFormPage'), 'KpiCardFormPage'),
});

export const kpiNewYearRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/new-year',
  component: lazyRouteComponent(() => import('@/features/kpi/NewYearPage'), 'KpiNewYearPage'),
});

export const kpiPlanRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/plans/$planId',
  component: lazyRouteComponent(() => import('@/features/kpi/PlanPage'), 'KpiPlanPage'),
});

export const kpiPendingRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/pending',
  component: lazyRouteComponent(() => import('@/features/kpi/PendingPage'), 'KpiPendingPage'),
});

export const kpiResultRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/results/$resultId',
  validateSearch: z.object({
    tab: z.enum(['result', 'review', 'approvals', 'tasks', 'history']).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/kpi/ResultPage'), 'KpiResultPage'),
});

export const kpiApprovalsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/approvals',
  validateSearch: z.object({ tab: z.enum(['queue', 'bulk']).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/kpi/ApprovalsPage'), 'KpiApprovalsPage'),
});

export const kpiScorecardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/scorecard',
  component: lazyRouteComponent(() => import('@/features/kpi/ScorecardPage'), 'KpiScorecardPage'),
});

export const kpiStrategyMapRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/strategy-map',
  component: lazyRouteComponent(() => import('@/features/kpi/StrategyMapPage'), 'KpiStrategyMapPage'),
});

export const kpiReportsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/kpi/reports',
  component: lazyRouteComponent(() => import('@/features/kpi/ReportsPage'), 'KpiReportsPage'),
});

// Risk Management (Phase 13, docs/design/risk.md §12): dashboard, register, risk page and wizard, my actions, project risks, mechanism, reports.
export const riskDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk',
  component: lazyRouteComponent(() => import('@/features/risk/DashboardPage'), 'RiskDashboardPage'),
});

export const riskRegisterRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/risks',
  validateSearch: z.object({
    page: z.coerce.number().int().min(1).optional().catch(undefined),
    view: z
      .enum([
        'all',
        'mine',
        'my-unit',
        'top10',
        'outside-appetite',
        'overdue-review',
        'under-review',
        'escalated',
        'reassessment',
      ])
      .optional()
      .catch(undefined),
    search: z.string().max(200).optional().catch(undefined),
    unitId: z.uuid().optional().catch(undefined),
    category: z.string().max(100).optional().catch(undefined),
    status: z.string().max(40).optional().catch(undefined),
    band: z.string().max(100).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/risk/RegisterPage'), 'RiskRegisterPage'),
});

export const riskNewRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/risks/new',
  component: lazyRouteComponent(() => import('@/features/risk/RiskWizardPage'), 'RiskWizardPage'),
});

export const riskRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/risks/$riskId',
  validateSearch: z.object({
    tab: z
      .enum([
        'overview',
        'assessment',
        'controls',
        'actions',
        'kris',
        'events',
        'reviews',
        'documents',
        'activity',
      ])
      .optional()
      .catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/risk/RiskPage'), 'RiskPage'),
});

export const riskEditRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/risks/$riskId/edit',
  component: lazyRouteComponent(() => import('@/features/risk/RiskWizardPage'), 'RiskWizardPage'),
});

export const riskMyActionsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/my-actions',
  validateSearch: z.object({ action: z.uuid().optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/risk/MyActionsPage'), 'RiskMyActionsPage'),
});

// Links registry target of a treatment action (task-able records): its owner's quick-update list.
export const riskActionRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/actions/$actionId',
  beforeLoad: ({ params }) => {
    // eslint-disable-next-line @typescript-eslint/only-throw-error -- TanStack Router redirects are thrown
    throw redirect({ to: '/risk/my-actions', search: { action: params.actionId } });
  },
});

export const riskProjectRisksRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/project-risks',
  component: lazyRouteComponent(() => import('@/features/risk/ProjectRisksPage'), 'RiskProjectRisksPage'),
});

export const riskMechanismRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/mechanism',
  component: lazyRouteComponent(() => import('@/features/risk/MechanismPage'), 'RiskMechanismPage'),
});

export const riskReportsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/risk/reports',
  component: lazyRouteComponent(() => import('@/features/risk/ReportsPage'), 'RiskReportsPage'),
});

// Request Framework, Document Registry, IP Registry (Phase 14, docs/design/requests.md).
export const requestCatalogueRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/requests',
  component: lazyRouteComponent(() => import('@/features/requests/CataloguePage'), 'RequestCataloguePage'),
});

export const requestNewRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/requests/new/$typeCode',
  /** <c>values</c>: JSON object of field values to prefill (e.g. the existing document of a new version). */
  validateSearch: z.object({ values: z.string().max(4000).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/requests/WizardPage'), 'RequestWizardPage'),
});

export const myRequestsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/requests/mine',
  component: lazyRouteComponent(() => import('@/features/requests/MyRequestsPage'), 'MyRequestsPage'),
});

export const requestQueueRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/requests/queue',
  component: lazyRouteComponent(() => import('@/features/requests/QueuePage'), 'RequestQueuePage'),
});

export const requestRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/requests/$requestId',
  validateSearch: z.object({
    tab: z.enum(['overview', 'approvals', 'timeline', 'documents', 'tasks', 'history']).catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/requests/RequestPage'), 'RequestPage'),
});

export const requestEditRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/requests/$requestId/edit',
  component: lazyRouteComponent(() => import('@/features/requests/WizardPage'), 'RequestEditPage'),
});

/** Chrome-less submit page for the intranet (ADR 0088): the same sign-in, no sidebar. */
const embedLayout = createRoute({
  getParentRoute: () => rootRoute,
  id: 'embed',
  component: Outlet,
  beforeLoad: appLayout.options.beforeLoad,
});

export const embedRequestRoute = createRoute({
  getParentRoute: () => embedLayout,
  path: '/embed/requests/$typeCode',
  validateSearch: z.object({ values: z.string().max(4000).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/requests/EmbedPage'), 'EmbedRequestPage'),
});

export const documentLibraryRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/documents',
  component: lazyRouteComponent(() => import('@/features/docreg/LibraryPage'), 'DocumentLibraryPage'),
});

export const documentDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/documents/dashboard',
  component: lazyRouteComponent(() => import('@/features/docreg/DashboardPage'), 'DocumentDashboardPage'),
});

export const documentRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/documents/$documentId',
  validateSearch: z.object({
    tab: z.enum(['overview', 'versions', 'validity', 'linked', 'tasks', 'history']).catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/docreg/DocumentPage'), 'DocumentPage'),
});

export const documentCompareRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/documents/$documentId/compare',
  validateSearch: z.object({
    from: z.uuid().optional().catch(undefined),
    to: z.uuid().optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/docreg/ComparePage'), 'DocumentComparePage'),
});

export const ipRegisterRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/ip',
  component: lazyRouteComponent(() => import('@/features/ip/RegisterPage'), 'IpRegisterPage'),
});

export const ipDashboardRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/ip/dashboard',
  component: lazyRouteComponent(() => import('@/features/ip/DashboardPage'), 'IpDashboardPage'),
});

export const ipRecordRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/ip/records/$recordId',
  validateSearch: z.object({
    tab: z
      .enum(['overview', 'contributors', 'registration', 'documents', 'linked', 'history'])
      .catch('overview'),
  }),
  component: lazyRouteComponent(() => import('@/features/ip/RecordPage'), 'IpRecordPage'),
});

// Audit trail (CLAUDE.md §8): global log for administrators and auditors, and the chain verification screen.
export const auditSearchSchema = z.object({
  page: z.coerce.number().int().min(1).catch(1),
  text: z.string().max(200).optional().catch(undefined),
  system: z.string().max(50).optional().catch(undefined),
  entityType: z.string().max(150).optional().catch(undefined),
  recordId: z.string().max(200).optional().catch(undefined),
  action: z.string().max(100).optional().catch(undefined),
  channel: z.string().max(20).optional().catch(undefined),
  from: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/u)
    .optional()
    .catch(undefined),
  to: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/u)
    .optional()
    .catch(undefined),
});

const auditRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/audit',
  validateSearch: (search: Record<string, unknown>) => auditSearchSchema.parse({ page: 1, ...search }),
  component: lazyRouteComponent(() => import('@/features/audit/AuditLogPage'), 'AuditLogPage'),
});

const auditVerificationRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/audit/verification',
  component: lazyRouteComponent(() => import('@/features/audit/VerificationPage'), 'VerificationPage'),
});

export const notificationsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/notifications',
  validateSearch: z.object({ page: z.coerce.number().int().min(1).catch(1) }),
  component: lazyRouteComponent(
    () => import('@/features/notifications/NotificationsPage'),
    'NotificationsPage',
  ),
});

const uiKitRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/ui-kit',
  component: lazyRouteComponent(() => import('@/features/ui-kit/UiKitPage'), 'UiKitPage'),
});

export const profileRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/profile',
  validateSearch: z.object({
    tab: z
      .enum(['preferences', 'notifications', 'security', 'sessions', 'tokens', 'delegations'])
      .catch('preferences'),
  }),
  component: lazyRouteComponent(() => import('@/features/profile/ProfilePage'), 'ProfilePage'),
});

// Organisation
export const unitsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/org/units',
  validateSearch: z.object({ unit: z.string().max(64).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/org/UnitsPage'), 'UnitsPage'),
});

export const chartRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/org/chart',
  validateSearch: z.object({
    asOf: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}$/u)
      .optional()
      .catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/org/OrgChartPage'), 'OrgChartPage'),
});

export const employeesSearchSchema = z.object({
  page: z.coerce.number().int().min(1).catch(1),
  search: z.string().max(200).optional().catch(undefined),
  status: z.enum(['active', 'on_leave', 'left']).optional().catch(undefined),
  unitId: z.string().max(64).optional().catch(undefined),
});

export const employeesRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/org/employees',
  validateSearch: (search: Record<string, unknown>) => employeesSearchSchema.parse({ page: 1, ...search }),
  component: lazyRouteComponent(() => import('@/features/org/EmployeesPage'), 'EmployeesPage'),
});

export const employeeRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/org/employees/$employeeId',
  component: lazyRouteComponent(() => import('@/features/org/EmployeePage'), 'EmployeePage'),
});

const levelsRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/org/levels',
  component: lazyRouteComponent(() => import('@/features/org/LevelsPage'), 'LevelsPage'),
});

const importRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/org/import',
  component: lazyRouteComponent(() => import('@/features/org/ImportPage'), 'ImportPage'),
});

// Administration (identity permissions, ADR 0016; the server enforces them)
export const usersRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/admin/users',
  validateSearch: z.object({
    page: z.coerce.number().int().min(1).catch(1),
    search: z.string().max(200).optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/admin/UsersPage'), 'UsersPage'),
});

const providersRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/admin/providers',
  component: lazyRouteComponent(() => import('@/features/admin/ProvidersPage'), 'ProvidersPage'),
});

const securityRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/admin/security',
  component: lazyRouteComponent(
    () => import('@/features/admin/SecuritySettingsPage'),
    'SecuritySettingsPage',
  ),
});

const emailRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/admin/email',
  component: lazyRouteComponent(() => import('@/features/admin/EmailSettingsPage'), 'EmailSettingsPage'),
});

// Settings centre (docs/design/settings.md): pages are listed by permission; the server checks every call.
const settingsLayout = createRoute({
  getParentRoute: () => appLayout,
  path: '/settings',
  component: lazyRouteComponent(() => import('@/features/settings/SettingsLayout'), 'SettingsLayout'),
});

const settingsHubRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/',
  component: lazyRouteComponent(() => import('@/features/settings/SettingsLayout'), 'SettingsHubPage'),
});

const regionalRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/regional',
  component: lazyRouteComponent(() => import('@/features/settings/GeneralPages'), 'RegionalPage'),
});

const brandingRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/branding',
  component: lazyRouteComponent(() => import('@/features/settings/GeneralPages'), 'BrandingPage'),
});

const systemsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/systems',
  component: lazyRouteComponent(() => import('@/features/settings/GeneralPages'), 'SystemsPage'),
});

const featuresRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/features',
  component: lazyRouteComponent(() => import('@/features/settings/GeneralPages'), 'FeaturesPage'),
});

const lookupsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/lookups',
  component: lazyRouteComponent(() => import('@/features/settings/DataPages'), 'LookupsPage'),
});

const numberingRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/numbering',
  component: lazyRouteComponent(() => import('@/features/settings/DataPages'), 'NumberingPage'),
});

const translationsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/translations',
  component: lazyRouteComponent(() => import('@/features/settings/DataPages'), 'TranslationsPage'),
});

const notificationTemplatesRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/notifications',
  component: lazyRouteComponent(() => import('@/features/notifications/TemplatesPage'), 'TemplatesPage'),
});

const filesSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/files',
  component: lazyRouteComponent(() => import('@/features/files/FilesSettingsPage'), 'FilesSettingsPage'),
});

const jobsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/jobs',
  component: lazyRouteComponent(() => import('@/features/jobs/JobsPage'), 'JobsPage'),
});

// Business hours and holidays (ADR 0027).
const businessHoursRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/business-hours',
  component: lazyRouteComponent(() => import('@/features/calendar/BusinessHoursPage'), 'BusinessHoursPage'),
});

const holidaysRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/holidays',
  component: lazyRouteComponent(() => import('@/features/calendar/HolidaysPage'), 'HolidaysPage'),
});

// Approval Settings centre (SPEC §0.2): every approval process of every system, its steps, preview and versions.
const approvalSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/approvals',
  component: lazyRouteComponent(
    () => import('@/features/workflow/ApprovalSettingsPage'),
    'ApprovalSettingsPage',
  ),
});

export const processEditorRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/approvals/$processKey',
  validateSearch: z.object({ version: z.coerce.number().int().min(1).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/workflow/ProcessEditorPage'), 'ProcessEditorPage'),
});

// AI platform and MCP server (Phase 5, docs/design/ai.md, docs/design/mcp.md).
const aiSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/ai',
  component: lazyRouteComponent(() => import('@/features/ai/AiSettingsPage'), 'AiSettingsPage'),
});

const teamSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/teams',
  component: lazyRouteComponent(() => import('@/features/teams/TeamSettingsPage'), 'TeamSettingsPage'),
});

const portfolioSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/portfolio',
  component: lazyRouteComponent(
    () => import('@/features/portfolio/PortfolioSettingsPage'),
    'PortfolioSettingsPage',
  ),
});

const kpiSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/kpi',
  component: lazyRouteComponent(() => import('@/features/kpi/KpiSettingsPage'), 'KpiSettingsPage'),
});

const riskSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/risk',
  component: lazyRouteComponent(
    () => import('@/features/risk/RiskFrameworkSettingsPage'),
    'RiskFrameworkSettingsPage',
  ),
});

const requestTypesRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/request-types',
  component: lazyRouteComponent(() => import('@/features/requests/TypesPage'), 'RequestTypesPage'),
});

const requestTypeBuilderRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/request-types/$code',
  component: lazyRouteComponent(() => import('@/features/requests/BuilderPage'), 'RequestTypeBuilderPage'),
});

const docregSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/docreg',
  component: lazyRouteComponent(() => import('@/features/docreg/SettingsPage'), 'DocRegSettingsPage'),
});

const ipSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/ip',
  component: lazyRouteComponent(() => import('@/features/ip/SettingsPage'), 'IpSettingsPage'),
});

const taskSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/tasks',
  component: lazyRouteComponent(() => import('@/features/tasks/TaskSettingsPage'), 'TaskSettingsPage'),
});

export const taskReassignRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/tasks/reassign',
  validateSearch: z.object({ employee: z.uuid().optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/tasks/ReassignTasksPage'), 'ReassignTasksPage'),
});

const mcpSettingsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/mcp',
  component: lazyRouteComponent(() => import('@/features/mcp/McpSettingsPage'), 'McpSettingsPage'),
});

const systemAccessRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/access/system-access',
  component: lazyRouteComponent(() => import('@/features/access/AccessPages'), 'SystemAccessPage'),
});

const transferRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/access/transfer',
  component: lazyRouteComponent(() => import('@/features/access/AccessPages'), 'TransferPage'),
});

const reviewRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/access/review',
  component: lazyRouteComponent(() => import('@/features/access/AccessPages'), 'ReviewPage'),
});

export const rolesRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/access/roles',
  validateSearch: z.object({ system: z.string().max(64).optional().catch(undefined) }),
  component: lazyRouteComponent(() => import('@/features/access/RolesPage'), 'RolesPage'),
});

export const assignmentsRoute = createRoute({
  getParentRoute: () => settingsLayout,
  path: '/access/assignments',
  validateSearch: z.object({
    roleId: z.string().max(64).optional().catch(undefined),
    userId: z.string().max(64).optional().catch(undefined),
    includeEnded: z.boolean().optional().catch(undefined),
  }),
  component: lazyRouteComponent(() => import('@/features/access/AccessPages'), 'AssignmentsPage'),
});

const routeTree = rootRoute.addChildren([
  publicLayout.addChildren([loginRoute, mfaRoute, enrolRoute, forgotRoute, resetRoute]),
  consentLayout.addChildren([consentRoute]),
  embedLayout.addChildren([embedRequestRoute]),
  waselRoot.addChildren([waselLoginRoute, waselMfaRoute, waselSetPasswordRoute, waselHomeRoute, waselProjectRoute]),
  appLayout.addChildren([
    homeRoute,
    sampleRoute,
    sampleItemRoute,
    tasksRoute,
    taskRoute,
    taskGroupRoute,
    taskDashboardRoute,
    teamsRoute,
    teamNewRoute,
    teamDashboardRoute,
    teamRoute,
    teamEditRoute,
    teamMeetingRoute,
    teamEvaluationRoute,
    teamRecommendationRoute,
    portfolioRequestsRoute,
    portfolioRequestNewRoute,
    portfolioRequestRoute,
    portfolioRequestEditRoute,
    portfolioProjectsRoute,
    portfolioProjectRoute,
    portfolioDashboardRoute,
    portfolioGanttRoute,
    portfolioGroupsRoute,
    portfolioVendorsRoute,
    portfolioVendorRoute,
    portfolioCapacityRoute,
    portfolioReviewsRoute,
    portfolioReviewRoute,
    kpiDashboardRoute,
    kpiCardsRoute,
    kpiCardNewRoute,
    kpiCardRoute,
    kpiCardEditRoute,
    kpiNewYearRoute,
    kpiPlanRoute,
    kpiPendingRoute,
    kpiResultRoute,
    kpiApprovalsRoute,
    kpiScorecardRoute,
    kpiStrategyMapRoute,
    kpiReportsRoute,
    riskDashboardRoute,
    riskRegisterRoute,
    riskNewRoute,
    riskRoute,
    riskEditRoute,
    riskMyActionsRoute,
    riskActionRoute,
    riskProjectRisksRoute,
    riskMechanismRoute,
    riskReportsRoute,
    requestCatalogueRoute,
    requestNewRoute,
    myRequestsRoute,
    requestQueueRoute,
    requestRoute,
    requestEditRoute,
    documentLibraryRoute,
    documentDashboardRoute,
    documentRoute,
    documentCompareRoute,
    ipRegisterRoute,
    ipDashboardRoute,
    ipRecordRoute,
    auditRoute,
    auditVerificationRoute,
    notificationsRoute,
    uiKitRoute,
    profileRoute,
    unitsRoute,
    chartRoute,
    employeesRoute,
    employeeRoute,
    levelsRoute,
    importRoute,
    usersRoute,
    providersRoute,
    securityRoute,
    emailRoute,
    settingsLayout.addChildren([
      settingsHubRoute,
      regionalRoute,
      brandingRoute,
      systemsRoute,
      featuresRoute,
      lookupsRoute,
      numberingRoute,
      translationsRoute,
      notificationTemplatesRoute,
      filesSettingsRoute,
      jobsRoute,
      businessHoursRoute,
      holidaysRoute,
      approvalSettingsRoute,
      processEditorRoute,
      aiSettingsRoute,
      mcpSettingsRoute,
      taskSettingsRoute,
      teamSettingsRoute,
      portfolioSettingsRoute,
      kpiSettingsRoute,
      riskSettingsRoute,
      requestTypesRoute,
      requestTypeBuilderRoute,
      docregSettingsRoute,
      ipSettingsRoute,
      taskReassignRoute,
      rolesRoute,
      assignmentsRoute,
      systemAccessRoute,
      transferRoute,
      reviewRoute,
    ]),
  ]),
]);

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  scrollRestoration: true,
  // Provided by <App /> (RouterProvider context); the placeholder only satisfies the type.
  context: { queryClient: undefined as unknown as QueryClient },
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
