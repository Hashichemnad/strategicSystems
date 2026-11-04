import { Plus, Sparkles, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { priorities } from '@/features/cap/api';
import { Section, SimpleDialog, useDate, useNames } from '@/features/cap/components';
import { useKpiCards } from '@/features/kpi/api';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import {
  Button,
  Checkbox,
  DatePicker,
  FormField,
  Input,
  Select,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
} from '@/ui';
import {
  benchApi,
  comparatorScopes,
  outreachChannels,
  outreachResponses,
  practiceCategories,
  useComparators,
  useStudyAnalysis,
  useStudySearch,
  useStudyVisits,
  visitMethods,
  type BenchAnalysisInput,
  type BenchCandidate,
  type BenchCandidateInput,
  type BenchStudy,
  type BenchVisitInput,
  type ComparatorSuggestion,
} from './api';
import { useBenchAction, useBenchT } from './components';

const today = () => new Date().toISOString().slice(0, 10);

const stripCandidate = (r: BenchCandidateInput): BenchCandidateInput => ({
  comparatorId: r.comparatorId,
  newComparator: r.newComparator,
  selected: r.selected,
  reason: r.reason,
  availableInfo: r.availableInfo,
  contact: r.contact,
  codeOfConductAck: r.codeOfConductAck,
});

const stripVisit = (v: BenchVisitInput): BenchVisitInput => ({
  id: v.id,
  comparatorId: v.comparatorId,
  agreedOn: v.agreedOn,
  method: v.method,
  participantIds: v.participantIds,
  teamId: v.teamId,
  questionnaire: v.questionnaire,
  questionnaireSentOn: v.questionnaireSentOn,
  notes: v.notes,
});

function toInput(c: BenchCandidate): BenchCandidateInput {
  return {
    comparatorId: c.comparatorId,
    newComparator: null,
    selected: c.selected,
    reason: c.reason,
    availableInfo: c.availableInfo,
    contact: c.contact,
    codeOfConductAck: c.codeOfConductAck,
  };
}

/** Search stage (SPEC §8.2): candidate entities from the register or new, selection with reasons, code of conduct, outreach log, ratings. */
export function SearchTab({ study: s }: { study: BenchStudy }) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const names = useNames();
  const date = useDate();
  const search = useStudySearch(s.id);
  const [rows, setRows] =
    useState<(BenchCandidateInput & { label: string; key: string; source?: BenchCandidate })[]>();
  const [adding, setAdding] = useState(false);
  const [outreachFor, setOutreachFor] = useState<BenchCandidate>();
  const [suggestions, setSuggestions] = useState<ComparatorSuggestion>();
  const save = useBenchAction(benchApi.saveSearch);
  const submit = useBenchAction(benchApi.submitSearch);
  const rate = useBenchAction(benchApi.rate);
  const ai = useBenchAction(benchApi.aiComparators);
  const aiFeature = useAiFeature('bench-comparators');
  const aiAllowed = useCan('bench.ai.comparators');
  useEffect(() => {
    if (search.data)
      setRows(
        search.data.map((c) => ({
          ...toInput(c),
          key: c.id,
          label: names.text(c.nameAr, c.nameEn),
          source: c,
        })),
      );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search.data]);
  if (!rows) return <Skeleton className="h-48" />;
  const editable = s.can.editSearch;
  const update = (key: string, patch: Partial<BenchCandidateInput>) =>
    setRows((rs) => rs?.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const addRow = (input: BenchCandidateInput, label: string) =>
    setRows((rs) => [...(rs ?? []), { ...input, key: crypto.randomUUID(), label }]);

  return (
    <div className="flex flex-col gap-4" data-testid="bench-search">
      <Section
        title={t('search.title')}
        actions={
          editable && (
            <>
              {aiFeature && aiAllowed && (
                <Button
                  variant="outline"
                  size="sm"
                  loading={ai.busy}
                  onClick={() => ai.run(s.id, setSuggestions)}
                  data-testid="bench-ai-comparators"
                >
                  <Sparkles aria-hidden />
                  {t('ai.comparators')}
                </Button>
              )}
              <Button
                variant="outline"
                size="sm"
                onClick={() => setAdding(true)}
                data-testid="bench-candidate-add"
              >
                <Plus aria-hidden />
                {t('search.addCandidate')}
              </Button>
              <Button
                size="sm"
                loading={save.busy}
                onClick={() => save.run({ id: s.id, candidates: rows.map(stripCandidate) })}
                data-testid="bench-search-save"
              >
                {t('common.save')}
              </Button>
              {s.can.submitSearch && (
                <Button
                  size="sm"
                  loading={submit.busy}
                  onClick={() => submit.run({ id: s.id, comment: null })}
                  data-testid="bench-search-submit"
                >
                  {t('search.submit')}
                </Button>
              )}
            </>
          )
        }
      >
        <p className="text-sm text-muted-foreground">{t('search.hint')}</p>
        {suggestions && (
          <div className="rounded-md border p-3" data-testid="bench-ai-comparator-results">
            <div className="mb-2 flex items-center gap-2 text-sm font-medium">
              {t('ai.drafts')} <AiBadge />
            </div>
            <ul className="flex flex-col divide-y text-sm">
              {suggestions.entities.map((e, i) => (
                <li key={i} className="flex items-start justify-between gap-2 py-2">
                  <span>
                    {names.text(e.nameAr, e.nameEn)} {e.country && `· ${e.country}`}
                    {e.reason && <span className="block text-xs text-muted-foreground">{e.reason}</span>}
                  </span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      addRow(
                        {
                          comparatorId: e.registerId,
                          newComparator: e.registerId
                            ? null
                            : {
                                nameAr: e.nameAr,
                                nameEn: e.nameEn,
                                sectorCode: null,
                                country: e.country,
                                scope: e.scope ?? 'local',
                                website: null,
                                contactName: null,
                                contactEmail: null,
                                contactPhone: null,
                                notes: null,
                                active: true,
                              },
                          selected: false,
                          reason: e.reason,
                          availableInfo: null,
                          contact: null,
                          codeOfConductAck: false,
                        },
                        names.text(e.nameAr, e.nameEn),
                      )
                    }
                  >
                    {t('ai.use')}
                  </Button>
                </li>
              ))}
            </ul>
          </div>
        )}
        {rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('search.empty')}</p>
        ) : (
          <div className="flex flex-col gap-3">
            {rows.map((r) => (
              <div
                key={r.key}
                className="flex flex-col gap-3 rounded-md border p-3"
                data-testid="bench-candidate"
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-medium">
                    {r.label}
                    {r.source && (
                      <span className="ms-2 text-xs text-muted-foreground">
                        {k(`scope.${r.source.scope}`)} {r.source.country && `· ${r.source.country}`}
                      </span>
                    )}
                  </span>
                  <span className="flex items-center gap-3">
                    <Checkbox
                      label={t('search.selected')}
                      checked={r.selected}
                      disabled={!editable}
                      onChange={(e) => update(r.key, { selected: e.target.checked })}
                    />
                    {editable && (
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={t('common.remove')}
                        onClick={() => setRows((rs) => rs?.filter((x) => x.key !== r.key))}
                      >
                        <Trash2 aria-hidden />
                      </Button>
                    )}
                  </span>
                </div>
                <div className="grid gap-3 md:grid-cols-3">
                  <FormField label={t('search.reason')}>
                    <Textarea
                      rows={2}
                      disabled={!editable}
                      value={r.reason ?? ''}
                      onChange={(e) => update(r.key, { reason: e.target.value })}
                    />
                  </FormField>
                  <FormField label={t('search.availableInfo')}>
                    <Textarea
                      rows={2}
                      disabled={!editable}
                      value={r.availableInfo ?? ''}
                      onChange={(e) => update(r.key, { availableInfo: e.target.value })}
                    />
                  </FormField>
                  <FormField label={t('search.contact')}>
                    <Textarea
                      rows={2}
                      disabled={!editable}
                      value={r.contact ?? ''}
                      onChange={(e) => update(r.key, { contact: e.target.value })}
                    />
                  </FormField>
                </div>
                <Checkbox
                  label={t('search.codeOfConduct')}
                  checked={r.codeOfConductAck}
                  disabled={!editable}
                  onChange={(e) => update(r.key, { codeOfConductAck: e.target.checked })}
                />
                {r.source && s.can.outreach && (
                  <div className="flex flex-col gap-2 border-t pt-2" data-testid="bench-outreach">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="text-sm font-medium">{t('outreach.title')}</span>
                      <span className="flex items-center gap-2">
                        <Select
                          aria-label={t('outreach.rating')}
                          value={r.source.rating ? String(r.source.rating) : '-'}
                          onValueChange={(v) =>
                            r.source &&
                            rate.run({
                              id: s.id,
                              studyEntityId: r.source.id,
                              rating: v === '-' ? null : Number(v),
                              note: null,
                            })
                          }
                          options={[
                            { value: '-', label: t('outreach.rating') },
                            ...[1, 2, 3, 4, 5].map((n) => ({ value: String(n), label: '★'.repeat(n) })),
                          ]}
                        />
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => setOutreachFor(r.source)}
                          data-testid="bench-outreach-add"
                        >
                          {t('outreach.add')}
                        </Button>
                      </span>
                    </div>
                    {r.source.outreach.length > 0 && (
                      <ul className="text-sm">
                        {r.source.outreach.map((o) => (
                          <li key={o.id}>
                            {date(o.on)} · {k(`channel.${o.channel}`)} · {k(`response.${o.response}`)}
                            {o.summary && ` — ${o.summary}`}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </Section>
      {adding && (
        <AddCandidateDialog
          onClose={() => setAdding(false)}
          onAdd={(input, label) => {
            addRow(input, label);
            setAdding(false);
          }}
        />
      )}
      {outreachFor && (
        <OutreachDialog studyId={s.id} candidate={outreachFor} onClose={() => setOutreachFor(undefined)} />
      )}
    </div>
  );
}

function AddCandidateDialog({
  onClose,
  onAdd,
}: {
  onClose: () => void;
  onAdd: (input: BenchCandidateInput, label: string) => void;
}) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const names = useNames();
  const [mode, setMode] = useState<'register' | 'new'>('register');
  const [search, setSearch] = useState('');
  const [comparatorId, setComparatorId] = useState<string>();
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [country, setCountry] = useState('');
  const [scope, setScope] = useState('local');
  const register = useComparators({ search: search || undefined, pageSize: 20 });
  const chosen = register.data?.items.find((c) => c.id === comparatorId);
  const base = { selected: false, reason: null, availableInfo: null, contact: null, codeOfConductAck: false };
  return (
    <SimpleDialog
      title={t('search.addCandidate')}
      onClose={onClose}
      busy={false}
      disabled={mode === 'register' ? !comparatorId : !(nameAr || nameEn)}
      submitLabel={t('search.add')}
      testId="bench-candidate-add-confirm"
      onSubmit={() =>
        mode === 'register'
          ? chosen &&
            onAdd(
              { ...base, comparatorId: chosen.id, newComparator: null },
              names.text(chosen.values.nameAr, chosen.values.nameEn),
            )
          : onAdd(
              {
                ...base,
                comparatorId: null,
                newComparator: {
                  nameAr: nameAr || null,
                  nameEn: nameEn || null,
                  sectorCode: null,
                  country: country || null,
                  scope,
                  website: null,
                  contactName: null,
                  contactEmail: null,
                  contactPhone: null,
                  notes: null,
                  active: true,
                },
              },
              names.text(nameAr, nameEn),
            )
      }
    >
      <Select
        aria-label={t('search.addFrom')}
        value={mode}
        onValueChange={(v) => setMode(v as 'register' | 'new')}
        options={[
          { value: 'register', label: t('search.fromRegister') },
          { value: 'new', label: t('search.newEntity') },
        ]}
      />
      {mode === 'register' ? (
        <>
          <FormField label={t('common.search')}>
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              data-testid="bench-candidate-search"
            />
          </FormField>
          <FormField label={t('fields.comparator')}>
            <Select
              value={comparatorId}
              onValueChange={setComparatorId}
              options={(register.data?.items ?? []).map((c) => ({
                value: c.id,
                label: `${names.text(c.values.nameAr, c.values.nameEn)}${c.values.country ? ` · ${c.values.country}` : ''}`,
              }))}
            />
          </FormField>
        </>
      ) : (
        <>
          <FormField label={t('comparators.nameAr')}>
            <Input dir="rtl" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('comparators.nameEn')}>
            <Input dir="ltr" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          <FormField label={t('comparators.country')}>
            <Input value={country} onChange={(e) => setCountry(e.target.value)} />
          </FormField>
          <FormField label={t('comparators.scope')}>
            <Select
              value={scope}
              onValueChange={setScope}
              options={comparatorScopes.map((x) => ({ value: x, label: k(`scope.${x}`) }))}
            />
          </FormField>
        </>
      )}
    </SimpleDialog>
  );
}

function OutreachDialog({
  studyId,
  candidate,
  onClose,
}: {
  studyId: string;
  candidate: BenchCandidate;
  onClose: () => void;
}) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const [on, setOn] = useState(today());
  const [channel, setChannel] = useState('email');
  const [response, setResponse] = useState('pending');
  const [summary, setSummary] = useState('');
  const m = useBenchAction(benchApi.addOutreach, onClose);
  return (
    <SimpleDialog
      title={t('outreach.add')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.save')}
      testId="bench-outreach-save"
      onSubmit={() =>
        m.run({ id: studyId, studyEntityId: candidate.id, on, channel, response, summary: summary || null })
      }
    >
      <FormField label={t('outreach.on')}>
        <DatePicker value={on} onChange={(d) => d && setOn(d)} />
      </FormField>
      <FormField label={t('outreach.channel')}>
        <Select
          value={channel}
          onValueChange={setChannel}
          options={outreachChannels.map((c) => ({ value: c, label: k(`channel.${c}`) }))}
        />
      </FormField>
      <FormField label={t('outreach.response')}>
        <Select
          value={response}
          onValueChange={setResponse}
          options={outreachResponses.map((c) => ({ value: c, label: k(`response.${c}`) }))}
        />
      </FormField>
      <FormField label={t('outreach.summary')}>
        <Textarea value={summary} onChange={(e) => setSummary(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

/** Acquire stage (SPEC §8.3): visit info per selected entity — date, method, participants, questionnaire, notes. */
export function VisitsTab({ study: s }: { study: BenchStudy }) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const names = useNames();
  const visits = useStudyVisits(s.id);
  const candidates = useStudySearch(s.id);
  const [rows, setRows] = useState<(BenchVisitInput & { key: string })[]>();
  const save = useBenchAction(benchApi.saveVisits);
  const send = useBenchAction(benchApi.sendToAnalysis);
  const draft = useBenchAction(benchApi.aiQuestionnaire);
  const aiFeature = useAiFeature('bench-questionnaire');
  const aiAllowed = useCan('bench.ai.questionnaire');
  const [aiKeys, setAiKeys] = useState<string[]>([]);
  useEffect(() => {
    if (visits.data)
      setRows(
        visits.data.map((v) => ({
          key: v.id,
          id: v.id,
          comparatorId: v.comparatorId,
          agreedOn: v.agreedOn,
          method: v.method,
          participantIds: v.participants.map((p) => p.id),
          teamId: v.teamId,
          questionnaire: v.questionnaire,
          questionnaireSentOn: v.questionnaireSentOn,
          notes: v.notes,
        })),
      );
  }, [visits.data]);
  if (!rows || !candidates.data) return <Skeleton className="h-48" />;
  const selected = candidates.data.filter((c) => c.selected);
  const editable = s.can.editVisits;
  const update = (key: string, patch: Partial<BenchVisitInput>) =>
    setRows((rs) => rs?.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  return (
    <Section
      title={t('visits.title')}
      testId="bench-visits"
      actions={
        editable && (
          <>
            <Button
              variant="outline"
              size="sm"
              disabled={selected.length === 0}
              onClick={() =>
                setRows((rs) => [
                  ...(rs ?? []),
                  {
                    key: crypto.randomUUID(),
                    id: null,
                    comparatorId: selected[0]?.comparatorId ?? '',
                    agreedOn: null,
                    method: 'site-visit',
                    participantIds: [],
                    teamId: null,
                    questionnaire: null,
                    questionnaireSentOn: null,
                    notes: null,
                  },
                ])
              }
              data-testid="bench-visit-add"
            >
              <Plus aria-hidden />
              {t('visits.add')}
            </Button>
            <Button
              size="sm"
              loading={save.busy}
              onClick={() => save.run({ id: s.id, visits: rows.map(stripVisit) })}
              data-testid="bench-visits-save"
            >
              {t('common.save')}
            </Button>
            {s.can.sendToAnalysis && (
              <Button
                size="sm"
                loading={send.busy}
                onClick={() => send.run(s.id)}
                data-testid="bench-send-to-analysis"
              >
                {t('visits.sendToAnalysis')}
              </Button>
            )}
          </>
        )
      }
    >
      <p className="text-sm text-muted-foreground">{t('visits.hint')}</p>
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('visits.empty')}</p>
      ) : (
        rows.map((v) => (
          <div
            key={v.key}
            className="grid gap-3 rounded-md border p-3 md:grid-cols-3"
            data-testid="bench-visit"
          >
            <FormField label={t('fields.comparator')}>
              <Select
                disabled={!editable}
                value={v.comparatorId}
                onValueChange={(comparatorId) => update(v.key, { comparatorId })}
                options={selected.map((c) => ({
                  value: c.comparatorId,
                  label: names.text(c.nameAr, c.nameEn),
                }))}
              />
            </FormField>
            <FormField label={t('visits.agreedOn')}>
              <DatePicker
                disabled={!editable}
                value={v.agreedOn ?? undefined}
                onChange={(d) => update(v.key, { agreedOn: d ?? null })}
              />
            </FormField>
            <FormField label={t('visits.method')}>
              <Select
                disabled={!editable}
                value={v.method}
                onValueChange={(method) => update(v.key, { method })}
                options={visitMethods.map((m) => ({ value: m, label: k(`method.${m}`) }))}
              />
            </FormField>
            <div className="md:col-span-3">
              <FormField label={t('visits.participants')}>
                <EmployeePicker
                  value={undefined}
                  onChange={(e) =>
                    e &&
                    !(v.participantIds ?? []).includes(e) &&
                    update(v.key, { participantIds: [...(v.participantIds ?? []), e] })
                  }
                />
              </FormField>
              <ParticipantChips
                ids={v.participantIds ?? []}
                known={visits.data?.flatMap((x) => x.participants) ?? []}
                onRemove={
                  editable
                    ? (id) =>
                        update(v.key, { participantIds: (v.participantIds ?? []).filter((x) => x !== id) })
                    : undefined
                }
              />
            </div>
            <div className="md:col-span-3">
              <FormField
                label={
                  <span className="flex items-center gap-2">
                    {t('visits.questionnaire')}
                    {aiKeys.includes(v.key) && <AiBadge />}
                  </span>
                }
              >
                <Textarea
                  rows={4}
                  disabled={!editable}
                  value={v.questionnaire ?? ''}
                  onChange={(e) => update(v.key, { questionnaire: e.target.value })}
                />
              </FormField>
              {editable && aiFeature && aiAllowed && (
                <Button
                  variant="outline"
                  size="sm"
                  className="mt-2"
                  loading={draft.busy}
                  onClick={() =>
                    draft.run({ id: s.id, comparatorId: v.comparatorId || null }, (q) => {
                      setAiKeys((x) => [...x, v.key]);
                      update(v.key, { questionnaire: names.text(q.questionsAr, q.questionsEn) });
                    })
                  }
                  data-testid="bench-ai-questionnaire"
                >
                  <Sparkles aria-hidden />
                  {t('ai.questionnaire')}
                </Button>
              )}
            </div>
            <FormField label={t('visits.questionnaireSentOn')}>
              <DatePicker
                disabled={!editable}
                value={v.questionnaireSentOn ?? undefined}
                onChange={(d) => update(v.key, { questionnaireSentOn: d ?? null })}
              />
            </FormField>
            <div className="md:col-span-2">
              <FormField label={t('visits.notes')}>
                <Textarea
                  rows={3}
                  disabled={!editable}
                  value={v.notes ?? ''}
                  onChange={(e) => update(v.key, { notes: e.target.value })}
                />
              </FormField>
            </div>
          </div>
        ))
      )}
    </Section>
  );
}

function ParticipantChips({
  ids,
  known,
  onRemove,
}: {
  ids: readonly string[];
  known: readonly { id: string; nameAr: string | null; nameEn: string | null }[];
  onRemove?: (id: string) => void;
}) {
  const { t } = useTranslation('bench');
  const names = useNames();
  if (ids.length === 0) return null;
  return (
    <span className="mt-2 flex flex-wrap gap-1">
      {ids.map((id) => {
        const person = known.find((p) => p.id === id);
        return (
          <StatusChip key={id} tone="neutral">
            {person ? names.person(person) : t('visits.newParticipant')}
            {onRemove && (
              <button
                type="button"
                className="ms-1"
                aria-label={t('common.remove')}
                onClick={() => onRemove(id)}
              >
                ×
              </button>
            )}
          </StatusChip>
        );
      })}
    </span>
  );
}

/** Analysis stage (SPEC §8.4): practices compared, indicators (KPI links), development plan → PMO approval creates CAP remarks. */
export function AnalysisTab({ study: s }: { study: BenchStudy }) {
  const { t } = useTranslation('bench');
  const { k } = useBenchT();
  const { t: tcap } = useTranslation('cap');
  const analysis = useStudyAnalysis(s.id);
  const kpis = useKpiCards({ pageSize: 200 });
  const names = useNames();
  const [v, setV] = useState<BenchAnalysisInput>();
  const [notes, setNotes] = useState('');
  const [aiUsed, setAiUsed] = useState(false);
  const save = useBenchAction(benchApi.saveAnalysis);
  const submit = useBenchAction(benchApi.submitAnalysis);
  const ai = useBenchAction(benchApi.aiAnalysis);
  const aiFeature = useAiFeature('bench-analysis');
  const aiAllowed = useCan('bench.ai.analysis');
  useEffect(() => {
    if (analysis.data)
      setV({
        practices: analysis.data.practices.map((p) => ({ ...p })),
        indicators: analysis.data.indicators.map((i) => ({ ...i })),
        planItems: analysis.data.planItems.map((p) => ({
          id: p.id,
          topicId: p.topicId,
          titleAr: p.titleAr,
          titleEn: p.titleEn,
          description: p.description,
          ownerEmployeeId: p.owner?.id ?? null,
          unitId: p.unit?.id ?? null,
          dueOn: p.dueOn,
          priority: p.priority,
          actionTypeCode: p.actionTypeCode,
          budgetRequired: p.budgetRequired,
        })),
      });
  }, [analysis.data]);
  if (!v || !analysis.data) return <Skeleton className="h-48" />;
  const editable = s.can.editAnalysis;
  const topicOptions = [
    { value: '-', label: '—' },
    ...s.topics.map((x) => ({ value: x.id, label: x.title })),
  ];
  const setRow = <K extends keyof BenchAnalysisInput>(
    key: K,
    index: number,
    patch: Partial<BenchAnalysisInput[K][number]>,
  ) => setV((x) => (x ? { ...x, [key]: x[key].map((r, i) => (i === index ? { ...r, ...patch } : r)) } : x));
  const removeRow = (key: keyof BenchAnalysisInput, index: number) =>
    setV((x) => (x ? { ...x, [key]: x[key].filter((_, i) => i !== index) } : x));
  const linked = new Map(analysis.data.planItems.map((p) => [p.id, p.remarkId]));
  const del = (key: keyof BenchAnalysisInput, i: number) =>
    editable && (
      <Button variant="ghost" size="sm" aria-label={t('common.remove')} onClick={() => removeRow(key, i)}>
        <Trash2 aria-hidden />
      </Button>
    );

  return (
    <div className="flex flex-col gap-4" data-testid="bench-analysis">
      {editable && (
        <div className="flex flex-wrap justify-end gap-2">
          <Button
            loading={save.busy}
            onClick={() => save.run({ id: s.id, analysis: v })}
            data-testid="bench-analysis-save"
          >
            {t('common.save')}
          </Button>
          {s.can.submitAnalysis && (
            <Button
              loading={submit.busy}
              onClick={() => submit.run({ id: s.id, comment: null })}
              data-testid="bench-analysis-submit"
            >
              {t('analysis.submit')}
            </Button>
          )}
        </div>
      )}
      {editable && aiFeature && aiAllowed && (
        <Section
          title={
            <span className="flex items-center gap-2">
              {t('ai.analysis')} {aiUsed && <AiBadge />}
            </span>
          }
        >
          <FormField label={t('ai.visitNotes')} hint={t('ai.analysisHint')}>
            <Textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </FormField>
          <div>
            <Button
              variant="outline"
              size="sm"
              loading={ai.busy}
              onClick={() =>
                ai.run({ id: s.id, notes: notes || null }, (d) => {
                  setAiUsed(true);
                  setV((x) =>
                    x
                      ? {
                          ...x,
                          practices: [
                            ...x.practices,
                            ...d.practices.map((p) => ({
                              id: null,
                              topicId: p.topicId,
                              subject: p.subject,
                              ourPractice: p.ourPractice,
                              theirPractice: p.theirPractice,
                              category: p.category,
                              applicable: true,
                              addToLibrary: false,
                            })),
                          ],
                          planItems: [
                            ...x.planItems,
                            ...d.actions.map((a) => ({
                              id: null,
                              topicId: null,
                              titleAr: a.titleAr,
                              titleEn: a.titleEn,
                              description: null,
                              ownerEmployeeId: null,
                              unitId: s.responsibleUnit?.id ?? null,
                              dueOn: null,
                              priority: a.priority,
                              actionTypeCode: null,
                              budgetRequired: false,
                            })),
                          ],
                        }
                      : x,
                  );
                })
              }
              data-testid="bench-ai-analysis"
            >
              <Sparkles aria-hidden />
              {t('ai.analysis')}
            </Button>
          </div>
        </Section>
      )}
      <Section
        title={t('analysis.practices')}
        testId="bench-practices"
        actions={
          editable && (
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                setV({
                  ...v,
                  practices: [
                    ...v.practices,
                    {
                      id: null,
                      topicId: null,
                      subject: '',
                      ourPractice: '',
                      theirPractice: '',
                      category: 'compared',
                      applicable: true,
                      addToLibrary: false,
                    },
                  ],
                })
              }
              data-testid="bench-practice-add"
            >
              <Plus aria-hidden />
              {t('analysis.addPractice')}
            </Button>
          )
        }
      >
        {v.practices.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('analysis.noPractices')}</p>
        ) : (
          v.practices.map((p, i) => (
            <div
              key={p.id ?? `new-${i}`}
              className="grid gap-2 rounded-md border p-3 md:grid-cols-4"
              data-testid="bench-practice-row"
            >
              <FormField label={t('analysis.subject')}>
                <Input
                  disabled={!editable}
                  value={p.subject ?? ''}
                  onChange={(e) => setRow('practices', i, { subject: e.target.value })}
                />
              </FormField>
              <FormField label={t('fields.topic')}>
                <Select
                  disabled={!editable}
                  value={p.topicId ?? '-'}
                  onValueChange={(x) => setRow('practices', i, { topicId: x === '-' ? null : x })}
                  options={topicOptions}
                />
              </FormField>
              <FormField label={t('analysis.category')}>
                <Select
                  disabled={!editable}
                  value={p.category}
                  onValueChange={(category) => setRow('practices', i, { category })}
                  options={practiceCategories.map((c) => ({ value: c, label: k(`category.${c}`) }))}
                />
              </FormField>
              <div className="flex items-end justify-end">{del('practices', i)}</div>
              <div className="md:col-span-2">
                <FormField label={t('analysis.ourPractice')}>
                  <Textarea
                    rows={2}
                    disabled={!editable}
                    value={p.ourPractice ?? ''}
                    onChange={(e) => setRow('practices', i, { ourPractice: e.target.value })}
                  />
                </FormField>
              </div>
              <div className="md:col-span-2">
                <FormField label={t('analysis.theirPractice')}>
                  <Textarea
                    rows={2}
                    disabled={!editable}
                    value={p.theirPractice ?? ''}
                    onChange={(e) => setRow('practices', i, { theirPractice: e.target.value })}
                  />
                </FormField>
              </div>
              <Checkbox
                label={t('analysis.applicable')}
                checked={p.applicable}
                disabled={!editable}
                onChange={(e) => setRow('practices', i, { applicable: e.target.checked })}
              />
              <Checkbox
                label={t('analysis.addToLibrary')}
                checked={p.addToLibrary}
                disabled={!editable}
                onChange={(e) => setRow('practices', i, { addToLibrary: e.target.checked })}
              />
            </div>
          ))
        )}
      </Section>
      <Section
        title={t('analysis.indicators')}
        testId="bench-indicators"
        actions={
          editable && (
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                setV({
                  ...v,
                  indicators: [
                    ...v.indicators,
                    {
                      id: null,
                      kpiCardId: null,
                      indicator: '',
                      ourValue: null,
                      theirValue: null,
                      period: '',
                      source: '',
                      updateKpiBenchmark: false,
                    },
                  ],
                })
              }
              data-testid="bench-indicator-add"
            >
              <Plus aria-hidden />
              {t('analysis.addIndicator')}
            </Button>
          )
        }
      >
        {v.indicators.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('analysis.noIndicators')}</p>
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('analysis.indicator')}</TableHeaderCell>
                <TableHeaderCell>{t('analysis.kpi')}</TableHeaderCell>
                <TableHeaderCell>{t('analysis.ourValue')}</TableHeaderCell>
                <TableHeaderCell>{t('analysis.theirValue')}</TableHeaderCell>
                <TableHeaderCell>{t('analysis.period')}</TableHeaderCell>
                <TableHeaderCell>{t('analysis.updateKpi')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{t('common.actions')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {v.indicators.map((x, i) => (
                <TableRow key={x.id ?? `new-${i}`} data-testid="bench-indicator-row">
                  <TableCell>
                    <Input
                      aria-label={t('analysis.indicator')}
                      disabled={!editable}
                      value={x.indicator ?? ''}
                      onChange={(e) => setRow('indicators', i, { indicator: e.target.value })}
                    />
                  </TableCell>
                  <TableCell className="min-w-48">
                    <Select
                      aria-label={t('analysis.kpi')}
                      disabled={!editable}
                      value={x.kpiCardId ?? '-'}
                      onValueChange={(id) => setRow('indicators', i, { kpiCardId: id === '-' ? null : id })}
                      options={[
                        { value: '-', label: '—' },
                        ...(kpis.data?.items ?? []).map((c) => ({
                          value: c.id,
                          label: `${c.code} ${names.text(c.nameAr, c.nameEn)}`.trim(),
                        })),
                      ]}
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      aria-label={t('analysis.ourValue')}
                      type="number"
                      disabled={!editable}
                      value={x.ourValue ?? ''}
                      onChange={(e) =>
                        setRow('indicators', i, {
                          ourValue: e.target.value === '' ? null : Number(e.target.value),
                        })
                      }
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      aria-label={t('analysis.theirValue')}
                      type="number"
                      disabled={!editable}
                      value={x.theirValue ?? ''}
                      onChange={(e) =>
                        setRow('indicators', i, {
                          theirValue: e.target.value === '' ? null : Number(e.target.value),
                        })
                      }
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      aria-label={t('analysis.period')}
                      disabled={!editable}
                      value={x.period ?? ''}
                      onChange={(e) => setRow('indicators', i, { period: e.target.value })}
                    />
                  </TableCell>
                  <TableCell>
                    <Checkbox
                      label={t('analysis.updateKpi')}
                      className="[&_label]:sr-only"
                      checked={x.updateKpiBenchmark}
                      disabled={!editable || !x.kpiCardId}
                      onChange={(e) => setRow('indicators', i, { updateKpiBenchmark: e.target.checked })}
                    />
                  </TableCell>
                  <TableCell>{del('indicators', i)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
      <Section
        title={t('analysis.plan')}
        testId="bench-development-plan"
        actions={
          editable && (
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                setV({
                  ...v,
                  planItems: [
                    ...v.planItems,
                    {
                      id: null,
                      topicId: null,
                      titleAr: '',
                      titleEn: '',
                      description: '',
                      ownerEmployeeId: null,
                      unitId: s.responsibleUnit?.id ?? null,
                      dueOn: null,
                      priority: 'medium',
                      actionTypeCode: null,
                      budgetRequired: false,
                    },
                  ],
                })
              }
              data-testid="bench-plan-item-add"
            >
              <Plus aria-hidden />
              {t('analysis.addPlanItem')}
            </Button>
          )
        }
      >
        <p className="text-sm text-muted-foreground">{t('analysis.planHint')}</p>
        {v.planItems.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('analysis.noPlanItems')}</p>
        ) : (
          v.planItems.map((p, i) => (
            <div
              key={p.id ?? `new-${i}`}
              className="grid gap-2 rounded-md border p-3 md:grid-cols-4"
              data-testid="bench-plan-item"
            >
              <div className="md:col-span-2">
                <FormField label={t('analysis.itemAr')}>
                  <Input
                    dir="rtl"
                    disabled={!editable}
                    value={p.titleAr ?? ''}
                    onChange={(e) => setRow('planItems', i, { titleAr: e.target.value })}
                  />
                </FormField>
              </div>
              <div className="md:col-span-2">
                <FormField label={t('analysis.itemEn')}>
                  <Input
                    dir="ltr"
                    disabled={!editable}
                    value={p.titleEn ?? ''}
                    onChange={(e) => setRow('planItems', i, { titleEn: e.target.value })}
                  />
                </FormField>
              </div>
              <FormField label={t('fields.unit')}>
                <UnitSelect
                  value={p.unitId ?? undefined}
                  onChange={(u) => setRow('planItems', i, { unitId: u ?? null })}
                />
              </FormField>
              <FormField label={t('analysis.owner')}>
                <EmployeePicker
                  value={p.ownerEmployeeId ?? undefined}
                  onChange={(e) => setRow('planItems', i, { ownerEmployeeId: e ?? null })}
                />
              </FormField>
              <FormField label={t('analysis.dueOn')}>
                <DatePicker
                  disabled={!editable}
                  value={p.dueOn ?? undefined}
                  onChange={(d) => setRow('planItems', i, { dueOn: d ?? null })}
                />
              </FormField>
              <FormField label={t('analysis.priority')}>
                <Select
                  disabled={!editable}
                  value={p.priority ?? undefined}
                  onValueChange={(priority) => setRow('planItems', i, { priority })}
                  options={priorities.map((x) => ({ value: x, label: tcap(`priority.${x}`) }))}
                />
              </FormField>
              <Checkbox
                label={t('analysis.budgetRequired')}
                checked={p.budgetRequired}
                disabled={!editable}
                onChange={(e) => setRow('planItems', i, { budgetRequired: e.target.checked })}
              />
              <div className="flex items-center justify-end gap-2 md:col-span-3">
                {p.id && linked.get(p.id) && (
                  <StatusChip tone="success">{t('analysis.remarkCreated')}</StatusChip>
                )}
                {del('planItems', i)}
              </div>
            </div>
          ))
        )}
      </Section>
    </div>
  );
}
