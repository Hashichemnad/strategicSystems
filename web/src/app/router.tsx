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

const uiKitRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/ui-kit',
  component: lazyRouteComponent(() => import('@/features/ui-kit/UiKitPage'), 'UiKitPage'),
});

export const profileRoute = createRoute({
  getParentRoute: () => appLayout,
  path: '/profile',
  validateSearch: z.object({
    tab: z.enum(['preferences', 'security', 'sessions', 'tokens', 'delegations']).catch('preferences'),
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

// Administration (system administrators, ADR 0009; the server enforces it)
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

const routeTree = rootRoute.addChildren([
  publicLayout.addChildren([loginRoute, mfaRoute, enrolRoute, forgotRoute, resetRoute]),
  appLayout.addChildren([
    homeRoute,
    sampleRoute,
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
