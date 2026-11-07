import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
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
  useToast,
} from '@/ui';
import {
  teamApi,
  useTeamConfiguration,
  useTeamMutation,
  type MemberRole,
  type RewardMatrixDto,
  type TeamConfiguration,
  type TeamType,
} from './api';
import { ResponsibilitiesLink } from '@/features/access/ResponsibilitiesLink';

/** Settings → Teams (SPEC §4.3, ADR 0041): general values, team types, member roles, versioned templates and reward matrices. */
export function TeamSettingsPage() {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const config = useTeamConfiguration();
  if (config.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (config.isError) return <StatusBanner tone="danger" title={errorMessage(tc, config.error)} />;
  return (
    <div className="flex flex-col gap-6">
      <ResponsibilitiesLink system="teams" />
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <Tabs defaultValue="general">
        <TabsList className="flex-wrap">
          {(['general', 'types', 'roles', 'templates', 'matrices'] as const).map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`settings.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="general">
          <GeneralSettings config={config.data} />
        </TabsContent>
        <TabsContent value="types">
          <TypesTable config={config.data} />
        </TabsContent>
        <TabsContent value="roles">
          <RolesTable config={config.data} />
        </TabsContent>
        <TabsContent value="templates">
          <Templates config={config.data} />
        </TabsContent>
        <TabsContent value="matrices">
          <Matrices config={config.data} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function GeneralSettings({ config }: { config: TeamConfiguration }) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [value, setValue] = useState(config.settings);
  useEffect(() => setValue(config.settings), [config.settings]);
  const save = useTeamMutation(teamApi.saveSettings);
  const fields = [
    'overAssignedThreshold',
    'reportReminderDays',
    'evaluationReminderDays',
    'recommendationDefaultDueDays',
    'memberSuggestionLimit',
  ] as const;
  return (
    <section className="flex max-w-2xl flex-col gap-4 rounded-lg border bg-card p-6">
      <div className="grid gap-4 sm:grid-cols-2">
        {fields.map((key) => (
          <FormField key={key} label={t(`settings.${key}`)}>
            <Input
              type="number"
              value={String(value[key])}
              onChange={(e) => setValue((v) => ({ ...v, [key]: Number(e.target.value) }))}
            />
          </FormField>
        ))}
      </div>
      <div>
        <Button
          loading={save.isPending}
          onClick={() =>
            save.mutate(
              { settings: value, version: config.settingsVersion },
              {
                onSuccess: () => toast({ title: t('settings.saved') }),
                onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }),
              },
            )
          }
        >
          {tc('actions.save')}
        </Button>
      </div>
    </section>
  );
}

function TypesTable({ config }: { config: TeamConfiguration }) {
  const { t, i18n } = useTranslation('teams');
  const [editing, setEditing] = useState<TeamType>();
  return (
    <>
      <Table data-testid="team-types">
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('settings.types')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.permanent')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.requiresApproval')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.trigger')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.evaluator')}</TableHeaderCell>
            <TableHeaderCell />
          </TableRow>
        </TableHead>
        <TableBody>
          {config.types.map((type) => (
            <TableRow key={type.id}>
              <TableCell>
                {pickText(i18n.language, type.nameAr, type.nameEn)}{' '}
                <span className="text-xs text-muted-foreground">{type.code}</span>
              </TableCell>
              <TableCell>{t(`permanence.${type.permanence as 'either'}`)}</TableCell>
              <TableCell>{type.requiresApproval ? '✓' : '—'}</TableCell>
              <TableCell>{t(`settings.triggers.${type.evaluationTrigger as 'auto'}`)}</TableCell>
              <TableCell>{t(`settings.evaluators.${type.evaluatorRule as 'leader'}`)}</TableCell>
              <TableCell>
                <Button size="sm" variant="ghost" onClick={() => setEditing(type)}>
                  {t('actions.edit')}
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {editing && <TypeDialog type={editing} onClose={() => setEditing(undefined)} />}
    </>
  );
}

function TypeDialog({ type, onClose }: { type: TeamType; onClose: () => void }) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const [v, setV] = useState(type);
  const [error, setError] = useState<string>();
  const save = useTeamMutation(teamApi.saveType);
  const options = <K extends string>(keys: readonly K[], label: (k: K) => string) =>
    keys.map((k) => ({ value: k, label: label(k) }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('settings.types')} · ${type.code}`}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    id: v.id,
                    code: v.code,
                    version: v.version,
                    values: {
                      nameAr: v.nameAr,
                      nameEn: v.nameEn,
                      permanence: v.permanence,
                      requiresApproval: v.requiresApproval,
                      requiresMemo: v.requiresMemo,
                      linkableTypes: v.linkableTypes,
                      defaultCadence: v.defaultCadence,
                      evaluationTrigger: v.evaluationTrigger,
                      annualMonth: v.annualMonth,
                      annualDay: v.annualDay,
                      evaluatorRule: v.evaluatorRule,
                      evaluatorEmployeeId: v.evaluatorEmployeeId,
                      evaluatorMemberRole: v.evaluatorMemberRole,
                      leaderEvaluatorRule: v.leaderEvaluatorRule,
                      leaderEvaluatorEmployeeId: v.leaderEvaluatorEmployeeId,
                      leaderEvaluatorMemberRole: v.leaderEvaluatorMemberRole,
                      sort: v.sort,
                      active: v.active,
                    },
                  },
                  { onSuccess: onClose, onError: (f) => setError(errorMessage(tc, f)) },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <div className="grid gap-3 sm:grid-cols-2">
          <FormField label={t('fields.nameAr')}>
            <Input dir="rtl" value={v.nameAr} onChange={(e) => setV({ ...v, nameAr: e.target.value })} />
          </FormField>
          <FormField label={t('fields.nameEn')}>
            <Input dir="ltr" value={v.nameEn} onChange={(e) => setV({ ...v, nameEn: e.target.value })} />
          </FormField>
          <FormField label={t('fields.permanent')}>
            <Select
              value={v.permanence}
              onValueChange={(x) => setV({ ...v, permanence: x })}
              options={options(['permanent', 'temporary', 'either'] as const, (k) => t(`permanence.${k}`))}
            />
          </FormField>
          <FormField label={t('settings.trigger')}>
            <Select
              value={v.evaluationTrigger}
              onValueChange={(x) => setV({ ...v, evaluationTrigger: x })}
              options={options(['none', 'auto', 'annual', 'end-date', 'linked-record-closed'] as const, (k) =>
                t(`settings.triggers.${k}`),
              )}
            />
          </FormField>
          <FormField label={t('settings.evaluator')}>
            <Select
              value={v.evaluatorRule}
              onValueChange={(x) => setV({ ...v, evaluatorRule: x })}
              options={options(['leader', 'sponsor', 'administrator'] as const, (k) =>
                t(`settings.evaluators.${k}`),
              )}
            />
          </FormField>
          <FormField label={t('settings.leaderEvaluator')}>
            <Select
              value={v.leaderEvaluatorRule}
              onValueChange={(x) => setV({ ...v, leaderEvaluatorRule: x })}
              options={options(['same', 'sponsor', 'administrator'] as const, (k) =>
                t(`settings.evaluators.${k}`),
              )}
            />
          </FormField>
          <FormField label={t('settings.annualDate')}>
            <div className="flex gap-2">
              <Input
                type="number"
                min={1}
                max={12}
                aria-label="month"
                value={String(v.annualMonth)}
                onChange={(e) => setV({ ...v, annualMonth: Number(e.target.value) })}
              />
              <Input
                type="number"
                min={1}
                max={31}
                aria-label="day"
                value={String(v.annualDay)}
                onChange={(e) => setV({ ...v, annualDay: Number(e.target.value) })}
              />
            </div>
          </FormField>
          <FormField label={t('fields.cadence')}>
            <Select
              value={v.defaultCadence}
              onValueChange={(x) => setV({ ...v, defaultCadence: x })}
              options={options(['monthly', 'quarterly', 'biannually', 'annually', 'other'] as const, (k) =>
                t(`cadence.${k}`),
              )}
            />
          </FormField>
        </div>
        <div className="flex flex-wrap gap-4">
          <Checkbox
            checked={v.requiresApproval}
            onChange={(ev) => setV({ ...v, requiresApproval: ev.target.checked })}
            label={t('settings.requiresApproval')}
          />
          <Checkbox
            checked={v.requiresMemo}
            onChange={(ev) => setV({ ...v, requiresMemo: ev.target.checked })}
            label={t('settings.requiresMemo')}
          />
          <Checkbox
            checked={v.active}
            onChange={(ev) => setV({ ...v, active: ev.target.checked })}
            label={t('settings.active')}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function RolesTable({ config }: { config: TeamConfiguration }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useTeamMutation(teamApi.saveRole);
  const flags = ['isLeader', 'canViewResults', 'countsTowardQuorum', 'isSecretary', 'active'] as const;
  const toggle = (role: MemberRole, flag: (typeof flags)[number], value: boolean) =>
    save.mutate(
      { ...role, [flag]: value },
      { onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }) },
    );
  return (
    <Table data-testid="member-roles">
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t('settings.roles')}</TableHeaderCell>
          {flags.map((f) => (
            <TableHeaderCell key={f}>{t(`settings.${f}`)}</TableHeaderCell>
          ))}
        </TableRow>
      </TableHead>
      <TableBody>
        {config.roles.map((role) => (
          <TableRow key={role.id}>
            <TableCell>{pickText(i18n.language, role.nameAr, role.nameEn)}</TableCell>
            {flags.map((f) => (
              <TableCell key={f}>
                <Checkbox
                  checked={role[f]}
                  onChange={(ev) => toggle(role, f, ev.target.checked)}
                  label={<span className="sr-only">{t(`settings.${f}`)}</span>}
                />
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function Templates({ config }: { config: TeamConfiguration }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const create = useTeamMutation(teamApi.saveTemplate);
  const publish = useTeamMutation(teamApi.publishTemplate);
  return (
    <div className="flex flex-col gap-4" data-testid="evaluation-templates">
      <div>
        <Button
          variant="outline"
          loading={create.isPending}
          onClick={() =>
            create.mutate(
              { id: null, year: new Date().getFullYear(), content: null, notes: null, version: null },
              { onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }) },
            )
          }
        >
          {t('settings.newVersion')}
        </Button>
      </div>
      {config.templates.map((tpl) => (
        <section key={tpl.id} className="flex flex-col gap-3 rounded-lg border bg-card p-4">
          <header className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="font-semibold">
              {t('settings.version', { year: tpl.year, version: tpl.versionNo })}
            </h3>
            <div className="flex items-center gap-2">
              <StatusChip
                tone={tpl.status === 'published' ? 'success' : tpl.status === 'draft' ? 'info' : 'neutral'}
              >
                {t(`settings.versionStatus.${tpl.status as 'draft'}`)}
              </StatusChip>
              {tpl.status === 'draft' && (
                <Button
                  size="sm"
                  loading={publish.isPending}
                  onClick={() =>
                    publish.mutate(tpl.id, {
                      onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }),
                    })
                  }
                >
                  {t('settings.publish')}
                </Button>
              )}
            </div>
          </header>
          <ul className="grid gap-2 text-sm md:grid-cols-2">
            {tpl.content.criteria.map((c) => (
              <li key={c.code} className="rounded-md border p-2">
                <p className="font-medium">
                  {pickText(i18n.language, c.nameAr, c.nameEn)} · {t('settings.weight')}{' '}
                  {formatNumber(c.weight, i18n.language)}
                </p>
                <p className="text-xs text-muted-foreground">
                  {c.levels
                    .map(
                      (l) =>
                        `${pickText(i18n.language, l.nameAr, l.nameEn)} = ${formatNumber(l.score, i18n.language)}`,
                    )
                    .join(' · ')}
                </p>
              </li>
            ))}
          </ul>
          <p className="text-sm">
            {t('settings.grades')}:{' '}
            {tpl.content.gradeBands
              .map((b) => `${pickText(i18n.language, b.nameAr, b.nameEn)} ${b.min}–${b.max}`)
              .join(' · ')}{' '}
            · {t('settings.threshold', { value: tpl.content.evidenceThreshold })}
          </p>
        </section>
      ))}
    </div>
  );
}

function Matrices({ config }: { config: TeamConfiguration }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const create = useTeamMutation(teamApi.saveMatrix);
  const publish = useTeamMutation(teamApi.publishMatrix);
  const grades = config.templates.find((x) => x.status === 'published')?.content.gradeBands ?? [];
  return (
    <div className="flex flex-col gap-4" data-testid="reward-matrices">
      <div>
        <Button
          variant="outline"
          loading={create.isPending}
          onClick={() =>
            create.mutate(
              { id: null, year: new Date().getFullYear(), content: null, version: null },
              { onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }) },
            )
          }
        >
          {t('settings.newVersion')}
        </Button>
      </div>
      {config.rewardMatrices.map((m) => (
        <MatrixEditor
          key={m.id}
          matrix={m}
          grades={grades.map((g) => ({ code: g.code, name: pickText(i18n.language, g.nameAr, g.nameEn) }))}
          roles={config.roles.map((r) => ({
            code: r.code,
            name: pickText(i18n.language, r.nameAr, r.nameEn),
          }))}
          onPublish={() =>
            publish.mutate(m.id, { onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }) })
          }
        />
      ))}
    </div>
  );
}

