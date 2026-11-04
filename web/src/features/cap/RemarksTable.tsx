import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Table, TableBody, TableCell, TableHead, TableHeaderCell, TableRow } from '@/ui';
import type { CapRemarkListItem } from './api';
import { PriorityChip, RemarkStatusChip, lookupTypes, useCapLabels, useDate, useNames } from './components';

/** Remarks list (source page and the remarks register). */
export function RemarksTable({
  rows,
  showSource = true,
}: {
  rows: readonly CapRemarkListItem[];
  showSource?: boolean;
}) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const labels = useCapLabels();
  const date = useDate();
  return (
    <Table data-testid="cap-remarks-table">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.remark')}</TableHeaderCell>
          {showSource && <TableHeaderCell>{t('fields.source')}</TableHeaderCell>}
          <TableHeaderCell>{t('fields.priority')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.department')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.owner')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.dueOn')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.actions')}</TableHeaderCell>
          <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((r) => (
          <TableRow key={r.id} data-testid="cap-remark-row">
            <TableCell>
              <Link
                to="/cap/remarks/$remarkId"
                params={{ remarkId: r.id }}
                className="whitespace-nowrap font-medium text-primary hover:underline"
              >
                <bdi>{r.number}</bdi>
              </Link>
            </TableCell>
            <TableCell className="max-w-md">
              <span className="flex flex-col">
                <span className="line-clamp-2">{names.text(r.textAr, r.textEn)}</span>
                <span className="text-xs text-muted-foreground">
                  {labels.lookup(lookupTypes.remarkType, r.typeCode)}
                  {r.criterionCode && ` · ${labels.lookup(lookupTypes.criterion, r.criterionCode)}`}
                </span>
              </span>
            </TableCell>
            {showSource && (
              <TableCell>
                <Link
                  to="/cap/sources/$sourceId"
                  params={{ sourceId: r.sourceId }}
                  className="text-primary hover:underline"
                >
                  <bdi>{r.sourceNumber}</bdi>
                </Link>
                <span className="block text-xs text-muted-foreground">
                  {labels.lookup(lookupTypes.sourceType, r.sourceTypeCode)}
                </span>
              </TableCell>
            )}
            <TableCell>
              <PriorityChip priority={r.priority} />
            </TableCell>
            <TableCell>
              {names.unit(r.department)}
              {r.section && (
                <span className="block text-xs text-muted-foreground">{names.unit(r.section)}</span>
              )}
            </TableCell>
            <TableCell>{names.person(r.owner)}</TableCell>
            <TableCell className={r.overdue ? 'font-medium text-danger' : undefined}>
              {date(r.dueOn)}
            </TableCell>
            <TableCell>
              <bdi className="tabular-nums">
                {r.completedActions} / {r.actions}
              </bdi>
            </TableCell>
            <TableCell>
              <RemarkStatusChip status={r.status} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
