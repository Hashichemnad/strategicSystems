import { Link } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { cn } from '@/lib/cn';
import {
  Button,
  EmptyState,
  FormField,
  PageHeader,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { capApi, useCapSuggestions, type CapSuggestion } from './api';
import {
  CapPageLayout,
  PriorityChip,
  SimpleDialog,
  useCapT,
  useDate,
  useDialogMutation,
  useNames,
} from './components';
import { ReasonDialog } from './forms';

const statuses = ['open', 'raised', 'dismissed'] as const;

/** Suggested remarks from other systems (KPI target missed, risk event, ineffective control) — raised in one click or dismissed. */
export function CapSuggestionsPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const names = useNames();
  const date = useDate();
  const [status, setStatus] = useState<string>('open');
  const suggestions = useCapSuggestions(status);
  const [raising, setRaising] = useState<CapSuggestion>();
  const [dismissing, setDismissing] = useState<CapSuggestion>();
  const dismiss = useDialogMutation(capApi.dismiss, () => setDismissing(undefined));

  return (
    <CapPageLayout testId="cap-suggestions">
      <PageHeader title={t('suggestions.title')} description={t('suggestions.description')} />
      <div role="tablist" aria-label={t('fields.status')} className="flex flex-wrap gap-1">
        {statuses.map((s) => (
          <button
            key={s}
            type="button"
            role="tab"
            aria-selected={status === s}
            onClick={() => setStatus(s)}
            className={cn(
              'rounded-full border px-3 py-1 text-sm',
              status === s ? 'border-primary bg-primary text-primary-foreground' : 'hover:bg-muted',
            )}
          >
            {k(`suggestionStatus.${s}`)}
          </button>
        ))}
      </div>
      {suggestions.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, suggestions.error)} />
      ) : !suggestions.data ? (
        <Skeleton className="h-48 rounded-lg" />
      ) : suggestions.data.items.length === 0 ? (
        <EmptyState icon={Inbox} title={t('suggestions.empty')} />
      ) : (
        <Table data-testid="cap-suggestions-table">
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('suggestions.kind')}</TableHeaderCell>
              <TableHeaderCell>{t('suggestions.record')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.priority')}</TableHeaderCell>
              <TableHeaderCell>{t('suggestions.createdAt')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('common.actions')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {suggestions.data.items.map((s) => (
              <TableRow key={s.id} data-testid="cap-suggestion-row">
                <TableCell>{k(`suggestionKind.${s.kind}`)}</TableCell>
                <TableCell>
                  <span className="flex flex-col">
                    <span>
                      <bdi className="font-medium">{s.parentNumber}</bdi> {names.text(s.titleAr, s.titleEn)}
                    </span>
                    {s.dismissReason && (
                      <span className="text-xs text-muted-foreground">{s.dismissReason}</span>
                    )}
                  </span>
                </TableCell>
                <TableCell>{names.unit(s.unit)}</TableCell>
                <TableCell>
                  <PriorityChip priority={s.priority} />
                </TableCell>
                <TableCell>{date(s.createdAt)}</TableCell>
                <TableCell>
                  <span className="flex justify-end gap-1">
                    {s.status === 'open' && (
                      <>
                        <Button size="sm" onClick={() => setRaising(s)} data-testid="cap-suggestion-raise">
                          {t('suggestions.raise')}
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setDismissing(s)}>
                          {t('suggestions.dismiss')}
                        </Button>
                      </>
                    )}
                    {s.remarkId && (
                      <Button size="sm" variant="outline" asChild>
                        <Link to="/cap/remarks/$remarkId" params={{ remarkId: s.remarkId }}>
                          {t('suggestions.openRemark')}
                        </Link>
                      </Button>
                    )}
                  </span>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {raising && <RaiseDialog suggestion={raising} onClose={() => setRaising(undefined)} />}
      {dismissing && (
        <ReasonDialog
          title={t('suggestions.dismiss')}
          label={t('suggestions.dismissReason')}
          submitLabel={t('suggestions.dismiss')}
          testId="cap-suggestion-dismiss"
          busy={dismiss.busy}
          onClose={() => setDismissing(undefined)}
          onSubmit={(reason) => dismiss.run({ id: dismissing.id, reason })}
        />
      )}
    </CapPageLayout>
  );
}

function RaiseDialog({ suggestion, onClose }: { suggestion: CapSuggestion; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const [unitId, setUnitId] = useState<string | undefined>(suggestion.unit?.id);
  const m = useDialogMutation(capApi.raise, onClose);
  return (
    <SimpleDialog
      title={t('suggestions.raise')}
      description={t('suggestions.raiseHint')}
      onClose={onClose}
      busy={m.busy}
      disabled={!unitId}
      submitLabel={t('suggestions.raise')}
      testId="cap-suggestion-raise-confirm"
      onSubmit={() => m.run({ id: suggestion.id, unitId: unitId ?? null })}
    >
      <FormField label={t('fields.department')} required>
        <UnitSelect value={unitId} onChange={setUnitId} />
      </FormField>
    </SimpleDialog>
  );
}
