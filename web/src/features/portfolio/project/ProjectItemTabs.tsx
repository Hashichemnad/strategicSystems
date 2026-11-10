import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, Upload } from 'lucide-react';
import { errorMessage } from '@/api/problem';
import { contentUrl } from '@/features/files/api';
import { LinkedTeamsPanel } from '@/features/teams/LinkedTeamsPanel';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
import { Button, FileDrop, RichTextView, StatusBanner, StatusChip, useToast, type SelectOption } from '@/ui';
import type { PortfolioOptions } from '../api';
import { useFeature } from '../featuresApi';
import { ContractExtrasSection } from '../standards/ContractExtrasPanel';
import { MeetingVendorCell, useVendorOptions } from '../wasel/ProjectVendorBits';
import { percent } from '../wasel/waselFormat';
import {
  projectApi,
  fromPercent,
  toPercent,
  useProjectFiles,
  useProjectMutation,
  type Challenge,
  type Contract,
  type Lesson,
  type ProjectFull,
  type ProjectKpi,
  type ProjectMeeting,
  type ProjectRisk,
  type PlanningItem,
  type ProposedMember,
  type Stakeholder,
} from '../projectApi';
import { asText, ItemsTab, num, str, type FieldSpec, type FormValues } from './ItemsTab';

type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const ragTone: Record<string, Tone> = {
  green: 'success',
  amber: 'warning',
  red: 'danger',
  none: 'neutral',
};
export const bandTone: Record<string, Tone> = {
  low: 'success',
  medium: 'info',
  high: 'warning',
  critical: 'danger',
};

function useLabels() {
  const { i18n } = useTranslation();
  const lang = i18n.language;
  const opts = (items: PortfolioOptions['themes']): SelectOption[] =>
    items.map((o) => ({ value: o.code, label: pickText(lang, o.labelAr, o.labelEn) }));
  const label = (items: PortfolioOptions['themes'], code: string | null | undefined) => {
    const found = items.find((o) => o.code === code);
    return found ? pickText(lang, found.labelAr, found.labelEn) : (code ?? '—');
  };
  return { lang, opts, label };
}

interface TabProps {
  project: ProjectFull;
  options: PortfolioOptions;
}

