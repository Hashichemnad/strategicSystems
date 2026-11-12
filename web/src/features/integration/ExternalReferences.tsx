import { ExternalLink, Lock } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { formatDateTime, pickText } from '@/lib/format';
import { StatusBanner, StatusChip, type Tone } from '@/ui';
import { useRecordReferences, type RecordReference } from './api';
import {
  useRecordSyncState,
  useSourceHistory,
  type RecordSyncState,
  type SourceHistoryEntry,
} from './sync/api';

interface RecordRef {
  /** Record type, e.g. `portfolio.project`, `risk.risk`, `org.employee`. */
  type: string;
  id: string | undefined;
}

const statusTone: Record<string, Tone> = {
  synced: 'success',
  pending: 'info',
  error: 'danger',
  deleted_in_source: 'warning',
};

function sourceName(reference: RecordReference, language: string) {
  return pickText(language, reference.sourceNameAr, reference.sourceNameEn) || reference.sourceSystem;
}

/**
 * Header chip "from {source}" for a record created from another system (SPEC-ADDENDUM §B, ADR 0164). Renders nothing for
 * records without an origin reference or that the user may not read.
 */
export function OriginBadge({ type, id }: RecordRef) {
  const { t, i18n } = useTranslation('integration');
  const references = useRecordReferences(type, id);
  const origin = references.data?.origin;
  const state = useRecordSyncState(type, origin ? id : undefined).data;
  if (!origin) return null;
  const source = syncSourceName(state, i18n.language) || sourceName(origin, i18n.language);
  return (
    <span data-testid="origin-badge" className="inline-flex flex-wrap items-center gap-1">
      {state?.readOnly ? (
        <StatusChip tone="warning" className="gap-1">
          <Lock className="size-3.5" aria-hidden />
          <span data-testid="sync-read-only">{t('sync.badges.readOnly', { source })}</span>
        </StatusChip>
      ) : (
        <StatusChip tone="info" className="gap-1">
          <ExternalLink className="size-3.5" aria-hidden />
          {t('origin', { source: sourceName(origin, i18n.language) })}
        </StatusChip>
      )}
      {state?.deletedInSource && (
        <StatusChip tone="danger">{t('sync.badges.deletedInSource', { source })}</StatusChip>
      )}
      {state?.resubmitRequired && <StatusChip tone="warning">{t('sync.badges.resubmit')}</StatusChip>}
    </span>
  );
}

function syncSourceName(state: RecordSyncState | undefined, language: string) {
  return state
    ? pickText(language, state.sourceNameAr, state.sourceNameEn) || (state.sourceSystem ?? '')
    : '';
}

/**
 * Details section "External references": every source system's ID of the record, with sync status and times. Hidden when
 * the record has none.
 */
export function ExternalReferencesSection({ type, id }: RecordRef) {
  const { t, i18n } = useTranslation('integration');
  const references = useRecordReferences(type, id);
  const items = references.data?.references ?? [];
  const state = useRecordSyncState(type, references.data?.origin ? id : undefined).data;
  if (items.length === 0) return null;
  const source = syncSourceName(state, i18n.language);
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-4"
      aria-labelledby="external-references-title"
      data-testid="external-references"
    >
      <h2 id="external-references-title" className="text-base font-semibold">
        {t('section.title')}
      </h2>
      {state?.readOnly && (
        <div data-testid="sync-read-only-banner">
          <StatusBanner
            tone="warning"
            title={t('sync.badges.readOnly', { source })}
            description={t('sync.badges.readOnlyHint', { source })}
          />
        </div>
      )}
      {state?.resubmitRequired && (
        <StatusBanner
          tone="warning"
          title={t('sync.badges.resubmit')}
          description={t('sync.badges.resubmitHint')}
        />
      )}
      <dl className="grid gap-3 sm:grid-cols-2">
        {items.map((reference) => (
          <div
            key={`${reference.sourceSystem}:${reference.externalId}`}
            className="flex flex-col gap-1 rounded-md border p-3"
          >
            <dt className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
              <span className="font-medium text-foreground">{sourceName(reference, i18n.language)}</span>
              {reference.isOrigin && <StatusChip tone="info">{t('section.origin')}</StatusChip>}
              <StatusChip tone={statusTone[reference.syncStatus] ?? 'neutral'}>
                {t(`status.${reference.syncStatus}`, { defaultValue: reference.syncStatus })}
              </StatusChip>
            </dt>
            <dd className="flex flex-col gap-1 text-sm">
              <span dir="ltr" className="text-start font-mono break-all" data-testid="external-id">
                {reference.externalId}
              </span>
              <span className="text-xs text-muted-foreground">
                {t('section.firstSeen', { date: formatDateTime(reference.firstSeenAt, i18n.language) })}
                {reference.lastSyncedAt &&
                  ` · ${t('section.lastSynced', { date: formatDateTime(reference.lastSyncedAt, i18n.language) })}`}
              </span>
            </dd>
          </div>
        ))}
      </dl>
      {state && state.sourceHistoryCount > 0 && <SourceHistoryPanel type={type} id={id} source={source} />}
    </section>
  );
}

