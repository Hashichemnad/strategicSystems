import { getRouteApi, Navigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { Skeleton, StatusBanner } from '@/ui';
import { useRecommendation } from './api';

const route = getRouteApi('/app/teams/recommendations/$recommendationId');

/** Deep link of a recommendation (links registry badge of tasks created from it): opens its team's register. */
export function RecommendationRedirect() {
  const { t: tc } = useTranslation();
  const { recommendationId } = route.useParams();
  const item = useRecommendation(recommendationId);
  if (item.isPending) return <Skeleton className="h-32" />;
  if (item.isError) return <StatusBanner tone="danger" title={errorMessage(tc, item.error)} />;
  return (
    <Navigate
      to="/teams/$teamId"
      params={{ teamId: item.data.teamId }}
      search={{ tab: 'recommendations' }}
      replace
    />
  );
}
