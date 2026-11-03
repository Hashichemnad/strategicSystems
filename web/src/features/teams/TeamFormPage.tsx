import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Sparkles, Trash2, UserPlus, Users } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { UnitSelect } from '@/features/access/pickers';
import { useAiFeature } from '@/features/ai/api';
import { LinkBadge } from '@/features/links/LinkBadge';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { useLookupOptions } from '@/features/settings/lookups';
import { pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Textarea,
  useToast,
  Wizard,
} from '@/ui';
import {
  teamApi,
  useTeam,
  useTeamMutation,
  useTeamOptions,
  type MemberInput,
  type MemberSuggestion,
  type TeamInput,
} from './api';

interface DraftMember extends MemberInput {
  key: string;
}

const emptyMember = (roleCode: string, external = false): DraftMember => ({
  key: crypto.randomUUID(),
  id: null,
  employeeId: null,
  externalName: external ? '' : null,
  externalOrganisation: null,
  externalEmail: null,
  externalPhone: null,
  roleCode,
  responsibilities: null,
  requiredSkills: null,
  allocationPercent: null,
  fromDate: null,
});

function nullable(value: string): string | null {
  return value.trim() ? value.trim() : null;
}

/**
 * Formation request (SPEC §4.5 T1): type, names, dates, purpose (with the AI formation assistant), members with roles and responsibilities
 * (with member suggestions), linked record, memo mode; save as draft or submit for the formation approval.
 */
