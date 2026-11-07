import { getRouteApi } from '@tanstack/react-router';
import { Lock } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { responsibilitySystems } from '@/features/settings/settingsNav';
import { newIdempotencyKey } from '@/features/workflow/api';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  ConfirmDialog,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  type Tone,
} from '@/ui';
import { UnitSelect } from './pickers';
import {
  type ResponsibilityKind,
  type ResponsibilityOutcome,
  type ResponsibilityResult,
  type ResponsibilityRow,
  useResponsibilities,
  useResponsibilityCandidates,
  useResponsibilityKinds,
  usePreviewResponsibilities,
  useReassignResponsibilities,
} from './responsibilities';
import { useAccess } from './session';

const route = getRouteApi('/app/settings/responsibilities');
const PAGE_SIZE = 25;

const outcomeTone: Record<string, Tone> = {
  allowed: 'success',
  moved: 'success',
  'change-request': 'info',
  blocked: 'danger',
  skipped: 'warning',
};

/**
 * System → Settings → Responsibilities (SPEC-ADDENDUM §D2, ADR 0168): choose the system and the kind, filter the records, select them, pick the
 * new person (or unit), give a reason, preview (allowed / change request / blocked per record), confirm, see what moved and what was skipped.
 * The server checks `<system>.responsibilities.manage` of that system and moves the records with the module's own rules.
 */
export function ResponsibilitiesPage() {
  const search = route.useSearch();
  const navigate = route.useNavigate();
  return (
    <ResponsibilitiesView
      system={search.system}
      kind={search.kind}
      onChange={(system, kind) => void navigate({ search: { system, kind } })}
    />
  );
}

/** The page body, driven by the chosen system / kind (router-free, so it can be tested on its own). */
export function ResponsibilitiesView({
  system: requestedSystem,
  kind: requestedKind,
  onChange,
}: {
  system: string | undefined;
  kind: string | undefined;
  onChange: (system: string, kind?: string) => void;
}) {
  const { t, i18n } = useTranslation('responsibilities');
  const { t: tc } = useTranslation();
  const access = useAccess();
  const managed = useMemo(
    () => responsibilitySystems.filter((s) => access?.permissions.includes(`${s}.responsibilities.manage`)),
    [access],
  );
  const system = (requestedSystem && managed.find((s) => s === requestedSystem)) || managed[0];
  const kinds = useResponsibilityKinds(system);
  const kind = kinds.data?.kinds.find((k) => k.code === requestedKind) ?? kinds.data?.kinds[0];

  if (!access) return <Skeleton className="h-96" />;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('title')} description={t('description')} />
      {managed.length === 0 ? (
        <EmptyState title={t('noSystems')} />
      ) : (
        <>
          <div className="grid max-w-3xl gap-4 sm:grid-cols-2">
            <FormField label={t('system')}>
              <Select
                value={system}
                onValueChange={(value) => onChange(value)}
                options={managed.map((value) => ({ value, label: t(`systems.${value}`) }))}
              />
            </FormField>
            <FormField label={t('kind')}>
              <Select
                value={kind?.code}
                disabled={!kinds.data}
                onValueChange={(value) => system && onChange(system, value)}
                options={(kinds.data?.kinds ?? []).map((k) => ({
                  value: k.code,
                  label: pickText(i18n.language, k.labelAr, k.labelEn),
                }))}
              />
            </FormField>
          </div>
          {kinds.isError && <StatusBanner tone="danger" title={errorMessage(tc, kinds.error)} />}
          {kinds.isPending && <Skeleton className="h-64" />}
          {system && kind && <KindPanel key={`${system}/${kind.code}`} system={system} kind={kind} />}
        </>
      )}
    </div>
  );
}

