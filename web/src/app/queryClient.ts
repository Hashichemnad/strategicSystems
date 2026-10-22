import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '@/api/client';

/** Client errors (4xx) are final; network and server errors retry twice. */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) =>
          !(error instanceof ApiError && error.status < 500) && failureCount < 2,
      },
    },
  });
}
