import { DirectionProvider } from '@radix-ui/react-direction';
import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
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

  return (
    <DirectionProvider dir={i18n.dir()}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <ToastProvider>
            <RouterProvider router={router} />
          </ToastProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </DirectionProvider>
  );
}