export function KpisTab({ project, options }: TabProps) {
  const { t } = useTranslation('portfolio');
  const { lang, opts, label } = useLabels();
  const isNew = (v: FormValues) => v.nature !== 'existing';
  const fields: FieldSpec[] = [
    {
      name: 'nature',
      label: t('project.kpi.nature'),
      kind: 'select',
      required: true,
      options: [
        { value: 'new', label: t('project.kpi.natures.new') },
        { value: 'existing', label: t('project.kpi.natures.existing') },
      ],
    },
    {
      name: 'kpiCardId',
      label: t('project.kpi.card'),
      kind: 'kpiCard',
      required: true,
      when: (v) => v.nature === 'existing',
    },
    { name: 'nameAr', label: t('fields.nameAr'), kind: 'text', when: isNew },
    { name: 'nameEn', label: t('fields.nameEn'), kind: 'text', when: isNew },
    { name: 'projectGoal', label: t('project.kpi.goal'), kind: 'textarea' },
    { name: 'equation', label: t('project.kpi.equation'), kind: 'text', when: isNew },
    {
      name: 'direction',
      label: t('project.kpi.direction'),
      kind: 'select',
      when: isNew,
      options: ['increase', 'decrease', 'matching'].map((d) => ({
        value: d,
        label: t(`project.kpi.directions.${d as 'increase'}`),
      })),
    },
    {
      name: 'indexClassCode',
      label: t('project.kpi.indexClass'),
      kind: 'select',
      options: opts(options.kpiIndexClasses),
    },
    { name: 'target', label: t('project.kpi.target'), kind: 'number', when: isNew },
    { name: 'targetYear', label: t('project.kpi.targetYear'), kind: 'number' },
    {
      name: 'whenMeasured',
      label: t('project.kpi.whenMeasured'),
      kind: 'select',
      options: [
        { value: 'during', label: t('project.kpi.during') },
        { value: 'after', label: t('project.kpi.after') },
      ],
    },
    { name: 'currentResult', label: t('project.kpi.currentResult'), kind: 'number', when: isNew },
  ];
  return (
    <ItemsTab<ProjectKpi>
      projectId={project.id}
      kind="kpis"
      testId="project-kpis"
      title={t('project.tabs.kpis')}
      description={t('project.kpi.hint')}
      canWrite={project.permissions.canEditContent}
      fields={fields}
      empty={{ nature: 'new', direction: 'increase', whenMeasured: 'during' }}
      fromRow={(k) => ({
        nature: k.nature,
        kpiCardId: k.kpiCardId ?? null,
        nameAr: k.nameAr ?? null,
        nameEn: k.nameEn ?? null,
        projectGoal: k.projectGoal ?? null,
        equation: k.equation ?? null,
        direction: k.direction ?? null,
        indexClassCode: k.indexClassCode ?? null,
        target: asText(k.target),
        targetYear: asText(k.targetYear),
        whenMeasured: k.whenMeasured ?? null,
        currentResult: asText(k.currentResult),
      })}
      toPayload={(v) => ({
        nature: v.nature,
        kpiCardId: v.nature === 'existing' ? str(v.kpiCardId) : null,
        projectGoal: str(v.projectGoal),
        nameAr: str(v.nameAr),
        nameEn: str(v.nameEn),
        equation: str(v.equation),
        direction: str(v.direction),
        indexClassCode: str(v.indexClassCode),
        target: num(v.target),
        targetYear: num(v.targetYear),
        whenMeasured: str(v.whenMeasured),
        currentResult: num(v.currentResult),
      })}
      columns={[
        {
          label: t('fields.name'),
          render: (k) => (
            <>
              {k.kpiCardCode && <bdi className="me-1 text-muted-foreground">{k.kpiCardCode}</bdi>}
              {pickText(lang, k.nameAr, k.nameEn) || k.kpiCardCode || '—'}
            </>
          ),
        },
        { label: t('project.kpi.nature'), render: (k) => t(`project.kpi.natures.${k.nature as 'new'}`) },
        {
          label: t('project.kpi.indexClass'),
          render: (k) => label(options.kpiIndexClasses, k.indexClassCode),
        },
        {
          label: t('project.kpi.target'),
          render: (k) => (k.target !== null ? formatNumber(k.target, lang) : '—'),
        },
        {
          label: t('project.kpi.performance'),
          render: (k) =>
            k.cappedPerformance !== null
              ? `${formatNumber(toPercent(k.cappedPerformance) ?? 0, lang)} %`
              : '—',
        },
        {
          label: t('project.kpi.goalStatus'),
          render: (k) => (
            <StatusChip tone={ragTone[k.goalStatus] ?? 'neutral'}>
              {t(`project.rag.${k.goalStatus as 'green'}`)}
            </StatusChip>
          ),
        },
      ]}
    />
  );
}