export function TeamFormPage() {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const params: { teamId?: string } = useParams({ strict: false });
  const search: { linkedType?: string; linkedId?: string; type?: string } = useSearch({ strict: false });
  const navigate = useNavigate();
  const toast = useToast();
  const options = useTeamOptions();
  const existing = useTeam(params.teamId);
  const aiFormation = useAiFeature('teams-formation');
  const aiMembers = useAiFeature('teams-members');
  const canAiMembers = useCan('teams.ai.members');
  const approvedBy = useLookupOptions('teams.approved-by', null, '—');
  const [typeCode, setTypeCode] = useState(search.type ?? '');
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [permanent, setPermanent] = useState(true);
  const [subject, setSubject] = useState('');
  const [objectives, setObjectives] = useState('');
  const [mandate, setMandate] = useState('');
  const [memoDraft, setMemoDraft] = useState('');
  const [memoMode, setMemoMode] = useState('generate');
  const [unitId, setUnitId] = useState<string>();
  const [sponsor, setSponsor] = useState<string>();
  const [approvedByCode, setApprovedByCode] = useState('-');
  const [startDate, setStartDate] = useState<string>();
  const [endDate, setEndDate] = useState<string>();
  const [cadence, setCadence] = useState('quarterly');
  const [workload, setWorkload] = useState('medium');
  const [members, setMembers] = useState<DraftMember[]>([]);
  const [aiText, setAiText] = useState('');
  const [aiDraft, setAiDraft] = useState<{ objective: string; mandate: string; memo: string }>();
  const [suggestions, setSuggestions] = useState<MemberSuggestion[]>();
  const [error, setError] = useState<string>();
  const [loaded, setLoaded] = useState(false);

  const linked = useQuery({
    queryKey: ['links', 'resolve', search.linkedType, search.linkedId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/links/resolve', {
          params: { query: { type: search.linkedType ?? '', id: search.linkedId ?? '' } },
          signal,
        }),
      ),
    enabled: !!search.linkedType && !!search.linkedId,
  });

  useEffect(() => {
    const team = existing.data;
    if (!team || loaded) return;
    setLoaded(true);
    setTypeCode(team.typeCode);
    setNameAr(team.nameAr ?? '');
    setNameEn(team.nameEn ?? '');
    setPermanent(team.permanent);
    setSubject(team.subject ?? '');
    setObjectives(team.objectives ?? '');
    setMandate(team.mandate ?? '');
    setMemoDraft(team.memoDraft ?? '');
    setMemoMode(team.memoMode);
    setUnitId(team.owningUnit?.id);
    setSponsor(team.sponsor?.id);
    setApprovedByCode(team.approvedByCode ?? '-');
    setStartDate(team.startDate ?? undefined);
    setEndDate(team.endDate ?? undefined);
    setCadence(team.cadence);
    setWorkload(team.workload);
    setMembers(
      team.members.map((m) => ({
        key: m.id,
        id: m.id,
        employeeId: m.employee?.id ?? null,
        externalName: m.externalName,
        externalOrganisation: m.externalOrganisation,
        externalEmail: m.externalEmail,
        externalPhone: m.externalPhone,
        roleCode: m.roleCode,
        responsibilities: m.responsibilities,
        requiredSkills: m.requiredSkills,
        allocationPercent: m.allocationPercent,
        fromDate: m.fromDate,
      })),
    );
  }, [existing.data, loaded]);

  const type = options.data?.types.find((x) => x.code === typeCode);
  useEffect(() => {
    if (!type || params.teamId) return;
    if (type.permanence !== 'either') setPermanent(type.permanence === 'permanent');
    setCadence(type.defaultCadence);
  }, [type, params.teamId]);

  const roles = useMemo(() => options.data?.roles ?? [], [options.data]);
  const leaderRole = roles.find((r) => r.isLeader)?.code ?? 'president';
  const memberRole = roles.find((r) => !r.isLeader && !r.isSecretary)?.code ?? 'member';
  const roleOptions = roles.map((r) => ({
    value: r.code,
    label: pickText(i18n.language, r.nameAr, r.nameEn),
  }));
  const typeOptions = (options.data?.types ?? []).map((x) => ({
    value: x.code,
    label: pickText(i18n.language, x.nameAr, x.nameEn),
  }));

  const body = (): TeamInput => ({
    typeCode,
    nameAr: nullable(nameAr),
    nameEn: nullable(nameEn),
    permanent,
    subject: nullable(subject),
    objectives: nullable(objectives),
    mandate: nullable(mandate),
    owningUnitId: unitId ?? null,
    sponsorEmployeeId: sponsor ?? null,
    approvedByCode: approvedByCode === '-' ? null : approvedByCode,
    memoMode,
    memoDraft: nullable(memoDraft),
    startDate: startDate ?? null,
    endDate: permanent ? null : (endDate ?? null),
    cadence,
    reportTo: null,
    workload,
    replacesTeamId: null,
    evaluatorRule: null,
    evaluatorEmployeeId: null,
    members: members
      .filter((m) => m.employeeId || m.externalName?.trim())
      .map((m) => ({
        id: m.id,
        employeeId: m.employeeId,
        externalOrganisation: m.externalOrganisation,
        externalEmail: m.externalEmail,
        externalPhone: m.externalPhone,
        roleCode: m.roleCode,
        responsibilities: m.responsibilities,
        requiredSkills: m.requiredSkills,
        allocationPercent: m.allocationPercent,
        fromDate: m.fromDate,
        externalName: m.employeeId ? null : (m.externalName?.trim() ?? null),
      })),
    linkedType: search.linkedType ?? null,
    linkedId: search.linkedId ?? null,
  });

  const save = useTeamMutation(async (submit: boolean) => {
    const team = existing.data
      ? await teamApi.update({ id: existing.data.id, team: body(), version: existing.data.version })
      : await teamApi.create(body());
    return submit ? teamApi.submit({ id: team.id, version: team.version }) : team;
  });

  const draftAi = useTeamMutation(async () =>
    teamApi.aiFormation({
      typeCode: typeCode || null,
      name: nameEn || nameAr || null,
      description: aiText,
      linkedType: search.linkedType ?? null,
      linkedId: search.linkedId ?? null,
    }),
  );
  const suggest = useTeamMutation(async () =>
    teamApi.aiMembers({
      unitId: unitId ?? null,
      skills: members.map((m) => m.requiredSkills ?? '').join(' ') || subject || null,
      exclude: members.flatMap((m) => (m.employeeId ? [m.employeeId] : [])),
    }),
  );

  const run = (submit: boolean) => {
    setError(undefined);
    save.mutate(submit, {
      onSuccess: (team) => {
        toast({ title: t(submit ? 'form.submitted' : 'form.saved') });
        void navigate({ to: '/teams/$teamId', params: { teamId: team.id }, search: { tab: 'overview' } });
      },
      onError: (failure) => setError(errorMessage(tc, failure)),
    });
  };

  if (options.isPending || (params.teamId && existing.isPending))
    return <Skeleton className="h-96 rounded-lg" />;
  const update = (key: string, patch: Partial<DraftMember>) =>
    setMembers((list) => list.map((m) => (m.key === key ? { ...m, ...patch } : m)));

  const basics = (
    <div className="grid gap-4 md:grid-cols-2">
      <FormField label={t('fields.type')} required>
        <Select
          value={typeCode || undefined}
          onValueChange={setTypeCode}
          options={typeOptions}
          disabled={!!params.teamId}
        />
      </FormField>
      <Switch
        checked={permanent}
        onCheckedChange={setPermanent}
        disabled={type?.permanence !== 'either'}
        label={t('fields.permanent')}
      />
      <FormField label={t('fields.nameAr')} required>
        <Input
          dir="rtl"
          lang="ar"
          value={nameAr}
          maxLength={300}
          onChange={(e) => setNameAr(e.target.value)}
        />
      </FormField>
      <FormField label={t('fields.nameEn')} required>
        <Input
          dir="ltr"
          lang="en"
          value={nameEn}
          maxLength={300}
          onChange={(e) => setNameEn(e.target.value)}
        />
      </FormField>
      <FormField label={t('fields.owningUnit')} required>
        <UnitSelect value={unitId} onChange={setUnitId} />
      </FormField>
      <FormField label={t('fields.sponsor')}>
        <EmployeePicker value={sponsor} onChange={setSponsor} />
      </FormField>
      <FormField label={t('fields.startDate')} required>
        <DatePicker value={startDate} onChange={setStartDate} />
      </FormField>
      {!permanent && (
        <FormField label={t('fields.endDate')} required>
          <DatePicker value={endDate} onChange={setEndDate} />
        </FormField>
      )}
      <FormField label={t('fields.cadence')}>
        <Select
          value={cadence}
          onValueChange={setCadence}
          options={(['monthly', 'quarterly', 'biannually', 'annually', 'other'] as const).map((c) => ({
            value: c,
            label: t(`cadence.${c}`),
          }))}
        />
      </FormField>
      <FormField label={t('fields.workload')}>
        <Select
          value={workload}
          onValueChange={setWorkload}
          options={(['high', 'medium', 'low'] as const).map((w) => ({ value: w, label: t(`workload.${w}`) }))}
        />
      </FormField>
      <FormField label={t('fields.approvedBy')}>
        <Select value={approvedByCode} onValueChange={setApprovedByCode} options={approvedBy} />
      </FormField>
      <FormField label={t('fields.memoMode')}>
        <Select
          value={memoMode}
          onValueChange={setMemoMode}
          options={(['generate', 'external'] as const).map((m) => ({ value: m, label: t(`memoMode.${m}`) }))}
        />
      </FormField>
      {linked.data && (
        <div className="md:col-span-2">
          <p className="mb-1 text-sm text-muted-foreground">{t('form.linkedTo')}</p>
          <LinkBadge view={linked.data} />
        </div>
      )}
    </div>
  );

  const purpose = (
    <div className="flex flex-col gap-4">
      {aiFormation && (
        <section className="flex flex-col gap-2 rounded-md border border-dashed p-3">
          <FormField label={t('form.aiDescription')}>
            <Textarea rows={2} value={aiText} onChange={(e) => setAiText(e.target.value)} />
          </FormField>
          <div>
            <Button
              variant="outline"
              loading={draftAi.isPending}
              disabled={!aiText.trim()}
              onClick={() =>
                draftAi.mutate(undefined, {
                  onSuccess: (d) =>
                    setAiDraft({
                      objective: pickText(i18n.language, d.objectiveAr, d.objectiveEn),
                      mandate: pickText(i18n.language, d.mandateAr, d.mandateEn),
                      memo: pickText(i18n.language, d.memoAr, d.memoEn),
                    }),
                  onError: (failure) => setError(errorMessage(tc, failure)),
                })
              }
            >
              <Sparkles aria-hidden />
              {t('form.draftWithAi')}
            </Button>
          </div>
          {aiDraft && (
            <div
              className="flex flex-col gap-2 rounded-md bg-muted p-3 text-sm"
              data-testid="ai-formation-draft"
            >
              <StatusChip tone="info">{t('form.aiDraftLabel')}</StatusChip>
              <p className="whitespace-pre-line">{aiDraft.objective}</p>
              <p className="whitespace-pre-line">{aiDraft.mandate}</p>
              <div>
                <Button
                  size="sm"
                  onClick={() => {
                    setObjectives(aiDraft.objective);
                    setMandate(aiDraft.mandate);
                    setMemoDraft(aiDraft.memo);
                    setAiDraft(undefined);
                  }}
                >
                  {t('form.aiApply')}
                </Button>
              </div>
            </div>
          )}
        </section>
      )}
      <FormField label={t('fields.subject')}>
        <Input value={subject} maxLength={600} onChange={(e) => setSubject(e.target.value)} />
      </FormField>
      <FormField label={t('fields.objectives')}>
        <Textarea rows={3} value={objectives} onChange={(e) => setObjectives(e.target.value)} />
      </FormField>
      <FormField label={t('fields.mandate')}>
        <Textarea rows={4} value={mandate} onChange={(e) => setMandate(e.target.value)} />
      </FormField>
      {memoMode === 'generate' && (
        <FormField label={t('fields.memoDraft')}>
          <Textarea rows={4} value={memoDraft} onChange={(e) => setMemoDraft(e.target.value)} />
        </FormField>
      )}
    </div>
  );

  const memberStep = (
    <div className="flex flex-col gap-3" data-testid="team-members-step">
      {members.length === 0 && <StatusBanner tone="info" title={t('form.noMembers')} />}
      {members.map((m) => (
        <fieldset key={m.key} className="grid gap-3 rounded-md border p-3 md:grid-cols-[2fr_1fr_2fr_auto]">
          {m.externalName === null ? (
            <FormField label={t('card.member')}>
              <EmployeePicker
                value={m.employeeId ?? undefined}
                onChange={(v) => update(m.key, { employeeId: v ?? null })}
              />
            </FormField>
          ) : (
            <div className="grid gap-2 sm:grid-cols-2">
              <FormField label={t('form.externalName')}>
                <Input
                  value={m.externalName}
                  onChange={(e) => update(m.key, { externalName: e.target.value })}
                />
              </FormField>
              <FormField label={t('form.externalOrganisation')}>
                <Input
                  value={m.externalOrganisation ?? ''}
                  onChange={(e) => update(m.key, { externalOrganisation: nullable(e.target.value) })}
                />
              </FormField>
            </div>
          )}
          <FormField label={t('form.role')}>
            <Select
              value={m.roleCode}
              onValueChange={(v) => update(m.key, { roleCode: v })}
              options={roleOptions}
            />
          </FormField>
          <div className="grid gap-2 sm:grid-cols-2">
            <FormField label={t('form.responsibilities')}>
              <Input
                value={m.responsibilities ?? ''}
                onChange={(e) => update(m.key, { responsibilities: nullable(e.target.value) })}
              />
            </FormField>
            <FormField label={t('form.skills')}>
              <Input
                value={m.requiredSkills ?? ''}
                onChange={(e) => update(m.key, { requiredSkills: nullable(e.target.value) })}
              />
            </FormField>
          </div>
          <Button
            variant="ghost"
            size="icon"
            className="self-end"
            aria-label={t('form.remove')}
            onClick={() => setMembers((list) => list.filter((x) => x.key !== m.key))}
          >
            <Trash2 aria-hidden />
          </Button>
        </fieldset>
      ))}
      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          onClick={() => setMembers((l) => [...l, emptyMember(l.length === 0 ? leaderRole : memberRole)])}
        >
          <UserPlus aria-hidden />
          {t('form.addMember')}
        </Button>
        <Button variant="outline" onClick={() => setMembers((l) => [...l, emptyMember(memberRole, true)])}>
          <UserPlus aria-hidden />
          {t('form.addExternal')}
        </Button>
        {aiMembers && canAiMembers && (
          <Button
            variant="outline"
            loading={suggest.isPending}
            onClick={() =>
              suggest.mutate(undefined, {
                onSuccess: setSuggestions,
                onError: (f) => setError(errorMessage(tc, f)),
              })
            }
          >
            <Users aria-hidden />
            {t('form.suggestMembers')}
          </Button>
        )}
      </div>
      {suggestions && (
        <ul className="grid gap-2 md:grid-cols-2" data-testid="member-suggestions">
          {suggestions.map((s) => (
            <li
              key={s.employee.id}
              className="flex items-center justify-between gap-2 rounded-md border p-2 text-sm"
            >
              <div>
                <p className="font-medium">{pickText(i18n.language, s.employee.nameAr, s.employee.nameEn)}</p>
                <p className="text-xs text-muted-foreground">
                  {t('form.activeTeams', { count: s.activeMemberships })} ·{' '}
                  {s.reasons.map((r) => t(`form.suggestionReasons.${r as 'skills'}`)).join(' · ')}
                </p>
              </div>
              <div className="flex items-center gap-2">
                {s.overAssigned && <StatusChip tone="warning">{t('form.overAssigned')}</StatusChip>}
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    setMembers((l) => [...l, { ...emptyMember(memberRole), employeeId: s.employee.id }]);
                    setSuggestions((list) => list?.filter((x) => x.employee.id !== s.employee.id));
                  }}
                >
                  {t('form.addMember')}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );

  const review = (
    <dl className="grid gap-3 text-sm md:grid-cols-2">
      <div>
        <dt className="text-muted-foreground">{t('fields.type')}</dt>
        <dd>{type ? pickText(i18n.language, type.nameAr, type.nameEn) : '—'}</dd>
      </div>
      <div>
        <dt className="text-muted-foreground">{t('fields.nameEn')}</dt>
        <dd>{pickText(i18n.language, nameAr, nameEn) || '—'}</dd>
      </div>
      <div>
        <dt className="text-muted-foreground">{t('fields.startDate')}</dt>
        <dd>{startDate ?? '—'}</dd>
      </div>
      <div>
        <dt className="text-muted-foreground">{t('fields.endDate')}</dt>
        <dd>{permanent ? t('permanence.permanent') : (endDate ?? '—')}</dd>
      </div>
      <div className="md:col-span-2">
        <dt className="text-muted-foreground">{t('tabs.members')}</dt>
        <dd>{members.length}</dd>
      </div>
    </dl>
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t(params.teamId ? 'form.titleEdit' : 'form.titleNew')}
        description={existing.data?.number ?? undefined}
      />
      {error && <StatusBanner tone="danger" title={error} />}
      <Wizard
        steps={[
          {
            key: 'basics',
            title: t('form.steps.basics'),
            content: basics,
            validate: () => !!typeCode && !!(nameAr.trim() || nameEn.trim()),
          },
          { key: 'purpose', title: t('form.steps.purpose'), content: purpose },
          { key: 'members', title: t('form.steps.members'), content: memberStep },
          { key: 'review', title: t('form.steps.review'), content: review },
        ]}
        onSaveDraft={() => run(false)}
        onFinish={() => run(true)}
        finishLabel={t('form.submit')}
        busy={save.isPending}
      />
    </div>
  );
}
