import { Link } from '@tanstack/react-router';
import { Plus, UsersRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import { Button, EmptyState, Skeleton, StatusChip } from '@/ui';
import { statusTone, useLinkedTeams } from './api';

/**
 * The "Teams" tab of any team-able record (SPEC §4.4 F): the teams linked to it with their members and roles, and "Request a team"
 * with the record linked. Proven on the Sample item; later systems add the same panel.
 */
export function LinkedTeamsPanel({ type, id }: { type: string; id: string }) {
  const { t, i18n } = useTranslation('teams');
  const linked = useLinkedTeams(type, id);
  return (
    <section className="flex flex-col gap-4" data-testid="linked-teams">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('linked.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('linked.description')}</p>
        </div>
        {linked.data?.canLink && (
          <Button asChild>
            <Link to="/teams/new" search={{ linkedType: type, linkedId: id }}>
              <Plus aria-hidden />
              {t('linked.request')}
            </Link>
          </Button>
        )}
      </header>
      {!linked.data ? (
        <Skeleton className="h-24" />
      ) : linked.data.teams.length === 0 ? (
        <EmptyState icon={UsersRound} title={t('linked.empty')} />
      ) : (
        <ul className="grid gap-3 md:grid-cols-2">
          {linked.data.teams.map(({ team, members }) => (
            <li key={team.id} className="rounded-lg border bg-card p-4">
              <div className="flex items-start justify-between gap-2">
                <Link
                  to="/teams/$teamId"
                  params={{ teamId: team.id }}
                  search={{ tab: 'overview' }}
                  className="font-semibold text-primary hover:underline"
                >
                  {team.number} · {pickText(i18n.language, team.nameAr, team.nameEn)}
                </Link>
                <StatusChip tone={statusTone[team.status] ?? 'neutral'}>
                  {t(`status.${team.status as 'active'}`)}
                </StatusChip>
              </div>
              <ul className="mt-2 flex flex-col gap-1 text-sm">
                {members.map((m) => (
                  <li key={m.id} className="flex justify-between gap-2">
                    <span>
                      {m.employee
                        ? pickText(i18n.language, m.employee.nameAr, m.employee.nameEn)
                        : m.externalName}
                    </span>
                    <span className="text-muted-foreground">
                      {pickText(i18n.language, m.roleNameAr, m.roleNameEn)}
                    </span>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
      {(linked.data?.restricted ?? 0) > 0 && (
        <p className="text-sm text-muted-foreground">
          {t('linked.restricted', { count: linked.data?.restricted })}
        </p>
      )}
    </section>
  );
}