export function RisksTab({ project, options }: TabProps) {
  const { t } = useTranslation('portfolio');
  const { lang, opts, label } = useLabels();
  const scale = (max: number): SelectOption[] =>
    Array.from({ length: max }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }));
  const fields: FieldSpec[] = [
    { name: 'title', label: t('project.risk.title'), kind: 'text', required: true, wide: true },
    {
      name: 'categoryCode',
      label: t('project.risk.category'),
      kind: 'select',
      options: opts(options.riskCategories),
    },
    {
      name: 'probability',
      label: t('project.risk.probability'),
      kind: 'select',
      options: scale(5),
      required: true,
    },
    { name: 'impact', label: t('project.risk.impact'), kind: 'select', options: scale(5), required: true },
    { name: 'controlEvaluation', label: t('project.risk.control'), kind: 'select', options: scale(10) },
    { name: 'riskFactor', label: t('project.risk.factor'), kind: 'textarea' },
    {
      name: 'response',
      label: t('project.risk.response'),
      kind: 'select',
      options: ['accept', 'avoid', 'mitigate', 'transfer'].map((r) => ({
        value: r,
        label: t(`project.risk.responses.${r as 'accept'}`),
      })),
    },
    { name: 'ownerEmployeeId', label: t('project.risk.owner'), kind: 'employee' },
    { name: 'mitigationActions', label: t('project.risk.mitigation'), kind: 'textarea' },
    { name: 'implementationPct', label: t('project.risk.implementation'), kind: 'number' },
  ];
  return (
    <ItemsTab<ProjectRisk>
      projectId={project.id}
      kind="risks"
      testId="project-risks"
      title={t('project.tabs.risks')}
      description={t('project.risk.hint')}
      canWrite={project.permissions.canEditContent}
      fields={fields}
      empty={{ probability: '3', impact: '3' }}
      fromRow={(r) => ({
        title: r.title ?? null,
        categoryCode: r.categoryCode ?? null,
        probability: String(r.probability),
        impact: String(r.impact),
        controlEvaluation: asText(r.controlEvaluation),
        riskFactor: r.riskFactor ?? null,
        response: r.response ?? null,
        ownerEmployeeId: r.owner?.id ?? null,
        mitigationActions: r.mitigationActions ?? null,
        implementationPct: asText(toPercent(r.implementationPct)),
      })}
      toPayload={(v) => ({
        title: str(v.title),
        categoryCode: str(v.categoryCode),
        probability: num(v.probability) ?? 1,
        impact: num(v.impact) ?? 1,
        riskFactor: str(v.riskFactor),
        controlEvaluation: num(v.controlEvaluation),
        response: str(v.response),
        mitigationActions: str(v.mitigationActions),
        implementationPct: num(v.implementationPct) === null ? null : (num(v.implementationPct) ?? 0) / 100,
        implementationStart: null,
        implementationEnd: null,
        ownerEmployeeId: str(v.ownerEmployeeId),
        rmsStatus: null,
      })}
      columns={[
        { label: t('project.risk.title'), render: (r) => r.title ?? '—' },
        { label: t('project.risk.category'), render: (r) => label(options.riskCategories, r.categoryCode) },
        { label: t('project.risk.pxi'), render: (r) => `${r.probability} × ${r.impact}` },
        {
          label: t('project.risk.inherent'),
          render: (r) => (
            <StatusChip tone={bandTone[r.inherentBand] ?? 'neutral'}>
              {formatNumber(r.inherentScore, lang)} · {t(`project.risk.bands.${r.inherentBand as 'low'}`)}
            </StatusChip>
          ),
        },
        {
          label: t('project.risk.residual'),
          render: (r) => (
            <StatusChip tone={bandTone[r.residualBand] ?? 'neutral'}>
              {formatNumber(r.residualScore, lang)} · {t(`project.risk.bands.${r.residualBand as 'low'}`)}
            </StatusChip>
          ),
        },
        {
          label: t('project.risk.response'),
          render: (r) => (r.response ? t(`project.risk.responses.${r.response as 'accept'}`) : '—'),
        },
      ]}
    />
  );
}

export function StakeholdersTab({ project, options }: TabProps) {
  const { t } = useTranslation('portfolio');
  const { opts, label } = useLabels();
  const levels: SelectOption[] = ['low', 'medium', 'high'].map((l) => ({
    value: l,
    label: t(`project.levels.${l as 'low'}`),
  }));
  return (
    <ItemsTab<Stakeholder>
      projectId={project.id}
      kind="stakeholders"
      testId="project-stakeholders"
      title={t('project.tabs.stakeholders')}
      canWrite={project.permissions.canEditContent}
      fields={[
        { name: 'name', label: t('project.stakeholder.name'), kind: 'text', required: true },
        {
          name: 'classificationCode',
          label: t('project.stakeholder.classification'),
          kind: 'select',
          options: opts(options.stakeholderClasses),
        },
        { name: 'interestLevel', label: t('project.stakeholder.interest'), kind: 'select', options: levels },
        { name: 'impactLevel', label: t('project.stakeholder.impact'), kind: 'select', options: levels },
        { name: 'communicationMethod', label: t('project.stakeholder.method'), kind: 'text' },
        { name: 'communicationFrequency', label: t('project.stakeholder.frequency'), kind: 'text' },
        { name: 'email', label: t('project.stakeholder.email'), kind: 'text' },
        { name: 'phone', label: t('project.stakeholder.phone'), kind: 'text' },
      ]}
      empty={{}}
      fromRow={(s) => ({
        name: s.name ?? null,
        classificationCode: s.classificationCode ?? null,
        interestLevel: s.interestLevel ?? null,
        impactLevel: s.impactLevel ?? null,
        communicationMethod: s.communicationMethod ?? null,
        communicationFrequency: s.communicationFrequency ?? null,
        email: s.email ?? null,
        phone: s.phone ?? null,
      })}
      toPayload={(v) => ({
        name: str(v.name),
        classificationCode: str(v.classificationCode),
        interestLevel: str(v.interestLevel),
        impactLevel: str(v.impactLevel),
        communicationMethod: str(v.communicationMethod),
        communicationFrequency: str(v.communicationFrequency),
        email: str(v.email),
        phone: str(v.phone),
      })}
      columns={[
        { label: t('project.stakeholder.name'), render: (s) => s.name ?? '—' },
        {
          label: t('project.stakeholder.classification'),
          render: (s) => label(options.stakeholderClasses, s.classificationCode),
        },
        {
          label: t('project.stakeholder.interest'),
          render: (s) => (s.interestLevel ? t(`project.levels.${s.interestLevel as 'low'}`) : '—'),
        },
        {
          label: t('project.stakeholder.impact'),
          render: (s) => (s.impactLevel ? t(`project.levels.${s.impactLevel as 'low'}`) : '—'),
        },
        { label: t('project.stakeholder.method'), render: (s) => s.communicationMethod ?? '—' },
      ]}
    />
  );
}

