import { DirectionProvider } from '@radix-ui/react-direction';
import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { UNAUTHORIZED_EVENT, resetAntiforgeryToken } from '@/api/client';
import { SettingsEffects } from '@/features/settings/publicSettings';
import { ToastProvider } from '@/ui';
import { createQueryClient } from './queryClient';
import { router } from './router';
import { ThemeProvider } from './theme';

/**
 * Providers. Switching language re-renders with the new direction; server data stays in the query cache,
 * so the switch is instant and nothing is refetched (CLAUDE.md §10).
 */
export function App() {
  const { i18n } = useTranslation();
  const [queryClient] = useState(createQueryClient);

  // A call answered 401 (session expired or revoked elsewhere): back to the login page, then to where the user was.
  useEffect(() => {
    const onUnauthorized = () => {
      resetAntiforgeryToken();
      queryClient.clear();
      const here = router.state.location.href;
      if (!router.state.location.pathname.startsWith('/login')) {
        void router.navigate({ to: '/login', search: { redirect: here } });
      }
    };
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
  }, [queryClient]);

  return (
    <DirectionProvider dir={i18n.dir()}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <SettingsEffects />
          <ToastProvider>
            <RouterProvider router={router} context={{ queryClient }} />
          </ToastProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </DirectionProvider>
  );
}
