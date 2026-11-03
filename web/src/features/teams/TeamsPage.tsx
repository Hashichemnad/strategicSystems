import { getRouteApi, Link } from '@tanstack/react-router';
import { LayoutDashboard, Plus, Search, UsersRound } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsList,
  TabsTrigger,
} from '@/ui';
import { statusTone, teamStatuses, useTeamCounts, useTeamOptions, useTeams, type TeamListItem } from './api';

const route = getRouteApi('/app/teams');
const PAGE_SIZE = 24;

/** Teams register (SPEC §4.6): cards by type, status, unit and leader; tabs My teams / I lead / Pending evaluation. State in the URL. */
export function TeamsPage() {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const canRequest = useCan('teams.team.request');
  const canDashboard = useCan('teams.dashboard.read');
  const counts = useTeamCounts();
  const options = useTeamOptions();
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    if (text === (search.search ?? '')) return;
    const timer = window.setTimeout(
      () => void navigate({ search: (p) => ({ ...p, search: text || undefined, page: 1 }) }),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [text, search.search, navigate]);
  const teams = useTeams({
    view: search.view,
    page: search.page,
    pageSize: PAGE_SIZE,
    ...(search.type ? { typeCode: search.type } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.search ? { search: search.search } : {}),
  });
  const count = (view: string) =>
    view === 'mine'
      ? counts.data?.mine
      : view === 'leading'
        ? counts.data?.leading
        : view === 'pending-evaluation'
          ? counts.data?.pendingEvaluation
          : undefined;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        actions={
          <>
            {canDashboard && (
              <Button variant="outline" asChild>
                <Link to="/teams/dashboard">
                  <LayoutDashboard aria-hidden />
                  {t('dashboard')}
                </Link>
              </Button>
            )}
            {canRequest && (
              <Button asChild>
                <Link to="/teams/new" search={{}}>
                  <Plus aria-hidden />
                  {t('newTeam')}
                </Link>
              </Button>
            )}
          </>
        }
      />
      <Tabs
        value={search.view}
        onValueChange={(view) => void navigate({ search: (p) => ({ ...p, view: view as 'all', page: 1 }) })}
      >
        <TabsList>
          {(['all', 'mine', 'leading', 'pending-evaluation'] as const).map((view) => (
            <TabsTrigger key={view} value={view}>
              {t(`views.${view}`)}
              {count(view) ? (
                <span className="ms-1 rounded-full bg-muted px-2 text-xs tabular-nums">{count(view)}</span>
              ) : null}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
      <div className="grid gap-3 md:grid-cols-[1fr_14rem_14rem]">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('filters.search')}
            aria-label={t('filters.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
        </div>
        <Select
          aria-label={t('filters.type')}
          value={search.type ?? 'all'}
          onValueChange={(v) =>
            void navigate({ search: (p) => ({ ...p, type: v === 'all' ? undefined : v, page: 1 }) })
          }
          options={[
            { value: 'all', label: t('filters.allTypes') },
            ...(options.data?.types ?? []).map((type) => ({
              value: type.code,
              label: pickText(i18n.language, type.nameAr, type.nameEn),
            })),
          ]}
        />
        <Select
          aria-label={t('filters.status')}
          value={search.status ?? 'all'}
          onValueChange={(v) =>
            void navigate({ search: (p) => ({ ...p, status: v === 'all' ? undefined : v, page: 1 }) })
          }
          options={[
            { value: 'all', label: t('filters.allStatuses') },
            ...teamStatuses.map((s) => ({ value: s, label: t(`status.${s}`) })),
          ]}
        />
      </div>
      {teams.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, teams.error)} />
      ) : !teams.data ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-40 rounded-lg" />
          ))}
        </div>
      ) : teams.data.items.length === 0 ? (
        <EmptyState icon={UsersRound} title={t('empty')} />
      ) : (
        <>
          <ul className="grid gap-4 md:grid-cols-2 xl:grid-cols-3" data-testid="teams-register">
            {teams.data.items.map((team) => (
              <li key={team.id}>
                <TeamCard team={team} />
              </li>
            ))}
          </ul>
          <Pagination
            page={teams.data.page}
            totalPages={teams.data.totalPages ?? 1}
            totalCount={teams.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </div>
  );
}

function TeamCard({ team }: { team: TeamListItem }) {
  const { t, i18n } = useTranslation('teams');
  return (
    <Link
      to="/teams/$teamId"
      params={{ teamId: team.id }}
      search={{ tab: 'overview' }}
      className="flex h-full flex-col gap-3 rounded-lg border bg-card p-4 shadow-sm transition hover:border-primary focus-visible:outline-2 focus-visible:outline-primary"
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-xs text-muted-foreground">
            {team.number} · {pickText(i18n.language, team.typeNameAr, team.typeNameEn)}
          </p>
          <h2 className="truncate font-semibold">{pickText(i18n.language, team.nameAr, team.nameEn)}</h2>
        </div>
        <StatusChip tone={statusTone[team.status] ?? 'neutral'}>
          {t(`status.${team.status as 'active'}`)}
        </StatusChip>
      </div>
      <dl className="grid grid-cols-2 gap-2 text-sm">
        <div>
          <dt className="text-muted-foreground">{t('card.leader')}</dt>
          <dd className="truncate">
            {team.leader ? pickText(i18n.language, team.leader.nameAr, team.leader.nameEn) : '—'}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.owningUnit')}</dt>
          <dd className="truncate">
            {team.owningUnit ? pickText(i18n.language, team.owningUnit.nameAr, team.owningUnit.nameEn) : '—'}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.startDate')}</dt>
          <dd>{team.startDate ? formatDate(team.startDate, i18n.language) : '—'}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.endDate')}</dt>
          <dd>{team.endDate ? formatDate(team.endDate, i18n.language) : t('permanence.permanent')}</dd>
        </div>
      </dl>
      <div className="mt-auto flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span>{t('card.members', { count: team.activeMembers })}</span>
        {team.myMembership && <StatusChip tone="info">{t('card.member')}</StatusChip>}
        {team.lastScore !== null && (
          <span className="ms-auto font-medium text-foreground tabular-nums">
            {t('card.lastScore')}: {formatNumber(team.lastScore, i18n.language)}
          </span>
        )}
      </div>
    </Link>
  );
}