export function TeamTab({ project }: TabProps) {
  const { t, i18n } = useTranslation('portfolio');
  return (
    <div className="flex flex-col gap-6">
      <ItemsTab<ProposedMember>
        projectId={project.id}
        kind="members"
        testId="project-members"
        title={t('project.team.proposed')}
        description={t('project.team.proposedHint', { count: project.counts.members })}
        canWrite={project.permissions.canEditContent}
        fields={[
          {
            name: 'employeeId',
            label: t('project.team.employee'),
            kind: 'employee',
            required: true,
            wide: true,
          },
          { name: 'roleAr', label: t('project.team.roleAr'), kind: 'text' },
          { name: 'roleEn', label: t('project.team.roleEn'), kind: 'text' },
          { name: 'responsibilities', label: t('project.team.responsibilities'), kind: 'textarea' },
          { name: 'allocationPercent', label: t('project.team.allocation'), kind: 'number' },
        ]}
        empty={{}}
        fromRow={(m) => ({
          employeeId: m.employee?.id ?? null,
          roleAr: m.roleAr ?? null,
          roleEn: m.roleEn ?? null,
          responsibilities: m.responsibilities ?? null,
          allocationPercent: asText(toPercent(allocationOf(m))),
        })}
        toPayload={(v) => ({
          employeeId: str(v.employeeId),
          roleAr: str(v.roleAr),
          roleEn: str(v.roleEn),
          responsibilities: str(v.responsibilities),
          // Capacity view (ADR 0116): typed as a percentage, stored as a ratio.
          allocationPercent: fromPercent(str(v.allocationPercent) ?? ''),
        })}
        columns={[
          {
            label: t('project.team.employee'),
            render: (m) => (m.employee ? pickText(i18n.language, m.employee.nameAr, m.employee.nameEn) : '—'),
          },
          {
            label: t('project.team.role'),
            render: (m) => pickText(i18n.language, m.roleAr, m.roleEn) || '—',
          },
          { label: t('project.team.responsibilities'), render: (m) => m.responsibilities ?? '—' },
          { label: t('project.team.allocation'), render: (m) => percent(allocationOf(m), i18n.language) },
        ]}
      />
      {project.template.teamFormation && (
        <StatusBanner
          tone={project.teamId ? 'success' : 'info'}
          title={project.teamId ? t('project.team.formed') : t('project.team.formation')}
          description={t('project.team.formationHint')}
        />
      )}
      <LinkedTeamsPanel type="portfolio.project" id={project.id} />
    </div>
  );
}

const planningGroups = {
  scope: ['in-scope', 'out-of-scope'],
  assumptions: ['assumption', 'constraint'],
  values: [
    'added-value',
    'deliverable',
    'resource-human',
    'resource-technical',
    'resource-financial',
    'resource-contract',
    'resource-other',
  ],
} as const;

