import { useTranslation } from 'react-i18next';

export interface DiffRow {
  key?: string | null;
  field: string;
  vendor: string | null | undefined;
  pm: string | null | undefined;
}

/** "Vendor value → PM value" per changed field (edit-then-accept and accepted-plan diffs, visible to both sides). */
export function DiffList({ entries }: { entries: DiffRow[] }) {
  const { t } = useTranslation('vendors');
  if (entries.length === 0) return null;
  return (
    <ul className="mt-1 flex flex-col gap-1 text-sm" data-testid="wasel-diff">
      {entries.map((d, i) => (
        <li key={`${d.key ?? ''}-${d.field}-${i}`} className="flex flex-wrap items-center gap-2">
          {d.key && <span className="text-xs text-muted-foreground">{d.key}</span>}
          <span className="font-medium">{t(`diffFields.${d.field}`, d.field)}</span>
          {d.field === 'added' ? (
            <ins className="text-success" dir="auto">
              {d.pm}
            </ins>
          ) : d.field === 'removed' ? (
            <del className="text-danger" dir="auto">
              {d.vendor}
            </del>
          ) : (
            <>
              <del className="text-muted-foreground" dir="auto" aria-label={t('diff.vendorValue')}>
                {d.vendor ?? '—'}
              </del>
              <span aria-hidden>→</span>
              <ins className="font-medium no-underline" dir="auto" aria-label={t('diff.pmValue')}>
                {d.pm ?? '—'}
              </ins>
            </>
          )}
        </li>
      ))}
    </ul>
  );
}
