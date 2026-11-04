import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type PortfolioFeatures = components['schemas']['PortfolioFeaturesDto'];

/** Feature code of each standards enhancement (ADR 0092); the server also refuses switched-off APIs. */
export type FeatureCode =
  | 'portfolios'
  | 'prioritisation'
  | 'health'
  | 'baselines'
  | 'earnedValue'
  | 'raid'
  | 'riskMatrix'
  | 'raci'
  | 'communicationPlan'
  | 'benefits'
  | 'quality'
  | 'procurement'
  | 'statusReport'
  | 'gateChecklists'
  | 'templates';

export const featureKeys = { all: ['portfolio', 'features'] as const };

export function usePortfolioFeatures() {
  return useQuery({
    queryKey: featureKeys.all,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/portfolio/features', { signal })),
    staleTime: 60_000,
  });
}

/** Whether a feature is on (false while loading, so switched-off UI never flashes). */
export function useFeature(code: FeatureCode): boolean {
  const { data } = usePortfolioFeatures();
  return data?.features[code] === true;
}
