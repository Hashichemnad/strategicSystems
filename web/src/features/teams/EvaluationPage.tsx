import { getRouteApi, Link } from '@tanstack/react-router';
import { AlertTriangle, ArrowLeft, Award, Download, Sparkles } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { contentUrl } from '@/features/files/api';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { ApprovalPanel } from '@/features/workflow/ApprovalPanel';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
  Wizard,
} from '@/ui';
import { teamApi, useEvaluation, useTeamMutation, type Evaluation, type EvaluationAssistant } from './api';

const route = getRouteApi('/app/teams/evaluations/$evaluationId');

interface CriterionState {
  level: string | null;
  comment: string;
  justification: string;
}

/**
 * Evaluation of one cycle (SPEC §4.5 T3–T4). The evaluator's wizard shows each criterion's data-based suggestion; HR reviews with the
 * approval panel and the previous cycles; HR then adjusts and submits the rewards; certificates follow the reward approval.
 */
export function EvaluationPage() {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const { evaluationId } = route.useParams();
  const evaluation = useEvaluation(evaluationId);
  const [tab, setTab] = useState('evaluation');
  if (evaluation.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (evaluation.isError)
    return (
      <StatusBanner
        tone="info"
        title={t('evaluations.hidden')}
        description={errorMessage(tc, evaluation.error)}
      />
    );
  const e = evaluation.data;
  const cycle = e.summary.cycle === 'completion' ? t('evaluations.completion') : e.summary.cycle;
  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/teams/$teamId"
        params={{ teamId: e.summary.teamId }}
        search={{ tab: 'evaluations' }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {pickText(i18n.language, e.teamNameAr, e.teamNameEn)}
      </Link>
      <PageHeader
        title={t('evaluation.title', { cycle })}
        description={`${e.summary.number ?? ''} · ${formatDate(e.summary.periodStart, i18n.language)} – ${formatDate(e.summary.periodEnd, i18n.language)}`}
        meta={
          <>
            <StatusChip tone={e.summary.status === 'approved' ? 'success' : 'info'}>
              {t(`evaluations.statuses.${e.summary.status as 'open'}`)}
            </StatusChip>
            {e.summary.score !== null && (
              <StatusChip tone="neutral">
                {t('evaluations.score')}: {formatNumber(e.summary.score, i18n.language)}
              </StatusChip>
            )}
            {e.summary.rewardStatus && e.summary.rewardStatus !== 'none' && (
              <StatusChip tone="neutral">
                {t(`rewards.statuses.${e.summary.rewardStatus as 'draft'}`)}
              </StatusChip>
            )}
          </>
        }
      />
      {e.returnComment && e.summary.status === 'returned' && (
        <StatusBanner tone="warning" title={t('evaluation.returnComment', { comment: e.returnComment })} />
      )}
      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="evaluation">{t('evaluation.steps.criteria')}</TabsTrigger>
          <TabsTrigger value="review">{t('evaluation.review')}</TabsTrigger>
          {e.summary.rewardStatus !== 'none' && (
            <TabsTrigger value="rewards">{t('rewards.title')}</TabsTrigger>
          )}
        </TabsList>
        <TabsContent value="evaluation">
          {e.permissions.canEvaluate ? (
            <EvaluationWizard evaluation={e} />
          ) : (
            <EvaluationSummary evaluation={e} />
          )}
        </TabsContent>
        <TabsContent value="review">
          <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
            <div className="flex flex-col gap-4">
              <EvaluationSummary evaluation={e} />
              <ApprovalPanel resourceType="teams.team" resourceId={e.summary.teamId} />
            </div>
            <PreviousCycles evaluation={e} />
          </div>
        </TabsContent>
        <TabsContent value="rewards">
          <RewardsPanel evaluation={e} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function useLevelName() {
  const { i18n } = useTranslation();
  return (e: Evaluation, criterion: string, level: string | null | undefined) => {
    const c = e.template.criteria.find((x) => x.code === criterion);
    const l = c?.levels.find((x) => x.code === level);
    return l ? pickText(i18n.language, l.nameAr, l.nameEn) : '—';
  };
}

function BasisText({ evaluation, code }: { evaluation: Evaluation; code: string }) {
  const { t, i18n } = useTranslation('teams');
  const levelName = useLevelName();
  const result = evaluation.criteria.find((c) => c.code === code);
  if (!result?.basis) return <p className="text-xs text-muted-foreground">{t('evaluation.noData')}</p>;
  const b = result.basis;
  return (
    <p
      className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"
      data-testid={`suggestion-${code}`}
    >
      <StatusChip tone="info">
        {t('evaluation.suggested', { level: levelName(evaluation, code, result.suggestedLevel) })}
      </StatusChip>
      {t(`evaluation.basis.${b.source as 'minutes-delay'}`, {
        count: b.count,
        total: b.total,
        value: formatNumber(b.value, i18n.language),
      })}
    </p>
  );
}

function EvaluationWizard({ evaluation: e }: { evaluation: Evaluation }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const aiEvaluation = useAiFeature('teams-evaluation');
  const [criteria, setCriteria] = useState<Record<string, CriterionState>>({});
  const [answers, setAnswers] = useState<Record<string, Record<string, number>>>({});
  const [comments, setComments] = useState<Record<string, string>>({});
  const [workload, setWorkload] = useState(e.workload);
  const [assistant, setAssistant] = useState<EvaluationAssistant>();
  const [error, setError] = useState<string>();
  useEffect(() => {
    setCriteria(
      Object.fromEntries(
        e.criteria.map((c) => [
          c.code,
          {
            level: c.level ?? c.suggestedLevel ?? null,
            comment: c.comment ?? '',
            justification: c.overrideJustification ?? '',
          },
        ]),
      ),
    );
    setAnswers(Object.fromEntries(e.members.map((m) => [m.id, { ...m.answers }])));
    setComments(Object.fromEntries(e.members.map((m) => [m.id, m.comment ?? ''])));
    setWorkload(e.workload);
  }, [e]);
  const score = useMemo(
    () =>
      e.template.criteria.reduce(
        (sum, c) => sum + (c.levels.find((l) => l.code === criteria[c.code]?.level)?.score ?? 0),
        0,
      ),
    [criteria, e.template.criteria],
  );
  const save = useTeamMutation(async () =>
    teamApi.saveEvaluation({
      id: e.summary.id,
      criteria: e.permissions.canSubmit
        ? Object.entries(criteria).map(([code, c]) => ({
            code,
            level: c.level,
            comment: c.comment || null,
            overrideJustification: c.justification || null,
          }))
        : null,
      workload: e.permissions.canSubmit ? workload : null,
      evidenceFileIds: null,
      members: e.members
        .filter((m) => m.canEvaluate)
        .map((m) => ({ id: m.id, answers: answers[m.id] ?? {}, comment: comments[m.id] || null })),
      version: e.version,
    }),
  );
  const submit = useTeamMutation(async () => {
    const saved = await save.mutateAsync();
    return teamApi.submitEvaluation({ id: saved.summary.id, version: saved.version, comment: null });
  });
  const summarise = useTeamMutation(teamApi.aiEvaluation);
  const evidenceRequired = score >= e.template.evidenceThreshold;

  const criteriaStep = (
    <div className="flex flex-col gap-4" data-testid="evaluation-criteria">
      {e.template.criteria.map((c) => {
        const state = criteria[c.code] ?? { level: null, comment: '', justification: '' };
        const suggestion = e.criteria.find((x) => x.code === c.code)?.suggestedLevel;
        const overridden = !!suggestion && state.level !== null && state.level !== suggestion;
        return (
          <fieldset key={c.code} className="flex flex-col gap-2 rounded-md border p-3">
            <legend className="px-1 font-medium">
              {pickText(i18n.language, c.nameAr, c.nameEn)}{' '}
              <span className="text-xs text-muted-foreground">
                ({t('settings.weight')} {c.weight})
              </span>
            </legend>
            {c.dataSource && <BasisText evaluation={e} code={c.code} />}
            <div
              className="flex flex-wrap gap-2"
              role="radiogroup"
              aria-label={pickText(i18n.language, c.nameAr, c.nameEn)}
            >
              {c.levels.map((l) => (
                <button
                  key={l.code}
                  type="button"
                  role="radio"
                  aria-checked={state.level === l.code}
                  onClick={() => setCriteria((x) => ({ ...x, [c.code]: { ...state, level: l.code } }))}
                  className={cn(
                    'rounded-md border px-3 py-2 text-start text-sm',
                    state.level === l.code ? 'border-primary bg-primary/10 font-medium' : 'hover:bg-muted',
                    suggestion === l.code && 'ring-1 ring-info',
                  )}
                >
                  {pickText(i18n.language, l.nameAr, l.nameEn)} ·{' '}
                  <span className="tabular-nums">{formatNumber(l.score, i18n.language)}</span>
                </button>
              ))}
            </div>
            {overridden && (
              <FormField label={t('evaluation.justification')} required>
                <Textarea
                  rows={2}
                  value={state.justification}
                  onChange={(ev) =>
                    setCriteria((x) => ({ ...x, [c.code]: { ...state, justification: ev.target.value } }))
                  }
                />
              </FormField>
            )}
            <FormField label={t('evaluation.comment')}>
              <Input
                value={state.comment}
                onChange={(ev) =>
                  setCriteria((x) => ({ ...x, [c.code]: { ...state, comment: ev.target.value } }))
                }
              />
            </FormField>
          </fieldset>
        );
      })}
      <FormField label={t('evaluation.workload')}>
        <Select
          value={workload}
          onValueChange={setWorkload}
          options={(['high', 'medium', 'low'] as const).map((w) => ({ value: w, label: t(`workload.${w}`) }))}
        />
      </FormField>
      <p className="text-lg font-semibold tabular-nums">
        {t('evaluations.score')}: {formatNumber(score, i18n.language)} / 100
      </p>
    </div>
  );

  const membersStep = (
    <div className="flex flex-col gap-3" data-testid="evaluation-members">
      {e.members.map((m) => {
        const points = e.template.questions.reduce((sum, q) => sum + (answers[m.id]?.[q.code] ?? 0), 0);
        return (
          <fieldset
            key={m.id}
            className="flex flex-col gap-2 rounded-md border p-3"
            disabled={!m.canEvaluate}
          >
            <legend className="px-1 font-medium">
              {m.employee ? pickText(i18n.language, m.employee.nameAr, m.employee.nameEn) : m.externalName} ·{' '}
              <span className="text-sm text-muted-foreground">
                {pickText(i18n.language, m.roleNameAr, m.roleNameEn)}
              </span>
            </legend>
            <p className="text-xs text-muted-foreground">
              {m.attendanceRate === null
                ? ''
                : t('evaluation.attendance', {
                    rate: `${formatNumber(m.attendanceRate * 100, i18n.language)} %`,
                  })}
              {!m.canEvaluate && ` · ${t('evaluation.leaderEvaluatedBy')}`}
            </p>
            {e.template.questions.map((q) => (
              <div key={q.code} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                <span>{pickText(i18n.language, q.textAr, q.textEn)}</span>
                <div
                  className="flex gap-1"
                  role="radiogroup"
                  aria-label={pickText(i18n.language, q.textAr, q.textEn)}
                >
                  {[1, 2, 3, 4, 5].map((n) => (
                    <button
                      key={n}
                      type="button"
                      role="radio"
                      aria-checked={answers[m.id]?.[q.code] === n}
                      onClick={() =>
                        setAnswers((x) => ({ ...x, [m.id]: { ...(x[m.id] ?? {}), [q.code]: n } }))
                      }
                      className={cn(
                        'size-8 rounded-md border tabular-nums',
                        answers[m.id]?.[q.code] === n
                          ? 'border-primary bg-primary text-primary-foreground'
                          : 'hover:bg-muted',
                      )}
                    >
                      {formatNumber(n, i18n.language)}
                    </button>
                  ))}
                </div>
              </div>
            ))}
            <div className="flex items-center justify-between gap-2">
              <Input
                className="max-w-lg"
                placeholder={t('evaluation.comment')}
                aria-label={t('evaluation.comment')}
                value={comments[m.id] ?? ''}
                onChange={(ev) => setComments((x) => ({ ...x, [m.id]: ev.target.value }))}
              />
              <span className="text-sm tabular-nums">
                {t('evaluation.points', { points: formatNumber(points, i18n.language) })}
              </span>
            </div>
          </fieldset>
        );
      })}
    </div>
  );

  const evidenceStep = (
    <div className="flex flex-col gap-3">
      <StatusBanner
        tone={evidenceRequired ? 'warning' : 'info'}
        title={
          evidenceRequired
            ? t('evaluation.evidenceRequired', {
                score: formatNumber(score, i18n.language),
                threshold: e.template.evidenceThreshold,
              })
            : t('evaluation.evidenceOptional', { threshold: e.template.evidenceThreshold })
        }
      />
      <DocumentsPanel ownerType="teams.team_evaluation" ownerId={e.summary.id} canEdit />
    </div>
  );

  const reviewStep = (
    <div className="flex flex-col gap-3">
      <Flags evaluation={e} />
      {aiEvaluation && (
        <div className="flex flex-col gap-2">
          <div>
            <Button
              variant="outline"
              loading={summarise.isPending}
              onClick={() =>
                summarise.mutate(e.summary.id, {
                  onSuccess: setAssistant,
                  onError: (f) => setError(errorMessage(tc, f)),
                })
              }
            >
              <Sparkles aria-hidden />
              {t('evaluation.assistant')}
            </Button>
          </div>
          {assistant && (
            <div className="rounded-md bg-muted p-3 text-sm" data-testid="evaluation-assistant">
              <StatusChip tone="info">{t('evaluation.assistantLabel')}</StatusChip>
              <p className="mt-2">{assistant.summary}</p>
              <ul className="mt-2 list-disc ps-5">
                {assistant.points.map((p, i) => (
                  <li key={i}>{p}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
      <p className="text-lg font-semibold tabular-nums">
        {t('evaluations.score')}: {formatNumber(score, i18n.language)} / 100
      </p>
    </div>
  );

  return (
    <div className="flex flex-col gap-4">
      {error && <StatusBanner tone="danger" title={error} />}
      <Wizard
        steps={[
          { key: 'criteria', title: t('evaluation.steps.criteria'), content: criteriaStep },
          { key: 'members', title: t('evaluation.steps.members'), content: membersStep },
          { key: 'evidence', title: t('evaluation.steps.evidence'), content: evidenceStep },
          { key: 'review', title: t('evaluation.steps.review'), content: reviewStep },
        ]}
        onSaveDraft={() =>
          save.mutate(undefined, {
            onSuccess: () => toast({ title: t('evaluation.saved') }),
            onError: (f) => setError(errorMessage(tc, f)),
          })
        }
        onFinish={() =>
          e.permissions.canSubmit
            ? submit.mutate(undefined, {
                onSuccess: () => toast({ title: t('evaluation.submitted') }),
                onError: (f) => setError(errorMessage(tc, f)),
              })
            : save.mutate(undefined, {
                onSuccess: () => toast({ title: t('evaluation.saved') }),
                onError: (f) => setError(errorMessage(tc, f)),
              })
        }
        finishLabel={e.permissions.canSubmit ? t('evaluation.submit') : t('evaluation.save')}
        busy={save.isPending || submit.isPending}
      />
    </div>
  );
}

function Flags({ evaluation: e }: { evaluation: Evaluation }) {
  const { t } = useTranslation('teams');
  if (e.flags.length === 0) return null;
  return (
    <section
      className="rounded-md border border-warning/50 bg-warning/10 p-3 text-sm"
      data-testid="evaluation-flags"
    >
      <p className="mb-1 flex items-center gap-1 font-medium">
        <AlertTriangle className="size-4" aria-hidden />
        {t('evaluation.flagsTitle')}
      </p>
      <ul className="list-disc ps-5">
        {e.flags.map((f, i) => (
          <li key={i}>{t(`evaluation.flags.${f.code as 'all-equal'}`)}</li>
        ))}
      </ul>
    </section>
  );
}

function EvaluationSummary({ evaluation: e }: { evaluation: Evaluation }) {
  const { t, i18n } = useTranslation('teams');
  const levelName = useLevelName();
  return (
    <div className="flex flex-col gap-4" data-testid="evaluation-summary">
      <Flags evaluation={e} />
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('evaluation.steps.criteria')}</TableHeaderCell>
            <TableHeaderCell>{t('evaluation.suggested', { level: '' })}</TableHeaderCell>
            <TableHeaderCell>{t('evaluations.score')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {e.template.criteria.map((c) => {
            const r = e.criteria.find((x) => x.code === c.code);
            return (
              <TableRow key={c.code}>
                <TableCell>
                  {pickText(i18n.language, c.nameAr, c.nameEn)}
                  {r?.overrideJustification && (
                    <p className="text-xs text-muted-foreground">{r.overrideJustification}</p>
                  )}
                </TableCell>
                <TableCell>{c.dataSource ? <BasisText evaluation={e} code={c.code} /> : '—'}</TableCell>
                <TableCell>
                  {levelName(e, c.code, r?.level)} ·{' '}
                  <span className="tabular-nums">
                    {r?.score === null || r?.score === undefined ? '—' : formatNumber(r.score, i18n.language)}
                  </span>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('card.member')}</TableHeaderCell>
            <TableHeaderCell>{t('form.role')}</TableHeaderCell>
            <TableHeaderCell>{t('evaluation.points', { points: '' })}</TableHeaderCell>
            <TableHeaderCell>{t('employeeTab.grade')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {e.members.map((m) => {
            const band = e.template.gradeBands.find((b) => b.code === m.grade);
            return (
              <TableRow key={m.id}>
                <TableCell>
                  {m.employee
                    ? pickText(i18n.language, m.employee.nameAr, m.employee.nameEn)
                    : m.externalName}
                </TableCell>
                <TableCell>{pickText(i18n.language, m.roleNameAr, m.roleNameEn)}</TableCell>
                <TableCell className="tabular-nums">
                  {m.points === null ? '—' : formatNumber(m.points, i18n.language)}
                </TableCell>
                <TableCell>{band ? pickText(i18n.language, band.nameAr, band.nameEn) : '—'}</TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}

function PreviousCycles({ evaluation: e }: { evaluation: Evaluation }) {
  const { t, i18n } = useTranslation('teams');
  return (
    <section className="rounded-lg border bg-card p-4" data-testid="previous-cycles">
      <h3 className="mb-2 font-semibold">{t('evaluation.previous')}</h3>
      {e.previousCycles.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('evaluations.empty')}</p>
      ) : (
        <ul className="flex flex-col gap-2 text-sm">
          {e.previousCycles.map((p) => (
            <li key={p.id} className="flex justify-between">
              <span>{p.cycle === 'completion' ? t('evaluations.completion') : p.cycle}</span>
              <span className="tabular-nums">
                {p.score === null ? '—' : formatNumber(p.score, i18n.language)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function RewardsPanel({ evaluation: e }: { evaluation: Evaluation }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [rows, setRows] = useState<
    Record<string, { approved: string; reason: string; certificate: boolean; financial: boolean }>
  >({});
  const [error, setError] = useState<string>();
  useEffect(() => {
    setRows(
      Object.fromEntries(
        e.members.map((m) => [
          m.id,
          {
            approved: String(m.approvedReward ?? m.calculatedReward ?? 0),
            reason: m.adjustmentReason ?? '',
            certificate: m.certificate,
            financial: m.financialReward,
          },
        ]),
      ),
    );
  }, [e]);
  const save = useTeamMutation(() =>
    teamApi.saveRewards({
      id: e.summary.id,
      rewards: e.members.map((m) => ({
        id: m.id,
        approvedReward: Number(rows[m.id]?.approved ?? 0),
        adjustmentReason: rows[m.id]?.reason || null,
        certificate: rows[m.id]?.certificate ?? false,
        financialReward: rows[m.id]?.financial ?? false,
      })),
      version: e.version,
    }),
  );
  const submit = useTeamMutation(async () => {
    const saved = await save.mutateAsync();
    return teamApi.submitRewards({ id: saved.summary.id, version: saved.version });
  });
  const money = (v: number | null | undefined) =>
    v === null || v === undefined ? '—' : `${formatNumber(v, i18n.language)} ${e.budget?.currency ?? 'AED'}`;
  const editable = e.permissions.canManageRewards;
  return (
    <section className="flex flex-col gap-4" data-testid="evaluation-rewards">
      {error && <StatusBanner tone="danger" title={error} />}
      {e.budget && (
        <StatusBanner
          tone="info"
          title={t('rewards.budget', {
            year: e.budget.year,
            used: money(e.budget.used),
            budget: money(e.budget.annualBudget),
            remaining: money(e.budget.remaining),
          })}
        />
      )}
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('card.member')}</TableHeaderCell>
            <TableHeaderCell>{t('employeeTab.grade')}</TableHeaderCell>
            <TableHeaderCell>{t('rewards.calculated')}</TableHeaderCell>
            <TableHeaderCell>{t('rewards.approved')}</TableHeaderCell>
            <TableHeaderCell>{t('rewards.reason')}</TableHeaderCell>
            <TableHeaderCell>{t('rewards.certificate')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {e.members.map((m) => {
            const row = rows[m.id];
            const band = e.template.gradeBands.find((b) => b.code === m.grade);
            return (
              <TableRow key={m.id}>
                <TableCell>
                  {m.employee
                    ? pickText(i18n.language, m.employee.nameAr, m.employee.nameEn)
                    : m.externalName}
                </TableCell>
                <TableCell>{band ? pickText(i18n.language, band.nameAr, band.nameEn) : '—'}</TableCell>
                <TableCell className="tabular-nums">{money(m.calculatedReward)}</TableCell>
                <TableCell>
                  {editable && row ? (
                    <Input
                      type="number"
                      min={0}
                      className="w-28"
                      aria-label={t('rewards.approved')}
                      value={row.approved}
                      onChange={(ev) =>
                        setRows((x) => ({ ...x, [m.id]: { ...row, approved: ev.target.value } }))
                      }
                    />
                  ) : (
                    money(m.approvedReward)
                  )}
                </TableCell>
                <TableCell>
                  {editable && row ? (
                    <Input
                      aria-label={t('rewards.reason')}
                      value={row.reason}
                      onChange={(ev) =>
                        setRows((x) => ({ ...x, [m.id]: { ...row, reason: ev.target.value } }))
                      }
                    />
                  ) : (
                    (m.adjustmentReason ?? '—')
                  )}
                </TableCell>
                <TableCell>
                  {editable && row ? (
                    <Checkbox
                      checked={row.certificate}
                      onChange={(ev) =>
                        setRows((x) => ({ ...x, [m.id]: { ...row, certificate: ev.target.checked } }))
                      }
                      label={t('rewards.certificate')}
                    />
                  ) : m.certificateFileId ? (
                    <Button variant="ghost" size="sm" asChild>
                      <a href={contentUrl(m.certificateFileId)} target="_blank" rel="noreferrer">
                        <Download aria-hidden />
                        {m.certificateNumber}
                      </a>
                    </Button>
                  ) : m.certificate ? (
                    <Award className="size-4 text-warning" aria-label={t('rewards.certificate')} />
                  ) : (
                    '—'
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <p className="font-semibold tabular-nums">
        {t('rewards.approved')}: {money(e.summary.rewardTotal)}
      </p>
      {editable && (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            loading={save.isPending && !submit.isPending}
            onClick={() =>
              save.mutate(undefined, {
                onSuccess: () => toast({ title: t('rewards.save') }),
                onError: (f) => setError(errorMessage(tc, f)),
              })
            }
          >
            {t('rewards.save')}
          </Button>
          <Button
            loading={submit.isPending}
            onClick={() =>
              submit.mutate(undefined, {
                onSuccess: () => toast({ title: t('rewards.submit') }),
                onError: (f) => setError(errorMessage(tc, f)),
              })
            }
          >
            {t('rewards.submit')}
          </Button>
        </div>
      )}
    </section>
  );
}