function MatrixEditor({
  matrix,
  grades,
  roles,
  onPublish,
}: {
  matrix: RewardMatrixDto;
  grades: { code: string; name: string }[];
  roles: { code: string; name: string }[];
  onPublish: () => void;
}) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [workload, setWorkload] = useState('high');
  const [cells, setCells] = useState(matrix.content.cells);
  const [budget, setBudget] = useState(String(matrix.content.annualBudget));
  useEffect(() => {
    setCells(matrix.content.cells);
    setBudget(String(matrix.content.annualBudget));
  }, [matrix]);
  const save = useTeamMutation(teamApi.saveMatrix);
  const draft = matrix.status === 'draft';
  const amount = (grade: string, role: string) =>
    cells.find((c) => c.grade === grade && c.workload === workload && c.role === role)?.amount ?? 0;
  const set = (grade: string, role: string, value: number) =>
    setCells((list) => [
      ...list.filter((c) => !(c.grade === grade && c.workload === workload && c.role === role)),
      { grade, workload, role, amount: value },
    ]);
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold">
          {t('settings.version', { year: matrix.year, version: matrix.versionNo })}
        </h3>
        <div className="flex items-center gap-2">
          <StatusChip tone={matrix.status === 'published' ? 'success' : draft ? 'info' : 'neutral'}>
            {t(`settings.versionStatus.${matrix.status as 'draft'}`)}
          </StatusChip>
          {draft && (
            <>
              <Button
                size="sm"
                variant="outline"
                loading={save.isPending}
                onClick={() =>
                  save.mutate(
                    {
                      id: matrix.id,
                      year: matrix.year,
                      content: {
                        ...matrix.content,
                        cells: cells.filter((c) => c.amount > 0),
                        annualBudget: Number(budget),
                      },
                      version: matrix.version,
                    },
                    {
                      onSuccess: () => toast({ title: t('settings.saved') }),
                      onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }),
                    },
                  )
                }
              >
                {tc('actions.save')}
              </Button>
              <Button size="sm" onClick={onPublish}>
                {t('settings.publish')}
              </Button>
            </>
          )}
        </div>
      </header>
      <div className="flex flex-wrap items-end gap-3">
        <FormField label={t('fields.workload')}>
          <Select
            value={workload}
            onValueChange={setWorkload}
            options={(['high', 'medium', 'low'] as const).map((w) => ({
              value: w,
              label: t(`workload.${w}`),
            }))}
          />
        </FormField>
        <FormField label={`${t('settings.budget')} (${matrix.content.currency})`}>
          <Input type="number" value={budget} disabled={!draft} onChange={(e) => setBudget(e.target.value)} />
        </FormField>
      </div>
      <div className="overflow-x-auto">
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('employeeTab.grade')}</TableHeaderCell>
              {roles.map((r) => (
                <TableHeaderCell key={r.code}>{r.name}</TableHeaderCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {grades.map((g) => (
              <TableRow key={g.code}>
                <TableCell>{g.name}</TableCell>
                {roles.map((r) => (
                  <TableCell key={r.code}>
                    {draft ? (
                      <Input
                        type="number"
                        min={0}
                        className="w-24"
                        aria-label={`${g.name} ${r.name}`}
                        value={String(amount(g.code, r.code))}
                        onChange={(e) => set(g.code, r.code, Number(e.target.value))}
                      />
                    ) : (
                      <span className="tabular-nums">
                        {formatNumber(amount(g.code, r.code), i18n.language)}
                      </span>
                    )}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </section>
  );
}