export function PlanningTab({ project, group }: TabProps & { group: keyof typeof planningGroups }) {
  const { t } = useTranslation('portfolio');
  const categories = planningGroups[group];
  return (
    <ItemsTab<PlanningItem>
      projectId={project.id}
      kind="planning-items"
      testId={`project-planning-${group}`}
      title={t(`project.tabs.${group}`)}
      description={t('project.planning.hint')}
      canWrite={project.permissions.canEditContent}
      filter={(i) => (categories as readonly string[]).includes(i.category)}
      fields={[
        {
          name: 'category',
          label: t('project.planning.category'),
          kind: 'select',
          required: true,
          options: categories.map((c) => ({ value: c, label: t(`project.planning.categories.${c}`) })),
        },
        { name: 'description', label: t('fields.description'), kind: 'textarea', required: true },
      ]}
      empty={{ category: categories[0] }}
      fromRow={(i) => ({ category: i.category, description: i.description ?? null })}
      toPayload={(v) => ({ category: v.category, description: str(v.description) })}
      columns={[
        {
          label: t('project.planning.category'),
          render: (i) => (
            <StatusChip tone={i.category === 'out-of-scope' ? 'warning' : 'info'}>
              {t(`project.planning.categories.${i.category as 'in-scope'}`)}
            </StatusChip>
          ),
        },
        {
          label: t('fields.description'),
          render: (i) => <span className="whitespace-pre-line">{i.description}</span>,
        },
        {
          label: t('project.createdStage'),
          render: (i) => t(`projectStages.${i.createdStage as 'planning'}`),
        },
      ]}
    />
  );
}

export function ChallengesTab({ project }: TabProps) {
  const { t, i18n } = useTranslation('portfolio');
  return (
    <ItemsTab<Challenge>
      projectId={project.id}
      kind="challenges"
      testId="project-challenges"
      title={t('project.tabs.challenges')}
      canWrite={project.permissions.canContribute && project.status === 'active'}
      fields={[
        { name: 'challenge', label: t('project.challenge.challenge'), kind: 'textarea', required: true },
        { name: 'impact', label: t('project.challenge.impact'), kind: 'textarea' },
        { name: 'recommendation', label: t('project.challenge.recommendation'), kind: 'textarea' },
        { name: 'achievedPct', label: t('project.challenge.achieved'), kind: 'number' },
        { name: 'reportDate', label: t('project.challenge.reportDate'), kind: 'date' },
      ]}
      empty={{}}
      fromRow={(c) => ({
        challenge: c.challenge ?? null,
        impact: c.impact ?? null,
        recommendation: c.recommendation ?? null,
        achievedPct: asText(toPercent(c.achievedPct)),
        reportDate: c.reportDate ?? null,
      })}
      toPayload={(v) => ({
        challenge: str(v.challenge),
        impact: str(v.impact),
        recommendation: str(v.recommendation),
        achievedPct: num(v.achievedPct) === null ? null : (num(v.achievedPct) ?? 0) / 100,
        reportDate: str(v.reportDate),
        milestoneId: null,
      })}
      columns={[
        { label: t('project.challenge.challenge'), render: (c) => c.challenge ?? '—' },
        { label: t('project.challenge.impact'), render: (c) => c.impact ?? '—' },
        {
          label: t('project.challenge.achieved'),
          render: (c) =>
            c.achievedPct !== null ? `${formatNumber(toPercent(c.achievedPct) ?? 0, i18n.language)} %` : '—',
        },
        {
          label: t('project.challenge.reportDate'),
          render: (c) => (c.reportDate ? formatDate(c.reportDate, i18n.language) : '—'),
        },
      ]}
    />
  );
}

export function LessonsTab({ project, options }: TabProps) {
  const { t } = useTranslation('portfolio');
  const { opts, label } = useLabels();
  return (
    <ItemsTab<Lesson>
      projectId={project.id}
      kind="lessons"
      testId="project-lessons"
      title={t('project.tabs.lessons')}
      canWrite={project.permissions.canContribute && project.status === 'active'}
      fields={[
        {
          name: 'stage',
          label: t('fields.stage'),
          kind: 'select',
          options: project.stagePath.map((s) => ({ value: s, label: t(`projectStages.${s as 'planning'}`) })),
        },
        {
          name: 'typeCode',
          label: t('project.lesson.type'),
          kind: 'select',
          options: opts(options.lessonTypes),
        },
        { name: 'lesson', label: t('project.lesson.lesson'), kind: 'textarea', required: true },
        { name: 'recommendation', label: t('project.lesson.recommendation'), kind: 'textarea' },
      ]}
      empty={{ stage: project.stage }}
      fromRow={(l) => ({
        stage: l.stage ?? null,
        typeCode: l.typeCode ?? null,
        lesson: l.lesson ?? null,
        recommendation: l.recommendation ?? null,
      })}
      toPayload={(v) => ({
        stage: str(v.stage),
        typeCode: str(v.typeCode),
        lesson: str(v.lesson),
        recommendation: str(v.recommendation),
      })}
      columns={[
        {
          label: t('fields.stage'),
          render: (l) => (l.stage ? t(`projectStages.${l.stage as 'planning'}`) : '—'),
        },
        { label: t('project.lesson.type'), render: (l) => label(options.lessonTypes, l.typeCode) },
        { label: t('project.lesson.lesson'), render: (l) => l.lesson ?? '—' },
        { label: t('project.lesson.recommendation'), render: (l) => l.recommendation ?? '—' },
      ]}
    />
  );
}

