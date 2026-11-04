import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { Download, Flag, Plus, Search, ShieldAlert, Sparkles, TrendingUp, TriangleAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { cn } from '@/lib/cn';
import { formatDate } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FileDrop,
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
  useToast,
} from '@/ui';
import {
  IDENTIFIED_RISK_KEY,
  registerViews,
  reportUrl,
  riskApi,
  riskStatuses,
  statusTone,
  useRiskOptions,
  useRisks,
  type SuggestedRisk,
} from './api';
import { RatingChip, RiskPageLayout, useNames, useRiskLabels, useRiskT, useBands } from './components';

const route = getRouteApi('/app/risk/risks');
const ALL = '__all';

/** Risk register (docs/design/risk.md §12): saved views, filters, rating chips, export, AI identification. */
export function RiskRegisterPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const options = useRiskOptions();
  const labels = useRiskLabels();
  const names = useNames();
  const { bands } = useBands();
  const canIdentify = useCan('risk.ai.identify');
  const [identify, setIdentify] = useState(false);
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    const handle = setTimeout(() => {
      if ((search.search ?? '') !== text)
        void navigate({ search: (p) => ({ ...p, search: text || undefined, page: undefined }) });
    }, 300);
    return () => clearTimeout(handle);
  }, [text, search.search, navigate]);
  const view = search.view ?? 'all';
  const risks = useRisks({
    page: search.page ?? 1,
    pageSize: 25,
    ...(view !== 'all' ? { view } : {}),
    ...(search.search ? { search: search.search } : {}),
    ...(search.unitId ? { unitId: search.unitId } : {}),
    ...(search.category ? { category: search.category } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.band ? { residualBand: search.band } : {}),
  });
  const set = (patch: Record<string, unknown>) => void navigate({ search: (p) => ({ ...p, ...patch, page: undefined }) });

  return (
    <RiskPageLayout testId="risk-register">
      <PageHeader
        title={t('register.title')}
        description={t('register.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            {options.data?.canExport &&
              (['pdf', 'xlsx'] as const).map((format) => (
                <Button key={format} variant="outline" size="sm" asChild>
                  <a href={reportUrl('register', format, language)} data-testid={`risk-register-export-${format}`}>
                    <Download aria-hidden />
                    {t(`reports.formats.${format}`)}
                  </a>
                </Button>
              ))}
            {canIdentify && options.data?.canCreate && (
              <Button variant="outline" onClick={() => setIdentify(true)} data-testid="risk-ai-identify">
                <Sparkles aria-hidden />
                {t('ai.identify.action')}
              </Button>
            )}
            {options.data?.canCreate && (
              <Button asChild>
                <Link to="/risk/risks/new" data-testid="risk-new">
                  <Plus aria-hidden />
                  {t('register.new')}
                </Link>
              </Button>
            )}
          </div>
        }
      />
      <div role="tablist" aria-label={t('register.views')} className="flex flex-wrap gap-1" data-testid="risk-views">
        {registerViews.map((v) => (
          <button
            key={v}
            type="button"
            role="tab"
            aria-selected={view === v}
            data-testid={`risk-view-${v}`}
            onClick={() => set({ view: v === 'all' ? undefined : v })}
            className={cn(
              'rounded-full border px-3 py-1 text-sm',
              view === v ? 'border-primary bg-primary text-primary-foreground' : 'hover:bg-muted',
            )}
          >
            {k(`views.${v}`)}
          </button>
        ))}
      </div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
        <div className="relative">
          <Search className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground" aria-hidden />
          <Input
            className="ps-9"
            placeholder={t('register.search')}
            aria-label={t('register.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
            data-testid="risk-search"
          />
        </div>
        <UnitSelect allowEmpty value={search.unitId} onChange={(unitId) => set({ unitId })} />
        <Select
          aria-label={t('fields.category')}
          value={search.category ?? ALL}
          onValueChange={(v) => set({ category: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('register.allCategories') },
            ...(options.data?.categories ?? []).map((c) => ({ value: c.code, label: labels.category(c.code) })),
          ]}
        />
        <Select
          aria-label={t('fields.status')}
          value={search.status ?? ALL}
          onValueChange={(v) => set({ status: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('register.allStatuses') },
            ...riskStatuses.map((s) => ({ value: s, label: k(`status.${s}`) })),
          ]}
        />
        <Select
          aria-label={t('register.residualBand')}
          value={search.band ?? ALL}
          onValueChange={(v) => set({ band: v === ALL ? undefined : v })}
          options={[
            { value: ALL, label: t('register.allBands') },
            ...bands.map((b) => ({ value: b.code, label: names.text(b.labelAr, b.labelEn) || b.code })),
          ]}
        />
      </div>
      {risks.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, risks.error)} />
      ) : !risks.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : risks.data.items.length === 0 ? (
        <EmptyState icon={ShieldAlert} title={t('register.empty')} />
      ) : (
        <>
          <Table data-testid="risk-register-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.inherent')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.residual')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.target')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.residualStatus')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.nextReviewOn')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {risks.data.items.map((r) => (
                <TableRow key={r.id} data-testid="risk-row">
                  <TableCell>
                    <Link
                      to="/risk/risks/$riskId"
                      params={{ riskId: r.id }}
                      className="font-medium text-primary hover:underline"
                    >
                      <bdi>{r.number}</bdi>
                    </Link>
                  </TableCell>
                  <TableCell>
                    <span className="flex flex-col gap-1">
                      <span>{names.text(r.titleAr, r.titleEn)}</span>
                      <span className="flex flex-wrap gap-1">
                        {r.top10 && (
                          <StatusChip tone="danger">
                            <Flag className="me-1 size-3" aria-hidden />
                            {t('flags.top10', { rank: r.top10Rank ?? '' })}
                          </StatusChip>
                        )}
                        {r.escalated && (
                          <StatusChip tone="warning">
                            <TrendingUp className="me-1 size-3" aria-hidden />
                            {t('flags.escalated')}
                          </StatusChip>
                        )}
                        {r.reassessmentRequired && (
                          <StatusChip tone="warning">
                            <TriangleAlert className="me-1 size-3" aria-hidden />
                            {t('flags.reassessment')}
                          </StatusChip>
                        )}
                        {r.withinAppetite === false && <StatusChip tone="danger">{t('flags.outsideAppetite')}</StatusChip>}
                      </span>
                    </span>
                  </TableCell>
                  <TableCell>{names.unit(r.unit)}</TableCell>
                  <TableCell>{names.person(r.owner)}</TableCell>
                  <TableCell>
                    <RatingChip band={r.inherentBand} score={r.inherentScore} testId="risk-chip-inherent" />
                  </TableCell>
                  <TableCell>
                    <RatingChip band={r.residualBand} score={r.residualScore} testId="risk-chip-residual" />
                  </TableCell>
                  <TableCell>
                    <RatingChip band={r.targetBand} score={r.targetScore} testId="risk-chip-target" />
                  </TableCell>
                  <TableCell>{k(`residualStatus.${r.residualStatus}`)}</TableCell>
                  <TableCell className={r.reviewOverdue ? 'font-medium text-danger' : undefined}>
                    {r.nextReviewOn ? formatDate(r.nextReviewOn, language) : '—'}
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={statusTone[r.status] ?? 'neutral'}>{k(`status.${r.status}`)}</StatusChip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pagination
            page={risks.data.page}
            totalPages={risks.data.totalPages ?? 1}
            totalCount={risks.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
      {identify && <IdentifyDialog onClose={() => setIdentify(false)} />}
    </RiskPageLayout>
  );
}

/** AI: identify risks from a description or a document; "Use" opens the wizard pre-filled (a draft the user reviews). */
function IdentifyDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const labels = useRiskLabels();
  const names = useNames();
  const [description, setDescription] = useState('');
  const [file, setFile] = useState<File | undefined>();
  const [busy, setBusy] = useState(false);
  const [risks, setRisks] = useState<SuggestedRisk[]>();
  async function run() {
    setBusy(true);
    try {
      const result = file
        ? await riskApi.identifyFromDocument({ file, description: description || null })
        : await riskApi.identify(description);
      setRisks(result.risks);
    } catch (e) {
      toast({ tone: 'error', title: errorMessage(tc, e) });
    } finally {
      setBusy(false);
    }
  }
  function use(risk: SuggestedRisk) {
    try {
      sessionStorage.setItem(IDENTIFIED_RISK_KEY, JSON.stringify(risk));
    } catch {
      // storage unavailable: the wizard opens empty
    }
    void navigate({ to: '/risk/risks/new' });
  }
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('ai.identify.title')}
        description={t('ai.identify.hint')}
        className="max-w-3xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button onClick={() => void run()} loading={busy} disabled={!description.trim() && !file} data-testid="risk-ai-identify-run">
              <Sparkles aria-hidden />
              {t('ai.identify.run')}
            </Button>
          </>
        }
      >
        <FormField label={t('ai.identify.description')}>
          <Textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={4} data-testid="risk-ai-identify-text" />
        </FormField>
        <FileDrop
          label={t('ai.identify.file')}
          hint={t('ai.identify.fileHint')}
          accept=".pdf,.docx,.txt,.pptx,.xlsx,.png,.jpg,.jpeg"
          file={file ?? null}
          onFile={(f) => setFile(f ?? undefined)}
        />
        {risks && (
          <div className="flex flex-col gap-3" data-testid="risk-ai-identify-results">
            <AiBadge label={t('ai.generatedDraft')} className="self-start" />
            {risks.length === 0 && <p className="text-sm text-muted-foreground">{t('ai.none')}</p>}
            {risks.map((r, index) => (
              <article key={index} className="flex flex-col gap-1 rounded border p-3 text-sm">
                <header className="flex flex-wrap items-center justify-between gap-2">
                  <strong>{names.text(r.titleAr, r.titleEn)}</strong>
                  <Button size="sm" onClick={() => use(r)} data-testid="risk-ai-identify-use">
                    {t('ai.use')}
                  </Button>
                </header>
                <p>
                  <span className="text-muted-foreground">{t('fields.cause')}:</span> {r.cause}
                </p>
                <p>
                  <span className="text-muted-foreground">{t('fields.eventText')}:</span> {r.eventText}
                </p>
                <p>
                  <span className="text-muted-foreground">{t('fields.consequence')}:</span> {r.consequence}
                </p>
                <p className="text-muted-foreground">
                  {labels.category(r.categoryCode)} · {t('fields.likelihood')} {r.likelihood ?? '—'} · {t('fields.impact')}{' '}
                  {r.impact ?? '—'}
                </p>
                {r.reasoning && <p className="text-xs text-muted-foreground">{r.reasoning}</p>}
              </article>
            ))}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