/** Read-only history from the source (ADR 0208): approval steps and field changes; never editable, never re-entered here. */
export function SourceHistoryPanel({ type, id, source }: RecordRef & { source: string }) {
  const { t, i18n } = useTranslation('integration');
  const history = useSourceHistory(type, id, true);
  const entries = history.data ?? [];
  const approvals = entries.filter((e) => e.kind === 'approval');
  const fields = entries.filter((e) => e.kind === 'field');
  const when = (e: SourceHistoryEntry) => formatDateTime(e.occurredAt, i18n.language);
  if (entries.length === 0) return null;
  return (
    <section
      className="flex flex-col gap-3 border-t pt-3"
      aria-labelledby="source-history-title"
      data-testid="source-history"
    >
      <div className="flex flex-col gap-1">
        <h3 id="source-history-title" className="font-medium">
          {t('sync.history.title', { source })}
        </h3>
        <p className="text-xs text-muted-foreground">{t('sync.history.hint')}</p>
      </div>
      {approvals.length > 0 && (
        <div className="flex flex-col gap-2">
          <h4 className="text-sm font-medium">{t('sync.history.approvals')}</h4>
          <ol className="flex flex-col gap-2 border-s ps-4" data-testid="source-approvals">
            {approvals.map((e) => (
              <li key={e.id} className="flex flex-col gap-0.5 text-sm">
                <span className="flex flex-wrap items-center gap-2">
                  {e.processName && <span className="font-medium">{e.processName}</span>}
                  {e.stepStatus && (
                    <StatusChip tone={stepTone[e.stepStatus] ?? 'neutral'}>
                      {t(`sync.history.steps.${e.stepStatus}` as 'sync.history.steps.Approved', {
                        defaultValue: e.stepStatus,
                      })}
                    </StatusChip>
                  )}
                  {e.processStatus && (
                    <span className="text-xs text-muted-foreground">
                      {t('sync.history.processStatus', {
                        status: t(`sync.history.steps.${e.processStatus}` as 'sync.history.steps.Approved', {
                          defaultValue: e.processStatus,
                        }),
                      })}
                    </span>
                  )}
                </span>
                <span className="text-xs text-muted-foreground">
                  {[e.actorName, when(e)].filter(Boolean).join(' · ')}
                </span>
                {e.comments && (
                  <span dir="auto" className="text-sm [overflow-wrap:anywhere]">
                    {e.comments}
                  </span>
                )}
              </li>
            ))}
          </ol>
        </div>
      )}
      {fields.length > 0 && (
        <div className="flex flex-col gap-2">
          <h4 className="text-sm font-medium">{t('sync.history.fields')}</h4>
          <ul className="flex flex-col divide-y text-sm" data-testid="source-field-changes">
            {fields.map((e) => (
              <li key={e.id} className="flex flex-col gap-0.5 py-1.5">
                <span className="flex flex-wrap items-center gap-2">
                  <span dir="ltr" className="font-mono text-xs">
                    {e.field}
                  </span>
                  <span dir="auto" className="[overflow-wrap:anywhere]">
                    {e.oldValue ?? t('sync.history.empty')} → {e.newValue ?? t('sync.history.empty')}
                  </span>
                </span>
                <span className="text-xs text-muted-foreground">
                  {[e.actorName, when(e)].filter(Boolean).join(' · ')}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

const stepTone: Record<string, Tone> = {
  Approved: 'success',
  Rejected: 'danger',
  Pending: 'warning',
  Started: 'info',
  Removed: 'neutral',
  Reassigned: 'neutral',
  NoResponse: 'neutral',
};