export function MeetingsTab({ project }: TabProps) {
  const { t, i18n } = useTranslation('portfolio');
  return (
    <ItemsTab<ProjectMeeting>
      projectId={project.id}
      kind="meetings"
      testId="project-meetings"
      title={t('project.tabs.meetings')}
      canWrite={project.permissions.canContribute && project.status === 'active'}
      fields={[
        { name: 'title', label: t('project.meeting.title'), kind: 'text', required: true },
        { name: 'meetingAt', label: t('project.meeting.at'), kind: 'datetime', required: true },
        { name: 'notes', label: t('project.meeting.notes'), kind: 'richtext' },
      ]}
      empty={{}}
      fromRow={(m) => ({
        title: m.title ?? null,
        meetingAt: m.meetingAt.slice(0, 16),
        notes: m.notes ?? null,
      })}
      toPayload={(v) => ({
        title: str(v.title),
        meetingAt: new Date(String(v.meetingAt)).toISOString(),
        notes: str(v.notes),
      })}
      columns={[
        { label: t('project.meeting.at'), render: (m) => formatDateTime(m.meetingAt, i18n.language) },
        { label: t('project.meeting.title'), render: (m) => m.title ?? '—' },
        // Minutes are rich text: sanitised on the server and again by DOMPurify; legacy plain values render as text (ADR 0161).
        {
          label: t('project.meeting.notes'),
          render: (m) => <RichTextView compact value={m.notes} />,
        },
        {
          label: t('project.meeting.vendor'),
          render: (m) => (
            <MeetingVendorCell
              projectId={project.id}
              meetingId={m.id}
              vendorId={(m as WaselMeetingFields).vendorId}
              confirmedAt={(m as WaselMeetingFields).vendorConfirmedAt}
              canShare={project.permissions.canEdit && project.status === 'active'}
            />
          ),
        },
      ]}
    />
  );
}

/** Wasel fields of a meeting row, when the server sends them (SPEC §1.18 "Meetings"). */
interface WaselMeetingFields {
  vendorId?: string | null;
  vendorConfirmedAt?: string | null;
}

/** Planned allocation of a proposed member (ratio), when the server sends it (capacity view, ADR 0116). */
const allocationOf = (m: object) => (m as { allocationPercent?: number | null }).allocationPercent ?? null;

