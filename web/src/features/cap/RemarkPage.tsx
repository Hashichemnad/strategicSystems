import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Ban, Pencil, UserRoundCog } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkBadge } from '@/features/links/LinkBadge';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import {
  Button,
  FormField,
  PageHeader,
  Select,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Section,
} from '@/ui';
import { capApi, remarkStages, useCapOptions, useCapRemark, useKpiTrend, type CapRemark } from './api';
import {
  CapPageLayout,
  PriorityChip,
  RemarkStatusChip,
  Rows,
  SimpleDialog,
  lookupTypes,
  useCapLabels,
  useCapT,
  useDate,
  useDialogMutation,
  useNames,
  usePct,
  useToastMutation,
} from './components';
import { ReasonDialog, RemarkDialog } from './forms';
import { RemarkActionsTab } from './RemarkActionsTab';
import { RootCauseTab } from './RootCauseTab';

const route = getRouteApi('/app/cap/remarks/$remarkId');

export const remarkTabs = ['overview', 'root-cause', 'actions', 'tasks', 'documents', 'history'] as const;
export type RemarkTab = (typeof remarkTabs)[number];

type DialogKind = 'reject' | 'confirm' | 'send-back' | 'submit' | 'verify' | 'reassign' | 'cancel' | 'edit';

/** Remark page (SPEC §9.2–9.6): stage stepper, current status with the next actions, accept / reject, root cause, actions and validation. */
export function CapRemarkPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const { remarkId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const remark = useCapRemark(remarkId);
  const names = useNames();
  const date = useDate();
  const accept = useToastMutation(capApi.accept);
  const [dialog, setDialog] = useState<DialogKind>();

  if (remark.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (remark.isError) return <StatusBanner tone="danger" title={errorMessage(tc, remark.error)} />;
  const r = remark.data;
  const go = (next: RemarkTab) => void navigate({ search: { tab: next } });
  const close = () => setDialog(undefined);

  return (
    <CapPageLayout testId="cap-remark-page">
      <Link to="/cap/remarks" className="flex items-center gap-1 text-sm text-primary hover:underline">
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('remarks.title')}
      </Link>
      <PageHeader
        title={<bdi data-testid="cap-remark-number">{r.number}</bdi>}
        description={
          <span className="line-clamp-3" data-testid="cap-remark-text">
            {names.text(r.values.textAr, r.values.textEn)}
          </span>
        }
        meta={
          <div className="flex flex-wrap items-center gap-2" data-testid="cap-remark-meta">
            <RemarkStatusChip status={r.status} />
            <PriorityChip priority={r.values.priority} />
            {r.overdue && <StatusChip tone="danger">{t('remark.overdue')}</StatusChip>}
            <span className="text-sm text-muted-foreground">
              {t('fields.dueOn')}: {date(r.dueOn)}
            </span>
            <Link
              to="/cap/sources/$sourceId"
              params={{ sourceId: r.sourceId }}
              className="text-sm text-primary hover:underline"
            >
              {t('fields.source')}: <bdi>{r.sourceNumber}</bdi>
            </Link>
            {r.linked && <LinkBadge view={r.linked} />}
            {r.sourceLinked && !r.linked && <LinkBadge view={r.sourceLinked} />}
          </div>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            {r.can.edit && (
              <Button variant="outline" onClick={() => setDialog('edit')} data-testid="cap-remark-edit">
                <Pencil aria-hidden />
                {tc('actions.edit')}
              </Button>
            )}
            {r.can.reassign && (
              <Button
                variant="outline"
                onClick={() => setDialog('reassign')}
                data-testid="cap-remark-reassign"
              >
                <UserRoundCog aria-hidden />
                {t('remark.reassign')}
              </Button>
            )}
            {r.can.cancel && (
              <Button variant="ghost" onClick={() => setDialog('cancel')} data-testid="cap-remark-cancel">
                <Ban aria-hidden />
                {t('remark.cancel')}
              </Button>
            )}
          </div>
        }
      />
      {r.stage !== 'rejected' && r.status !== 'cancelled' && r.status !== 'rejected' && (
        <StageStepper
          stages={remarkStages.map((s) => ({ key: s, label: k(`stage.${s}`) }))}
          current={r.stage}
        />
      )}
      <div data-testid="cap-remark-current-status">
        <CurrentStatusBanner
          resourceType="cap.remark"
          resourceId={r.id}
          onAction={(code) => {
            if (code === 'accept') accept.run(r.id);
            if (code === 'reject') setDialog('reject');
            if (code === 'confirm-rejection') setDialog('confirm');
            if (code === 'send-back') setDialog('send-back');
            if (code === 'submit') setDialog('submit');
            if (code === 'validate') go('actions');
            if (code === 'verify') setDialog('verify');
          }}
        />
      </div>
      {r.rejectionReason && (
        <StatusBanner tone="warning" title={t('remark.rejectionReason')} description={r.rejectionReason} />
      )}
      {r.pmoDecisionNote && (
        <StatusBanner
          tone="info"
          title={k(`pmoDecision.${r.pmoDecision ?? 'sent-back'}`)}
          description={r.pmoDecisionNote}
        />
      )}
      <Tabs value={tab ?? 'overview'} onValueChange={(value) => go(value as RemarkTab)}>
        <TabsList className="flex-wrap" data-testid="cap-remark-tabs">
          {remarkTabs.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`cap-tab-${key}`}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview remark={r} />
        </TabsContent>
        <TabsContent value="root-cause">
          <RootCauseTab remark={r} />
        </TabsContent>
        <TabsContent value="actions">
          <RemarkActionsTab remark={r} onSubmit={() => setDialog('submit')} />
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type="cap.remark" id={r.id} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel
            ownerType="cap.remark"
            ownerId={r.id}
            canEdit={r.can.rootCause || r.can.manageActions || r.can.edit}
          />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="capRemark" id={r.id} />
        </TabsContent>
      </Tabs>
      {dialog === 'edit' && (
        <RemarkDialog remarkId={r.id} initial={r.values} version={r.version} onClose={close} />
      )}
      {dialog === 'reject' && <RejectDialog remark={r} onClose={close} />}
      {dialog === 'confirm' && <ConfirmRejectionDialog remark={r} confirm onClose={close} />}
      {dialog === 'send-back' && <ConfirmRejectionDialog remark={r} confirm={false} onClose={close} />}
      {dialog === 'submit' && <SubmitDialog remark={r} onClose={close} />}
      {dialog === 'verify' && <VerifyDialog remark={r} onClose={close} />}
      {dialog === 'reassign' && <ReassignDialog remark={r} onClose={close} />}
      {dialog === 'cancel' && <CancelDialog remark={r} onClose={close} />}
    </CapPageLayout>
  );
}

