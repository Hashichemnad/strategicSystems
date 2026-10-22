import { createRootRoute, createRoute, createRouter, lazyRouteComponent } from '@tanstack/react-router';
import { z } from 'zod';
import { HomePage } from '@/features/home/HomePage';
import { NotFoundPage } from '@/features/home/NotFoundPage';
import { AppShell } from '@/layout/AppShell';

const rootRoute = createRootRoute({ component: AppShell, notFoundComponent: NotFoundPage });

const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: HomePage });

/** List state lives in typed search params, so views are shareable links (and saved views later). */
export const sampleSearchSchema = z.object({
  page: z.coerce.number().int().min(1).catch(1),
  search: z.string().max(200).optional().catch(undefined),
  status: z.enum(['active', 'inactive']).optional().catch(undefined),
  sort: z.string().max(50).catch('-createdAt'),
});

export type SampleSearch = z.infer<typeof sampleSearchSchema>;

export const sampleRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/sample',
  validateSearch: (search: Record<string, unknown>): SampleSearch =>
    sampleSearchSchema.parse({ page: 1, sort: '-createdAt', ...search }),
  // Each system's pages load on demand, so the shell stays small as modules are added.
  component: lazyRouteComponent(() => import('@/features/sample/SampleListPage'), 'SampleListPage'),
});

const uiKitRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/ui-kit',
  component: lazyRouteComponent(() => import('@/features/ui-kit/UiKitPage'), 'UiKitPage'),
});

const routeTree = rootRoute.addChildren([homeRoute, sampleRoute, uiKitRoute]);

export const router = createRouter({ routeTree, defaultPreload: 'intent', scrollRestoration: true });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
