import { Link } from '@tanstack/react-router';
import { UsersRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  EmptyState,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { statusTone, useMemberships, useTeamOptions } from './api';

/** Employee profile → Teams (SPEC §4.6): memberships and roles; evaluation results only where the viewer may see them (ADR 0042). */
export function EmployeeTeamsTab({ employeeId }: { employeeId: string }) {
  const { t, i18n } = useTranslation('teams');
  const memberships = useMemberships(employeeId);
  const options = useTeamOptions();
  if (memberships.isPending) return <Skeleton className="h-32" />;
  if (memberships.isError || memberships.data.length === 0)
    return <EmptyState icon={UsersRound} title={t('employeeTab.empty')} />;
  const grade = (code: string | null) => {
    const band = options.data?.grades.find((g) => g.code === code);
    return band ? pickText(i18n.language, band.nameAr, band.nameEn) : (code ?? '—');
  };
  return (
    <Table data-testid="employee-teams">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('title')}</TableHeaderCell>
          <TableHeaderCell>{t('form.role')}</TableHeaderCell>
          <TableHeaderCell>{t('members.from')}</TableHeaderCell>
          <TableHeaderCell>{t('members.to')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
          <TableHeaderCell>{t('employeeTab.results')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {memberships.data.map((m) => (
          <TableRow key={m.memberId}>
            <TableCell>
              <Link
                to="/teams/$teamId"
                params={{ teamId: m.teamId }}
                search={{ tab: 'overview' }}
                className="text-primary hover:underline"
              >
                {m.teamNumber} · {pickText(i18n.language, m.teamNameAr, m.teamNameEn)}
              </Link>
              <StatusChip tone={statusTone[m.teamStatus] ?? 'neutral'} className="ms-2">
                {t(`status.${m.teamStatus as 'active'}`)}
              </StatusChip>
            </TableCell>
            <TableCell>{pickText(i18n.language, m.roleNameAr, m.roleNameEn)}</TableCell>
            <TableCell>{m.fromDate ? formatDate(m.fromDate, i18n.language) : '—'}</TableCell>
            <TableCell>{m.toDate ? formatDate(m.toDate, i18n.language) : '—'}</TableCell>
            <TableCell>{t(`members.statuses.${m.status as 'active'}`)}</TableCell>
            <TableCell>
              {m.results === null ? (
                <span className="text-xs text-muted-foreground">—</span>
              ) : (
                <ul className="flex flex-col gap-1 text-sm">
                  {m.results.map((r) => (
                    <li key={r.evaluationId}>
                      {r.cycle === 'completion' ? t('evaluations.completion') : r.cycle}: {grade(r.grade)}
                      {r.points !== null && ` (${formatNumber(r.points, i18n.language)})`}
                      {r.approvedReward !== null && ` · ${formatNumber(r.approvedReward, i18n.language)}`}
                    </li>
                  ))}
                </ul>
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