function Overview({ remark: r }: { remark: CapRemark }) {
  const { t } = useTranslation('cap');
  const labels = useCapLabels();
  const names = useNames();
  const date = useDate();
  const v = r.values;
  const kpiLinked = (r.linked?.type ?? r.sourceLinked?.type ?? '').startsWith('kpi.');
  return (
    <div className="grid gap-4 xl:grid-cols-[2fr_1fr]" data-testid="cap-remark-overview">
      <Rows
        rows={[
          [t('fields.textAr'), v.textAr],
          [t('fields.textEn'), v.textEn],
          [t('fields.remarkType'), labels.lookup(lookupTypes.remarkType, v.typeCode)],
          [t('fields.criterion'), labels.lookup(lookupTypes.criterion, v.criterionCode)],
          [t('fields.source'), names.text(r.sourceTitleAr, r.sourceTitleEn) || r.sourceNumber],
          [t('fields.sourceType'), labels.lookup(lookupTypes.sourceType, r.sourceTypeCode)],
          [t('fields.department'), names.unit(r.department)],
          [t('fields.section'), names.unit(r.section)],
          [t('fields.owner'), names.person(r.owner)],
          [t('fields.assignedOn'), date(r.assignedOn)],
          [t('fields.dueOn'), date(r.dueOn)],
          [t('fields.completedAt'), date(r.completedAt)],
          ...(r.verificationDueOn
            ? ([[t('fields.verificationDueOn'), date(r.verificationDueOn)]] as [string, string][])
            : []),
          ...(r.cancelReason ? ([[t('fields.cancelReason'), r.cancelReason]] as [string, string][]) : []),
        ]}
      />
      {kpiLinked && <KpiTrend remarkId={r.id} />}
    </div>
  );
}

const ragTone: Record<string, 'danger' | 'warning' | 'success' | 'neutral'> = {
  red: 'danger',
  amber: 'warning',
  green: 'success',
};

function KpiTrend({ remarkId }: { remarkId: string }) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const pct = usePct();
  const trend = useKpiTrend(remarkId, true);
  return (
    <Section title={t('remark.kpiTrend')} testId="cap-kpi-trend">
      {trend.isError ? (
        <p className="text-sm text-muted-foreground">—</p>
      ) : !trend.data ? (
        <Skeleton className="h-24" />
      ) : (
        <div className="flex items-center justify-between gap-2 text-sm">
          <span>
            <bdi>{trend.data.code}</bdi> {names.text(trend.data.nameAr, trend.data.nameEn)} ·{' '}
            {trend.data.year}
            <span className="block text-xs text-muted-foreground">
              {t('remark.kpiPeriods', {
                approved: trend.data.approvedPeriods,
                total: trend.data.totalPeriods,
              })}
            </span>
          </span>
          <StatusChip tone={ragTone[trend.data.rag] ?? 'neutral'}>
            {pct(trend.data.capped ?? trend.data.achieved)}
          </StatusChip>
        </div>
      )}
    </Section>
  );
}

