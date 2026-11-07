import { ExternalLink } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { formatDateTime, pickText } from '@/lib/format';
import { StatusChip, type Tone } from '@/ui';
import { useRecordReferences, type RecordReference } from './api';

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
  if (!origin) return null;
  return (
    <span data-testid="origin-badge">
      <StatusChip tone="info" className="gap-1">
        <ExternalLink className="size-3.5" aria-hidden />
        {t('origin', { source: sourceName(origin, i18n.language) })}
      </StatusChip>
    </span>
  );
}

/**
 * Details section "External references": every source system's ID of the record, with sync status and times. Hidden when
 * the record has none.
 */
export function ExternalReferencesSection({ type, id }: RecordRef) {
  const { t, i18n } = useTranslation('integration');
  const references = useRecordReferences(type, id);
  const items = references.data?.references ?? [];
  if (items.length === 0) return null;
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-4"
      aria-labelledby="external-references-title"
      data-testid="external-references"
    >
      <h2 id="external-references-title" className="text-base font-semibold">
        {t('section.title')}
      </h2>
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
    </section>
  );
}
