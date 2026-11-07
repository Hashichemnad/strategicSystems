import { Link } from '@tanstack/react-router';
import { BookOpen, Building, Pencil, Plus, Search, Sparkles } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { SimpleDialog, useNames } from '@/features/cap/components';
import {
  Button,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
  Section,
} from '@/ui';
import {
  benchApi,
  benchLookups,
  comparatorScopes,
  useBenchMutation,
  useBenchOptions,
  useBenchSettings,
  useComparator,
  useComparators,
  usePractices,
  type BenchBestPractice,
  type BenchComparator,
  type BenchPracticeValues,
  type BenchSettings,
  type ComparatorValues,
  type PracticeSearch,
} from './api';
import { BenchPageLayout, StageChip, useBenchAction, useBenchLabels, useBenchT } from './components';
import { SystemSettingsLinks } from '@/features/access/SystemSettingsLinks';

const ALL = '__all';

/** Comparator register (SPEC §8.5): entities compared with, their sector / scope, contacts, rating and study history. */
export function BenchComparatorsPage() {
  const { t } = useTranslation('bench');
  const { t: tc } = useTranslation();
  const { k } = useBenchT();
  const options = useBenchOptions();
  const labels = useBenchLabels();
  const names = useNames();
  const [search, setSearch] = useState('');
  const [scope, setScope] = useState<string>();
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<BenchComparator | 'new'>();
  const [viewing, setViewing] = useState<string>();
  const list = useComparators({
    page,
    pageSize: 25,
    ...(search ? { search } : {}),
    ...(scope ? { scope } : {}),
  });

  return (
    <BenchPageLayout testId="bench-comparators">
      <PageHeader
        title={t('comparators.title')}
        description={t('comparators.description')}
        actions={
          options.data?.canManageComparators && (
            <Button onClick={() => setEditing('new')} data-testid="bench-comparator-new">
              <Plus aria-hidden />
              {t('comparators.new')}
            </Button>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-3">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('common.search')}
            aria-label={t('common.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Select
          aria-label={t('comparators.scope')}
          value={scope ?? ALL}
          onValueChange={(v) => setScope(v === ALL ? undefined : v)}
          options={[
            { value: ALL, label: t('common.allScopes') },
            ...comparatorScopes.map((s) => ({ value: s, label: k(`scope.${s}`) })),
          ]}
        />
      </div>
      {list.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, list.error)} />
      ) : !list.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : list.data.items.length === 0 ? (
        <EmptyState icon={Building} title={t('comparators.empty')} />
      ) : (
        <>
          <Table data-testid="bench-comparators-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('comparators.name')}</TableHeaderCell>
                <TableHeaderCell>{t('comparators.sector')}</TableHeaderCell>
                <TableHeaderCell>{t('comparators.scope')}</TableHeaderCell>
                <TableHeaderCell>{t('comparators.country')}</TableHeaderCell>
                <TableHeaderCell>{t('comparators.studies')}</TableHeaderCell>
                <TableHeaderCell>{t('comparators.rating')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{t('common.actions')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {list.data.items.map((c) => (
                <TableRow key={c.id} data-testid="bench-comparator-row">
                  <TableCell>
                    <button
                      type="button"
                      className="text-start font-medium text-primary hover:underline"
                      onClick={() => setViewing(c.id)}
                    >
                      {names.text(c.values.nameAr, c.values.nameEn)}
                    </button>
                    {!c.values.active && <StatusChip tone="neutral">{t('common.inactive')}</StatusChip>}
                  </TableCell>
                  <TableCell>{labels.lookup(benchLookups.sector, c.values.sectorCode)}</TableCell>
                  <TableCell>{k(`scope.${c.values.scope}`)}</TableCell>
                  <TableCell>{c.values.country ?? '—'}</TableCell>
                  <TableCell>{c.studies}</TableCell>
                  <TableCell>
                    {c.averageRating == null
                      ? '—'
                      : `${'★'.repeat(Math.round(c.averageRating))} (${c.averageRating.toFixed(1)})`}
                  </TableCell>
                  <TableCell>
                    {options.data?.canManageComparators && (
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={tc('actions.edit')}
                        onClick={() => setEditing(c)}
                      >
                        <Pencil aria-hidden />
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pagination
            page={list.data.page}
            totalPages={list.data.totalPages ?? 1}
            totalCount={list.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
      {editing && (
        <ComparatorDialog
          comparator={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(undefined)}
        />
      )}
      {viewing && <ComparatorHistory id={viewing} onClose={() => setViewing(undefined)} />}
    </BenchPageLayout>
  );
}

function ComparatorDialog({ comparator, onClose }: { comparator?: BenchComparator; onClose: () => void }) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const labels = useBenchLabels();
  const [v, setV] = useState<ComparatorValues>(
    comparator?.values ?? {
      nameAr: '',
      nameEn: '',
      sectorCode: null,
      country: '',
      scope: 'local',
      website: '',
      contactName: '',
      contactEmail: '',
      contactPhone: '',
      notes: '',
      active: true,
    },
  );
  const create = useBenchAction(benchApi.createComparator, onClose);
  const update = useBenchAction(benchApi.updateComparator, onClose);
  const set = (patch: Partial<ComparatorValues>) => setV((x) => ({ ...x, ...patch }));
  const text = (key: keyof ComparatorValues, label: string, dir?: 'rtl' | 'ltr') => (
    <FormField label={label}>
      <Input
        dir={dir}
        value={(v[key] as string | null) ?? ''}
        onChange={(e) => set({ [key]: e.target.value })}
      />
    </FormField>
  );
  const clean = Object.fromEntries(
    Object.entries(v).map(([key, x]) => [key, x === '' ? null : x]),
  ) as ComparatorValues;
  return (
    <SimpleDialog
      title={comparator ? t('comparators.edit') : t('comparators.new')}
      onClose={onClose}
      busy={create.busy || update.busy}
      disabled={!(v.nameAr || v.nameEn)}
      submitLabel={t('common.save')}
      testId="bench-comparator-save"
      wide
      onSubmit={() =>
        comparator
          ? update.run({ id: comparator.id, values: clean, version: comparator.version })
          : create.run(clean)
      }
    >
      <div className="grid gap-3 md:grid-cols-2">
        {text('nameAr', t('comparators.nameAr'), 'rtl')}
        {text('nameEn', t('comparators.nameEn'), 'ltr')}
        <FormField label={t('comparators.sector')}>
          <Select
            value={v.sectorCode ?? '-'}
            onValueChange={(x) => set({ sectorCode: x === '-' ? null : x })}
            options={[{ value: '-', label: '—' }, ...labels.lookupOptions(benchLookups.sector)]}
          />
        </FormField>
        <FormField label={t('comparators.scope')}>
          <Select
            value={v.scope}
            onValueChange={(scope) => set({ scope })}
            options={comparatorScopes.map((s) => ({ value: s, label: k(`scope.${s}`) }))}
          />
        </FormField>
        {text('country', t('comparators.country'))}
        {text('website', t('comparators.website'), 'ltr')}
        {text('contactName', t('comparators.contactName'))}
        {text('contactEmail', t('comparators.contactEmail'), 'ltr')}
        {text('contactPhone', t('comparators.contactPhone'), 'ltr')}
      </div>
      <FormField label={t('comparators.notes')}>
        <Textarea value={v.notes ?? ''} onChange={(e) => set({ notes: e.target.value })} />
      </FormField>
      <Switch checked={v.active} onCheckedChange={(active) => set({ active })} label={t('common.active')} />
    </SimpleDialog>
  );
}

function ComparatorHistory({ id, onClose }: { id: string; onClose: () => void }) {
  const { t } = useTranslation('bench');
  const names = useNames();
  const c = useComparator(id);
  return (
    <SimpleDialog
      title={c.data ? names.text(c.data.values.nameAr, c.data.values.nameEn) : t('comparators.title')}
      description={t('comparators.history')}
      onClose={onClose}
      busy={false}
      submitLabel={t('common.close')}
      testId="bench-comparator-close"
      onSubmit={onClose}
    >
      {!c.data ? (
        <Skeleton className="h-24" />
      ) : c.data.history.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('comparators.noHistory')}</p>
      ) : (
        <ul className="flex flex-col divide-y text-sm" data-testid="bench-comparator-history">
          {c.data.history.map((h) => (
            <li key={h.studyId} className="flex items-center justify-between gap-2 py-2">
              <Link
                to="/bench/studies/$studyId"
                params={{ studyId: h.studyId }}
                className="text-primary hover:underline"
              >
                <bdi>{h.number}</bdi> {names.text(h.titleAr, h.titleEn)} · {h.targetYear}
              </Link>
              <span className="flex items-center gap-1">
                <StageChip stage={h.stage} status="active" />
                {h.rating && <span>{'★'.repeat(h.rating)}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </SimpleDialog>
  );
}

/** Best-practice library (SPEC §8.5): practices adopted from studies or added by the PM Office; keyword and AI search. */
export function BenchPracticesPage() {
  const { t } = useTranslation('bench');
  const { t: tc } = useTranslation();
  const { k } = useBenchT();
  const options = useBenchOptions();
  const labels = useBenchLabels();
  const names = useNames();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [question, setQuestion] = useState('');
  const [aiResult, setAiResult] = useState<PracticeSearch>();
  const [editing, setEditing] = useState<BenchBestPractice | 'new'>();
  const list = usePractices({ page, pageSize: 20, ...(search ? { search } : {}) });
  const ai = useBenchAction(benchApi.aiSearch);
  const aiFeature = useAiFeature('bench-search');
  const aiAllowed = useCan('bench.ai.search');
  const items = aiResult?.results.items ?? list.data?.items;

  return (
    <BenchPageLayout testId="bench-practices-library">
      <PageHeader
        title={t('practices.title')}
        description={t('practices.description')}
        actions={
          options.data?.canManagePractices && (
            <Button onClick={() => setEditing('new')} data-testid="bench-practice-new">
              <Plus aria-hidden />
              {t('practices.new')}
            </Button>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-2">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('common.search')}
            aria-label={t('common.search')}
            value={search}
            onChange={(e) => {
              setAiResult(undefined);
              setSearch(e.target.value);
            }}
          />
        </div>
        {aiFeature && aiAllowed && (
          <div className="flex gap-2">
            <Input
              placeholder={t('practices.ask')}
              aria-label={t('practices.ask')}
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              data-testid="bench-ai-search-question"
            />
            <Button
              variant="outline"
              loading={ai.busy}
              disabled={!question.trim()}
              onClick={() => ai.run(question, setAiResult)}
              data-testid="bench-ai-search"
            >
              <Sparkles aria-hidden />
              {t('ai.search')}
            </Button>
          </div>
        )}
      </div>
      {aiResult && (
        <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <AiBadge /> {t('practices.keywords')}: {aiResult.keywords.join('، ')}
        </p>
      )}
      {list.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, list.error)} />
      ) : !items ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : items.length === 0 ? (
        <EmptyState icon={BookOpen} title={t('practices.empty')} />
      ) : (
        <div className="grid gap-3 md:grid-cols-2" data-testid="bench-practice-cards">
          {items.map((p) => (
            <Section
              key={p.id}
              title={names.text(p.values.titleAr, p.values.titleEn)}
              testId="bench-practice-card"
              actions={
                options.data?.canManagePractices && (
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label={tc('actions.edit')}
                    onClick={() => setEditing(p)}
                  >
                    <Pencil aria-hidden />
                  </Button>
                )
              }
            >
              <p className="whitespace-pre-line text-sm">{p.values.description}</p>
              {p.values.applicability && (
                <p className="text-sm text-muted-foreground">{p.values.applicability}</p>
              )}
              <div className="flex flex-wrap items-center gap-1 text-xs">
                {p.values.topic && <StatusChip tone="info">{p.values.topic}</StatusChip>}
                {p.values.sectorCode && (
                  <StatusChip tone="neutral">
                    {labels.lookup(benchLookups.sector, p.values.sectorCode)}
                  </StatusChip>
                )}
                <StatusChip tone="neutral">{k(`origin.${p.origin}`)}</StatusChip>
                {(p.values.tags ?? []).map((tag) => (
                  <span key={tag} className="rounded bg-muted px-1.5 py-0.5">
                    #{tag}
                  </span>
                ))}
              </div>
              {p.studyId && (
                <Link
                  to="/bench/studies/$studyId"
                  params={{ studyId: p.studyId }}
                  className="text-sm text-primary hover:underline"
                >
                  <bdi>{p.studyNumber}</bdi>
                  {p.comparatorNameAr || p.comparatorNameEn
                    ? ` · ${names.text(p.comparatorNameAr, p.comparatorNameEn)}`
                    : ''}
                </Link>
              )}
            </Section>
          ))}
        </div>
      )}
      {!aiResult && list.data && (list.data.totalPages ?? 1) > 1 && (
        <Pagination
          page={list.data.page}
          totalPages={list.data.totalPages ?? 1}
          totalCount={list.data.totalCount}
          onPageChange={setPage}
        />
      )}
      {editing && (
        <PracticeDialog
          practice={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(undefined)}
        />
      )}
    </BenchPageLayout>
  );
}

function PracticeDialog({ practice, onClose }: { practice?: BenchBestPractice; onClose: () => void }) {
  const { t } = useTranslation('bench');
  const labels = useBenchLabels();
  const [v, setV] = useState<BenchPracticeValues>(
    practice?.values ?? {
      titleAr: '',
      titleEn: '',
      description: '',
      topic: '',
      tags: [],
      applicability: '',
      sectorCode: null,
      active: true,
    },
  );
  const [tags, setTags] = useState((practice?.values.tags ?? []).join(', '));
  const create = useBenchAction(benchApi.createPractice, onClose);
  const update = useBenchAction(benchApi.updatePractice, onClose);
  const set = (patch: Partial<BenchPracticeValues>) => setV((x) => ({ ...x, ...patch }));
  const values: BenchPracticeValues = {
    ...v,
    titleAr: v.titleAr || null,
    titleEn: v.titleEn || null,
    description: v.description || null,
    topic: v.topic || null,
    applicability: v.applicability || null,
    tags: tags
      .split(/[,،]/)
      .map((x) => x.trim())
      .filter(Boolean),
  };
  return (
    <SimpleDialog
      title={practice ? t('practices.edit') : t('practices.new')}
      onClose={onClose}
      busy={create.busy || update.busy}
      disabled={!(v.titleAr || v.titleEn)}
      submitLabel={t('common.save')}
      testId="bench-practice-save"
      wide
      onSubmit={() =>
        practice ? update.run({ id: practice.id, values, version: practice.version }) : create.run(values)
      }
    >
      <FormField label={t('practices.titleAr')}>
        <Input dir="rtl" value={v.titleAr ?? ''} onChange={(e) => set({ titleAr: e.target.value })} />
      </FormField>
      <FormField label={t('practices.titleEn')}>
        <Input dir="ltr" value={v.titleEn ?? ''} onChange={(e) => set({ titleEn: e.target.value })} />
      </FormField>
      <FormField label={t('practices.descriptionField')}>
        <Textarea value={v.description ?? ''} onChange={(e) => set({ description: e.target.value })} />
      </FormField>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.topic')}>
          <Input value={v.topic ?? ''} onChange={(e) => set({ topic: e.target.value })} />
        </FormField>
        <FormField label={t('comparators.sector')}>
          <Select
            value={v.sectorCode ?? '-'}
            onValueChange={(x) => set({ sectorCode: x === '-' ? null : x })}
            options={[{ value: '-', label: '—' }, ...labels.lookupOptions(benchLookups.sector)]}
          />
        </FormField>
      </div>
      <FormField label={t('practices.tags')} hint={t('practices.tagsHint')}>
        <Input value={tags} onChange={(e) => setTags(e.target.value)} />
      </FormField>
      <FormField label={t('practices.applicability')}>
        <Textarea value={v.applicability ?? ''} onChange={(e) => set({ applicability: e.target.value })} />
      </FormField>
      <Switch checked={v.active} onCheckedChange={(active) => set({ active })} label={t('common.active')} />
    </SimpleDialog>
  );
}

/** Settings → Benchmarking: pre-quarter search notice, impact follow-up, KPI benchmark update. */
export function BenchSettingsPage() {
  const { t } = useTranslation('bench');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const doc = useBenchSettings();
  const save = useBenchMutation(benchApi.saveSettings);
  const [value, setValue] = useState<BenchSettings>();
  useEffect(() => {
    if (doc.data?.value) setValue(doc.data.value);
  }, [doc.data]);
  if (doc.isError) return <StatusBanner tone="danger" title={errorMessage(tc, doc.error)} />;
  if (!value || !doc.data) return <Skeleton className="h-64 rounded-lg" />;
  const set = (patch: Partial<BenchSettings>) => setValue((v) => (v ? { ...v, ...patch } : v));
  return (
    <div className="flex flex-col gap-6" data-testid="bench-settings">
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description')}
        actions={
          <Button
            loading={save.isPending}
            data-testid="bench-settings-save"
            onClick={() =>
              save.mutate(
                { settings: value, version: doc.data.version },
                {
                  onSuccess: () => toast({ title: t('settings.saved') }),
                  onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                },
              )
            }
          >
            {t('common.save')}
          </Button>
        }
      />
      <SystemSettingsLinks system="bench" />
      <Section title={t('settings.notices')}>
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('settings.searchNoticeDays')}>
            <Input
              type="number"
              min={1}
              max={180}
              value={String(value.searchNoticeDays ?? 30)}
              onChange={(e) => set({ searchNoticeDays: Number(e.target.value) })}
            />
          </FormField>
          <FormField label={t('settings.impactFollowUpMonths')}>
            <Input
              type="number"
              min={1}
              max={36}
              value={String(value.impactFollowUpMonths ?? 6)}
              onChange={(e) => set({ impactFollowUpMonths: Number(e.target.value) })}
            />
          </FormField>
        </div>
        <Switch
          checked={value.countWorkingDays ?? false}
          onCheckedChange={(countWorkingDays) => set({ countWorkingDays })}
          label={t('settings.countWorkingDays')}
        />
        <Switch
          checked={value.kpiBenchmarkUpdate ?? false}
          onCheckedChange={(kpiBenchmarkUpdate) => set({ kpiBenchmarkUpdate })}
          label={t('settings.kpiBenchmarkUpdate')}
          description={t('settings.kpiBenchmarkUpdateHint')}
        />
      </Section>
    </div>
  );
}