function RejectDialog({ remark, onClose }: { remark: CapRemark; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const options = useCapOptions();
  const m = useDialogMutation(capApi.reject, onClose);
  return (
    <ReasonDialog
      title={t('remark.reject')}
      description={
        options.data?.rejectionNeedsPmoConfirmation ? t('remark.rejectHintPmo') : t('remark.rejectHintFinal')
      }
      label={t('remark.rejectionReason')}
      submitLabel={t('remark.reject')}
      testId="cap-remark-reject-confirm"
      busy={m.busy}
      onClose={onClose}
      onSubmit={(reason) => m.run({ id: remark.id, reason })}
    />
  );
}

function ConfirmRejectionDialog({
  remark,
  confirm,
  onClose,
}: {
  remark: CapRemark;
  confirm: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation('cap');
  const m = useDialogMutation(capApi.confirmRejection, onClose);
  return (
    <ReasonDialog
      title={confirm ? t('remark.confirmRejection') : t('remark.sendBack')}
      description={confirm ? t('remark.confirmRejectionHint') : t('remark.sendBackHint')}
      label={t('common.note')}
      submitLabel={confirm ? t('remark.confirmRejection') : t('remark.sendBack')}
      testId={confirm ? 'cap-confirm-rejection' : 'cap-send-back'}
      required={!confirm}
      busy={m.busy}
      onClose={onClose}
      onSubmit={(note) => m.run({ id: remark.id, confirm, note: note || null })}
    />
  );
}

function SubmitDialog({ remark, onClose }: { remark: CapRemark; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const [comment, setComment] = useState('');
  const m = useDialogMutation(capApi.submit, onClose);
  return (
    <SimpleDialog
      title={t('remark.submit')}
      description={t('remark.submitHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('remark.submit')}
      testId="cap-remark-submit-confirm"
      onSubmit={() => m.run({ id: remark.id, comment: comment || null })}
    >
      <FormField label={t('common.comment')}>
        <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function VerifyDialog({ remark, onClose }: { remark: CapRemark; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const [effective, setEffective] = useState<string>();
  const [comment, setComment] = useState('');
  const m = useDialogMutation(capApi.verify, onClose);
  return (
    <SimpleDialog
      title={t('remark.verify')}
      description={t('remark.verifyHint')}
      onClose={onClose}
      busy={m.busy}
      disabled={!effective}
      submitLabel={t('remark.verify')}
      testId="cap-remark-verify-confirm"
      onSubmit={() => m.run({ id: remark.id, effective: effective === 'yes', comment: comment || null })}
    >
      <FormField label={t('remark.effective')} required>
        <Select
          value={effective}
          onValueChange={setEffective}
          options={[
            { value: 'yes', label: t('remark.effectiveYes') },
            { value: 'no', label: t('remark.effectiveNo') },
          ]}
        />
      </FormField>
      <FormField label={t('common.comment')}>
        <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function ReassignDialog({ remark, onClose }: { remark: CapRemark; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const [owner, setOwner] = useState<string>();
  const m = useDialogMutation(capApi.reassign, onClose);
  return (
    <SimpleDialog
      title={t('remark.reassign')}
      description={t('remark.reassignHint')}
      onClose={onClose}
      busy={m.busy}
      disabled={!owner}
      submitLabel={t('remark.reassign')}
      testId="cap-remark-reassign-confirm"
      onSubmit={() => owner && m.run({ id: remark.id, ownerEmployeeId: owner })}
    >
      <FormField label={t('fields.owner')} required>
        <EmployeePicker value={owner} onChange={setOwner} excludeId={remark.owner?.id} />
      </FormField>
    </SimpleDialog>
  );
}

function CancelDialog({ remark, onClose }: { remark: CapRemark; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const m = useDialogMutation(capApi.cancelRemark, onClose);
  return (
    <ReasonDialog
      title={t('remark.cancel')}
      label={t('fields.cancelReason')}
      submitLabel={t('remark.cancel')}
      testId="cap-remark-cancel-confirm"
      busy={m.busy}
      onClose={onClose}
      onSubmit={(reason) => m.run({ id: remark.id, reason })}
    />
  );
}