function KindPanel({ system, kind }: { system: string; kind: ResponsibilityKind }) {
  const { t, i18n } = useTranslation('responsibilities');
  const { t: tc } = useTranslation();
  const unitKind = kind.target === 'unit';
  const [personId, setPersonId] = useState<string>();
  const [unitId, setUnitId] = useState<string>();
  const [status, setStatus] = useState<string>();
  const [text, setText] = useState('');
  const [debounced, setDebounced] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Map<string, ResponsibilityRow>>(new Map());
  const [newId, setNewId] = useState<string>();
  const [reason, setReason] = useState('');
  const [preview, setPreview] = useState<ResponsibilityOutcome[]>();
  const [result, setResult] = useState<ResponsibilityResult>();
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string>();
  const [key, setKey] = useState(newIdempotencyKey);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [text]);
  useEffect(() => setPage(1), [personId, unitId, status, debounced]);

  const list = useResponsibilities(system, kind.code, {
    personId,
    unitId,
    status,
    text: debounced || undefined,
    page,
    pageSize: PAGE_SIZE,
  });
  const candidates = useResponsibilityCandidates(system, kind.code, '', kind.restrictedCandidates);
  const previewing = usePreviewResponsibilities(system, kind.code);
  const reassign = useReassignResponsibilities(system, kind.code);
  const rows = list.data?.items ?? [];
  const allOnPage = rows.length > 0 && rows.every((row) => selected.has(row.id));
  const statusLabel = (code: string | null | undefined) => {
    const option = kind.statuses.find((s) => s.code === code);
    return option ? pickText(i18n.language, option.labelAr, option.labelEn) : (code ?? '—');
  };
  const name = (value: { nameAr: string | null; nameEn: string | null } | null | undefined) =>
    value ? pickText(i18n.language, value.nameAr, value.nameEn) || '—' : '—';
  const body = () => ({
    items: [...selected.values()].map((row) => ({ id: row.id, version: row.version })),
    newId: newId ?? null,
    reason: reason.trim(),
  });
  const valid = selected.size > 0 && !!newId && reason.trim().length > 0;
  const allowedCount = preview?.filter((o) => o.outcome !== 'blocked').length ?? 0;

  const toggle = (row: ResponsibilityRow, on: boolean) =>
    setSelected((current) => {
      const next = new Map(current);
      if (on) next.set(row.id, row);
      else next.delete(row.id);
      return next;
    });

  const reset = () => {
    setSelected(new Map());
    setPreview(undefined);
    setResult(undefined);
    setReason('');
    setKey(newIdempotencyKey());
  };

  if (result) {
    return (
      <section
        className="flex flex-col gap-4"
        aria-labelledby="responsibilities-result"
        data-testid="responsibilities-result"
      >
        <h2 id="responsibilities-result" className="text-lg font-semibold">
          {t('result.title')}
        </h2>
        <StatusBanner
          tone={result.skipped > 0 ? 'warning' : 'success'}
          title={t('result.summary', {
            moved: result.moved,
            changeRequests: result.changeRequests,
            skipped: result.skipped,
          })}
          description={t('result.to', { name: name(result.new) })}
        />
        <OutcomeTable outcomes={result.items} />
        <Button className="self-start" variant="outline" onClick={reset}>
          {t('result.again')}
        </Button>
      </section>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {kind.noteKey && <StatusBanner tone="info" title={translateMessageKey(tc, kind.noteKey)} />}

      <section
        className="flex flex-col gap-3 rounded-lg border bg-card p-4"
        aria-labelledby="responsibilities-filters"
      >
        <h2 id="responsibilities-filters" className="text-sm font-semibold">
          {t('filters.title')}
        </h2>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <FormField label={t('filters.person')}>
            {unitKind ? (
              <UnitSelect value={personId} onChange={setPersonId} allowEmpty />
            ) : (
              <EmployeePicker value={personId} onChange={setPersonId} />
            )}
          </FormField>
          {!unitKind && (
            <FormField label={t('filters.unit')}>
              <UnitSelect value={unitId} onChange={setUnitId} allowEmpty />
            </FormField>
          )}
          {kind.statuses.length > 0 && (
            <FormField label={t('filters.status')}>
              <Select
                value={status ?? '-'}
                onValueChange={(value) => setStatus(value === '-' ? undefined : value)}
                options={[
                  { value: '-', label: t('filters.anyStatus') },
                  ...kind.statuses.map((s) => ({
                    value: s.code,
                    label: pickText(i18n.language, s.labelAr, s.labelEn),
                  })),
                ]}
              />
            </FormField>
          )}
          <FormField label={t('filters.text')}>
            <Input type="search" value={text} onChange={(e) => setText(e.target.value)} maxLength={200} />
          </FormField>
        </div>
      </section>

      {list.isError && <StatusBanner tone="danger" title={errorMessage(tc, list.error)} />}
      {list.isPending && <Skeleton className="h-48" />}
      {list.data && rows.length === 0 && <EmptyState title={t('table.none')} />}
      {rows.length > 0 && (
        <section className="flex flex-col gap-3" aria-labelledby="responsibilities-records">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="responsibilities-records" className="font-semibold">
              {t('table.caption')}
            </h2>
            <span className="text-sm text-muted-foreground" aria-live="polite">
              {t('table.selected', { count: selected.size })}
            </span>
          </div>
          <div className="overflow-x-auto">
            <Table data-testid="responsibilities-table">
              <TableHead>
                <tr>
                  <TableHeaderCell className="w-10">
                    <Checkbox
                      label={<span className="sr-only">{t('table.selectAll')}</span>}
                      checked={allOnPage}
                      onChange={(e) => rows.forEach((row) => toggle(row, e.target.checked))}
                      data-testid="responsibilities-select-all"
                    />
                  </TableHeaderCell>
                  <TableHeaderCell>{t('table.number')}</TableHeaderCell>
                  <TableHeaderCell>{t('table.title')}</TableHeaderCell>
                  <TableHeaderCell>{t('table.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('table.responsible')}</TableHeaderCell>
                  {!unitKind && <TableHeaderCell>{t('table.unit')}</TableHeaderCell>}
                </tr>
              </TableHead>
              <TableBody>
                {rows.map((row) => {
                  const title = pickText(i18n.language, row.titleAr, row.titleEn);
                  const context = pickText(i18n.language, row.contextAr, row.contextEn);
                  return (
                    <TableRow key={row.id} data-testid={`responsibility-row-${row.id}`}>
                      <TableCell>
                        <Checkbox
                          label={
                            <span className="sr-only">
                              {t('table.select', { record: row.number ?? (title || row.id) })}
                            </span>
                          }
                          checked={selected.has(row.id)}
                          onChange={(e) => toggle(row, e.target.checked)}
                        />
                      </TableCell>
                      <TableCell dir="ltr" className="whitespace-nowrap text-start">
                        {row.number ?? '—'}
                      </TableCell>
                      <TableCell>
                        {title ? (
                          row.link ? (
                            <a href={row.link} className="hover:underline">
                              {title}
                            </a>
                          ) : (
                            title
                          )
                        ) : (
                          <span className="flex items-center gap-1 text-muted-foreground">
                            <Lock className="size-3" aria-hidden />
                            {t('table.restricted')}
                          </span>
                        )}
                        {context && (
                          <span className="block text-xs text-muted-foreground" dir="auto">
                            {t('table.context')}: {context}
                          </span>
                        )}
                      </TableCell>
                      <TableCell>{statusLabel(row.status)}</TableCell>
                      <TableCell>{name(row.responsible)}</TableCell>
                      {!unitKind && <TableCell>{name(row.unit)}</TableCell>}
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
          {list.data && (
            <Pagination
              page={page}
              totalPages={Math.ceil(list.data.total / PAGE_SIZE)}
              totalCount={list.data.total}
              onPageChange={setPage}
            />
          )}
        </section>
      )}

      {!preview ? (
        <form
          className="flex max-w-2xl flex-col gap-4 rounded-lg border bg-card p-5"
          aria-labelledby="responsibilities-change"
          onSubmit={(event) => {
            event.preventDefault();
            setError(undefined);
            previewing.mutate(body(), {
              onSuccess: setPreview,
              onError: (failure) => setError(errorMessage(tc, failure)),
            });
          }}
        >
          <h2 id="responsibilities-change" className="font-semibold">
            {t('change.title')}
          </h2>
          {error && <StatusBanner tone="danger" title={error} />}
          <FormField
            label={unitKind ? t('change.unit') : t('change.person')}
            required
            hint={kind.restrictedCandidates ? t('change.eligible') : undefined}
          >
            {unitKind ? (
              <UnitSelect value={newId} onChange={setNewId} />
            ) : kind.restrictedCandidates ? (
              <Select
                value={newId}
                onValueChange={setNewId}
                placeholder={candidates.data?.length === 0 ? t('change.noCandidates') : t('change.search')}
                options={(candidates.data ?? []).map((c) => ({ value: c.id, label: name(c) }))}
              />
            ) : (
              <EmployeePicker value={newId} onChange={setNewId} />
            )}
          </FormField>
          <FormField label={t('change.reason')} required>
            <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
          </FormField>
          <Button
            type="submit"
            className="self-end"
            disabled={!valid || previewing.isPending}
            data-testid="responsibilities-preview"
          >
            {t('change.preview')}
          </Button>
        </form>
      ) : (
        <section
          className="flex flex-col gap-4"
          aria-labelledby="responsibilities-preview-title"
          data-testid="responsibilities-preview-result"
        >
          <h2 id="responsibilities-preview-title" className="font-semibold">
            {t('change.previewTitle')}
          </h2>
          {error && <StatusBanner tone="danger" title={error} />}
          <OutcomeTable outcomes={preview} />
          {allowedCount === 0 && <StatusBanner tone="warning" title={t('change.nothingAllowed')} />}
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="outline" onClick={() => setPreview(undefined)}>
              {t('change.back')}
            </Button>
            <Button
              disabled={allowedCount === 0}
              onClick={() => setConfirming(true)}
              data-testid="responsibilities-confirm"
            >
              {t('change.confirm', { count: allowedCount })}
            </Button>
          </div>
        </section>
      )}

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={t('change.confirmTitle')}
        description={t('change.confirmDescription')}
        confirmLabel={t('change.confirm', { count: allowedCount })}
        loading={reassign.isPending}
        onConfirm={() =>
          reassign.mutate(
            { idempotencyKey: key, body: body() },
            {
              onSuccess: (done) => {
                setConfirming(false);
                setResult(done);
                setSelected(new Map());
              },
              onError: (failure) => {
                setConfirming(false);
                setError(errorMessage(tc, failure));
              },
            },
          )
        }
      />
    </div>
  );
}

/** Per-record outcome of a preview or a reassignment, with the reason of anything blocked or skipped. */
function OutcomeTable({ outcomes }: { outcomes: readonly ResponsibilityOutcome[] }) {
  const { t, i18n } = useTranslation('responsibilities');
  const { t: tc } = useTranslation();
  return (
    <div className="overflow-x-auto">
      <Table data-testid="responsibilities-outcomes">
        <TableHead>
          <tr>
            <TableHeaderCell>{t('table.number')}</TableHeaderCell>
            <TableHeaderCell>{t('table.title')}</TableHeaderCell>
            <TableHeaderCell>{t('table.status')}</TableHeaderCell>
          </tr>
        </TableHead>
        <TableBody>
          {outcomes.map((outcome) => (
            <TableRow key={outcome.id}>
              <TableCell dir="ltr" className="whitespace-nowrap text-start">
                {outcome.number ?? '—'}
              </TableCell>
              <TableCell>{pickText(i18n.language, outcome.titleAr, outcome.titleEn) || '—'}</TableCell>
              <TableCell>
                <div className="flex flex-col items-start gap-1">
                  <StatusChip tone={outcomeTone[outcome.outcome] ?? 'neutral'}>
                    {t(`outcomes.${outcome.outcome as 'allowed'}`, { defaultValue: outcome.outcome })}
                  </StatusChip>
                  {outcome.messageKey && (
                    <span className="text-xs text-muted-foreground">
                      {translateMessageKey(tc, outcome.messageKey)}
                    </span>
                  )}
                  {outcome.reference && (
                    <span className="text-xs" dir="auto">
                      {t('result.reference', { reference: outcome.reference })}
                    </span>
                  )}
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
