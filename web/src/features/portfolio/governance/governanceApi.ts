import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

type S = components['schemas'];
export type Capacity = S['CapacityDto'];
export type CapacityRow = S['CapacityRowDto'];
export type CapacityAllocation = S['CapacityAllocationDto'];
export type PortfolioReview = S['PortfolioReviewDto'];
export type PortfolioReviewProject = S['PortfolioReviewProjectDto'];
export type PortfolioReviewListItem = S['PortfolioReviewListItemDto'];
export type PortfolioReviewInput = S['PortfolioReviewInput'];

export const reviewDecisions = ['continue', 're-scope', 'hold', 'cancel'] as const;
export type ReviewDecision = (typeof reviewDecisions)[number];

export interface CapacityQuery {
  unitId?: string | undefined;
  below: boolean;
  overOnly: boolean;
}

const keys = {
  capacity: (q: CapacityQuery) => ['portfolio', 'capacity', q] as const,
  reviews: ['portfolio', 'reviews'] as const,
  review: (id: string) => ['portfolio', 'reviews', id] as const,
};

/** Employee allocation across teams and projects (docs/design/portfolio.md §58). */
export function useCapacity(query: CapacityQuery) {
  return useQuery({
    queryKey: keys.capacity(query),
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/capacity', {
          params: {
            query: {
              ...(query.unitId ? { unitId: query.unitId } : {}),
              below: query.below,
              overOnly: query.overOnly,
            },
          },
          signal,
        }),
      ),
  });
}

export function useReviews() {
  return useQuery({
    queryKey: keys.reviews,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/portfolio/reviews', { signal })),
  });
}

export function useReview(id: string) {
  return useQuery({
    queryKey: keys.review(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/reviews/{rid}', { params: { path: { rid: id } }, signal })),
  });
}

export function useCreateReview() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: PortfolioReviewInput) =>
      unwrap(await api.POST('/api/v1/portfolio/reviews', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.reviews }),
  });
}

export function useSetDecision(reviewId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { projectId: string; decision: ReviewDecision; comment: string | null }) =>
      unwrap(
        await api.PUT('/api/v1/portfolio/reviews/{rid}/decisions/{projectId}', {
          params: { path: { rid: reviewId, projectId: input.projectId } },
          body: { decision: input.decision, comment: input.comment },
        }),
      ),
    onSuccess: (data) => queryClient.setQueryData(keys.review(reviewId), data),
  });
}

export function useCompleteReview(reviewId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/portfolio/reviews/{rid}/complete', { params: { path: { rid: reviewId } } }),
      ),
    onSuccess: (data) => {
      queryClient.setQueryData(keys.review(reviewId), data);
      void queryClient.invalidateQueries({ queryKey: keys.reviews });
    },
  });
}

/** `YYYY-Qn` of a date (local calendar). */
export function quarterOf(date: Date): string {
  return `${date.getFullYear()}-Q${Math.floor(date.getMonth() / 3) + 1}`;
}

/** The quarters offered by the new-review dialog: the last four, this one and the next one. */
export function quarterOptions(today: Date): string[] {
  const result: string[] = [];
  for (let offset = -4; offset <= 1; offset++) {
    const d = new Date(today.getFullYear(), today.getMonth() + offset * 3, 1);
    result.push(quarterOf(d));
  }
  return result.reverse();
}