export function ContractsTab({ project }: TabProps) {
  const { t, i18n } = useTranslation('portfolio');
  const vendorOptions = useVendorOptions();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const files = useProjectFiles(project.id);
  const upload = useProjectMutation(project.id, projectApi.uploadFile);
  const confirm = useProjectMutation(project.id, projectApi.lpoUploaded);
  const [file, setFile] = useState<File | null>(null);
  const canLpo = (project.permissions.canLpo || project.permissions.canEdit) && project.status === 'active';
  const procurement = useFeature('procurement');
  const lpoFiles = (files.data ?? []).filter((f) => f.kind === 'lpo');
  return (
    <div className="flex flex-col gap-6">
      {project.template.lpo && (
        <section className="flex flex-col gap-3 rounded-lg border bg-card p-6" data-testid="project-lpo">
          <h2 className="font-semibold">{t('project.lpo.title')}</h2>
          <StatusChip tone={project.lpoUploaded ? 'success' : 'info'}>
            {project.lpoUploaded ? t('project.lpo.done') : t('project.lpo.waiting')}
          </StatusChip>
          <ul className="flex flex-col gap-2">
            {lpoFiles.map((f) => (
              <li
                key={f.id}
                className="flex items-center justify-between rounded-md border px-3 py-2 text-sm"
              >
                <bdi>{f.name}</bdi>
                <Button size="sm" variant="ghost" asChild>
                  <a href={contentUrl(f.id, undefined, true)} aria-label={f.name}>
                    <Download aria-hidden />
                  </a>
                </Button>
              </li>
            ))}
          </ul>
          {canLpo && !project.lpoUploaded && (
            <div className="flex flex-col gap-2">
              <FileDrop
                label={t('project.lpo.file')}
                file={file}
                onFile={setFile}
                accept=".pdf,.docx,.png,.jpg,.jpeg"
              />
              <div className="flex flex-wrap gap-2">
                <Button
                  variant="outline"
                  disabled={!file}
                  loading={upload.isPending}
                  onClick={() =>
                    file &&
                    upload.mutate(
                      { id: project.id, kind: 'lpo', file },
                      {
                        onSuccess: () => {
                          setFile(null);
                          void files.refetch();
                          toast({ title: t('toast.uploaded') });
                        },
                      },
                    )
                  }
                >
                  <Upload aria-hidden />
                  {t('actions.upload')}
                </Button>
                {project.subState === 'lpo' && (
                  <Button
                    disabled={lpoFiles.length === 0}
                    loading={confirm.isPending}
                    onClick={() =>
                      confirm.mutate(
                        { id: project.id, version: project.version },
                        {
                          onSuccess: () => toast({ title: t('project.lpo.confirmed') }),
                          onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                        },
                      )
                    }
                  >
                    {t('project.lpo.confirm')}
                  </Button>
                )}
              </div>
              {upload.isError && <StatusBanner tone="danger" title={errorMessage(tc, upload.error)} />}
            </div>
          )}
        </section>
      )}
      <ItemsTab<Contract>
        projectId={project.id}
        kind="contracts"
        testId="project-contracts"
        title={t('project.tabs.contracts')}
        canWrite={canLpo}
        fields={[
          { name: 'name', label: t('project.contract.name'), kind: 'text', required: true },
          { name: 'number', label: t('project.contract.number'), kind: 'text' },
          {
            name: 'vendorId',
            label: t('project.contract.vendor'),
            kind: 'select',
            options: [{ value: '*', label: t('project.contract.noVendor') }, ...vendorOptions],
          },
          { name: 'supplier', label: t('project.contract.supplier'), kind: 'text' },
          { name: 'issueDate', label: t('project.contract.issueDate'), kind: 'date' },
          { name: 'endDate', label: t('project.contract.endDate'), kind: 'date' },
          { name: 'amount', label: t('project.contract.amount'), kind: 'number' },
          {
            name: 'contractStatus',
            label: t('project.contract.status'),
            kind: 'select',
            options: ['active', 'closed', 'cancelled'].map((s) => ({
              value: s,
              label: t(`project.contract.statuses.${s as 'active'}`),
            })),
          },
        ]}
        empty={{ contractStatus: 'active' }}
        fromRow={(c) => ({
          name: c.name ?? null,
          number: c.number ?? null,
          supplier: c.supplier ?? null,
          issueDate: c.issueDate ?? null,
          amount: asText(c.amount),
          contractStatus: c.contractStatus,
          vendorId: (c as WaselContractFields).vendorId ?? null,
          endDate: (c as WaselContractFields).endDate ?? null,
        })}
        toPayload={(v) => ({
          name: str(v.name),
          number: str(v.number),
          supplier: str(v.supplier),
          issueDate: str(v.issueDate),
          amount: num(v.amount),
          contractStatus: str(v.contractStatus),
          vendorId: str(v.vendorId) === '*' ? null : str(v.vendorId),
          endDate: str(v.endDate),
        })}
        columns={[
          { label: t('project.contract.name'), render: (c) => c.name ?? '—' },
          { label: t('project.contract.number'), render: (c) => (c.number ? <bdi>{c.number}</bdi> : '—') },
          { label: t('project.contract.supplier'), render: (c) => c.supplier ?? '—' },
          {
            label: t('project.contract.issueDate'),
            render: (c) => (c.issueDate ? formatDate(c.issueDate, i18n.language) : '—'),
          },
          {
            label: t('project.contract.amount'),
            render: (c) => (c.amount !== null ? formatNumber(c.amount, i18n.language) : '—'),
          },
          {
            label: t('project.contract.endDate'),
            render: (c) => {
              const end = (c as WaselContractFields).endDate;
              return end ? formatDate(end, i18n.language) : '—';
            },
          },
          {
            label: t('project.contract.status'),
            render: (c) => t(`project.contract.statuses.${c.contractStatus as 'active'}`),
          },
        ]}
      />
      {procurement && <ContractExtrasSection project={project} canWrite={canLpo} />}
    </div>
  );
}

/** Supplier from the vendor register and end date of a contract, when the server sends them (ADR 0112 / 0115). */
interface WaselContractFields {
  vendorId?: string | null;
  endDate?: string | null;
}
