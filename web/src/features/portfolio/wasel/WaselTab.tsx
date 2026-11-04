import { Link } from '@tanstack/react-router';
import { MessageSquare, Plus, Star, UserPlus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { contentUrl } from '@/features/files/api';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
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
  useToast,
} from '@/ui';
import { DatePicker } from '@/ui/date-picker';
import { type Contract, type ProjectFull, useProjectItems } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { CommentsDialog, type CommentSubject } from './CommentsDialog';
import { ProposalReviewDialog } from './ProposalReviewDialog';
import { SubmissionReview } from './SubmissionReview';
import {
  expiryRules,
  type PmDeliverable,
  type PmVendorItem,
  updateModes,
  useVendors,
  usePanelMutation,
  useWaselPanel,
  vendorRoles,
  type WaselAssignment,
  type WaselPanel,
  waselApi,
} from './waselApi';
import { percent } from './waselFormat';

export const statusTone: Record<string, Tone> = {
  active: 'success',
  invited: 'info',
  submitted: 'info',
  proposed: 'info',
  pending: 'info',
  accepted: 'success',
  edited: 'warning',
  included: 'success',
  returned: 'warning',
  rejected: 'danger',
  revoked: 'danger',
  ended: 'neutral',
  expired: 'neutral',
  disabled: 'danger',
  dormant: 'warning',
  suspended: 'warning',
  blacklisted: 'danger',
  withdrawn: 'neutral',
  draft: 'neutral',
};

/** Project tab "Vendors (Wasel)" (docs/design/portfolio.md §62): assignments, invitations, reviews, deliverables, drafts, rating. */
export function WaselTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const panel = useWaselPanel(project.id);
  const [comments, setComments] = useState<CommentSubject | null>(null);
  if (panel.isPending) return <Skeleton className="h-48" />;
  if (panel.isError) return <StatusBanner tone="danger" title={errorMessage(tc, panel.error)} />;
  const p = panel.data;
  const names = new Map(
    p.assignments.map((a) => [a.vendorId, pickText(i18n.language, a.vendorNameAr, a.vendorNameEn)]),
  );
  const openComments = (subject: CommentSubject) => setComments(subject);
  return (
    <section className="flex flex-col gap-6" data-testid="wasel-panel">
      <ModeSection project={project} panel={p} />
      <AssignmentsSection project={project} panel={p} />
      <MilestoneVendorsSection project={project} panel={p} names={names} onComments={openComments} />
      <section className="flex flex-col gap-3" aria-labelledby="wasel-proposals">
        <h2 id="wasel-proposals" className="font-semibold">
          {t('proposal.title')}
        </h2>
        {p.proposals.length === 0 ? (
          <EmptyState title={t('proposal.empty')} />
        ) : (
          p.proposals.map((proposal) => (
            <ProposalReviewDialog
              key={proposal.id}
              project={project}
              proposal={proposal}
              canManage={p.canManage}
              onComments={() =>
                openComments({
                  type: 'proposal',
                  id: proposal.id,
                  label: t('proposal.number', { n: proposal.sequence }),
                })
              }
            />
          ))
        )}
      </section>
      <section className="flex flex-col gap-3" aria-labelledby="wasel-submissions">
        <h2 id="wasel-submissions" className="font-semibold">
          {t('submission.title')}
        </h2>
        {p.submissions.length === 0 ? (
          <EmptyState title={t('submission.empty')} />
        ) : (
          p.submissions.map((submission) => (
            <SubmissionReview
              key={submission.id}
              project={project}
              submission={submission}
              canManage={p.canManage}
              onComments={() =>
                openComments({
                  type: 'submission',
                  id: submission.id,
                  label: t('submission.period', { period: submission.period }),
                })
              }
            />
          ))
        )}
      </section>
      <DeliverablesSection project={project} panel={p} names={names} onComments={openComments} />
      <ItemsSection project={project} panel={p} names={names} />
      <RatingsSection project={project} panel={p} />
      {comments && (
        <CommentsDialog
          projectId={project.id}
          subject={comments}
          canWrite={p.canManage}
          onClose={() => setComments(null)}
        />
      )}
    </section>
  );
}

function ModeSection({ project, panel }: { project: ProjectFull; panel: WaselPanel }) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const setMode = usePanelMutation(project.id, waselApi.setMode);
  return (
    <header className="flex flex-wrap items-end justify-between gap-3 rounded-lg border bg-card p-4">
      <div>
        <h2 className="font-semibold">{t('panel.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('panel.hint')}</p>
      </div>
      <FormField label={t('panel.mode')} hint={t('panel.modeHint')}>
        <div data-testid="wasel-mode">
          <Select
            value={panel.updateMode}
            disabled={!panel.canManage || setMode.isPending}
            onValueChange={(mode) =>
              setMode.mutate(
                { projectId: project.id, mode, version: project.version },
                {
                  onSuccess: () => toast({ title: t('panel.modeChanged') }),
                  onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                },
              )
            }
            options={updateModes.map((m) => ({ value: m, label: t(`modes.${m}`) }))}
          />
        </div>
      </FormField>
    </header>
  );
}

function AssignmentsSection({ project, panel }: { project: ProjectFull; panel: WaselPanel }) {
  const { t, i18n } = useTranslation('vendors');
  const [assigning, setAssigning] = useState(false);
  const [inviting, setInviting] = useState<WaselAssignment | null>(null);
  const [revoking, setRevoking] = useState<{ kind: 'member' | 'assignment'; id: string } | null>(null);
  const lang = i18n.language;
  return (
    <section className="flex flex-col gap-3" aria-labelledby="wasel-assignments">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="wasel-assignments" className="font-semibold">
          {t('panel.assignments')}
        </h2>
        {panel.canManage && (
          <Button onClick={() => setAssigning(true)} data-testid="wasel-assign">
            <Plus aria-hidden />
            {t('panel.assign')}
          </Button>
        )}
      </div>
      {panel.assignments.length === 0 ? (
        <EmptyState title={t('panel.noAssignments')} />
      ) : (
        panel.assignments.map((a) => (
          <article key={a.id} className="rounded-lg border bg-card p-4" data-testid="wasel-assignment">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex flex-wrap items-center gap-2">
                <Link
                  to="/portfolio/vendors/$vendorId"
                  params={{ vendorId: a.vendorId }}
                  className="font-medium text-primary hover:underline"
                >
                  {a.vendorNumber} · {pickText(lang, a.vendorNameAr, a.vendorNameEn)}
                </Link>
                <StatusChip tone={statusTone[a.status] ?? 'neutral'}>
                  {t(`assignmentStatus.${a.status}`, a.status)}
                </StatusChip>
                {a.vendorStatus !== 'active' && (
                  <StatusChip tone={statusTone[a.vendorStatus] ?? 'warning'}>
                    {t(`status.${a.vendorStatus}`, a.vendorStatus)}
                  </StatusChip>
                )}
              </div>
              <p className="text-sm text-muted-foreground">
                {t('panel.accessUntil')}:{' '}
                {a.accessUntil ? formatDate(a.accessUntil, lang) : t('panel.untilClosure')} ·{' '}
                {t(`expiry.${a.expiryRule}`, a.expiryRule)}
              </p>
              {panel.canManage && a.status === 'active' && (
                <div className="flex gap-2">
                  <Button size="sm" onClick={() => setInviting(a)} data-testid="wasel-invite">
                    <UserPlus aria-hidden />
                    {t('panel.invite')}
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => setRevoking({ kind: 'assignment', id: a.id })}
                  >
                    {t('panel.revokeAssignment')}
                  </Button>
                </div>
              )}
            </div>
            {a.members.length > 0 && (
              <div className="mt-3 overflow-x-auto">
                <Table>
                  <TableHead>
                    <TableRow>
                      <TableHeaderCell>{t('panel.email')}</TableHeaderCell>
                      <TableHeaderCell>{t('panel.role')}</TableHeaderCell>
                      <TableHeaderCell>{t('panel.memberStatus')}</TableHeaderCell>
                      <TableHeaderCell>{t('panel.validTo')}</TableHeaderCell>
                      <TableHeaderCell>{t('detail.lastSignIn')}</TableHeaderCell>
                      <TableHeaderCell>
                        <span className="sr-only">{t('panel.actions')}</span>
                      </TableHeaderCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {a.members.map((m) => (
                      <TableRow key={m.id} data-testid="wasel-member">
                        <TableCell>
                          <span dir="ltr">{m.email}</span>
                          {m.name && <span className="block text-xs text-muted-foreground">{m.name}</span>}
                        </TableCell>
                        <TableCell>{t(`roles.${m.role}`, m.role)}</TableCell>
                        <TableCell>
                          <StatusChip tone={statusTone[m.status] ?? 'neutral'}>
                            {t(`memberStatus.${m.status}`, m.status)}
                          </StatusChip>{' '}
                          <StatusChip tone={statusTone[m.userStatus] ?? 'neutral'}>
                            {t(`userStatus.${m.userStatus}`, m.userStatus)}
                          </StatusChip>
                        </TableCell>
                        <TableCell>{m.validTo ? formatDateTime(m.validTo, lang) : '—'}</TableCell>
                        <TableCell>
                          {m.lastSignInAt ? formatDateTime(m.lastSignInAt, lang) : t('detail.never')}
                        </TableCell>
                        <TableCell>
                          {panel.canManage && m.status === 'active' && (
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => setRevoking({ kind: 'member', id: m.id })}
                            >
                              {t('panel.revoke')}
                            </Button>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}
          </article>
        ))
      )}
      {assigning && <AssignDialog project={project} onClose={() => setAssigning(false)} />}
      {inviting && <InviteDialog project={project} assignment={inviting} onClose={() => setInviting(null)} />}
      {revoking && <RevokeDialog project={project} target={revoking} onClose={() => setRevoking(null)} />}
    </section>
  );
}

function AssignDialog({ project, onClose }: { project: ProjectFull; onClose: () => void }) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const vendors = useVendors('', 'active');
  const contracts = useProjectItems<Contract>(project.id, 'contracts');
  const assign = usePanelMutation(project.id, waselApi.assign);
  const [v, setV] = useState({ vendorId: '', contractId: '', accessUntil: '', expiryRule: 'date' });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('panel.assign')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!v.vendorId}
              loading={assign.isPending}
              data-testid="wasel-assign-save"
              onClick={() =>
                assign.mutate(
                  {
                    projectId: project.id,
                    vendorId: v.vendorId,
                    contractId: v.contractId || null,
                    accessUntil: v.accessUntil || null,
                    expiryRule: v.expiryRule,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('panel.assigned') });
                      onClose();
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="wasel-assign-dialog">
          <FormField label={t('panel.vendor')} required>
            <Select
              value={v.vendorId || undefined}
              onValueChange={(vendorId) => set({ vendorId })}
              placeholder={t('panel.pickVendor')}
              options={(vendors.data ?? []).map((x) => ({
                value: x.id,
                label: `${x.number} · ${pickText(i18n.language, x.nameAr, x.nameEn)}`,
              }))}
            />
          </FormField>
          <FormField label={t('panel.expiryRule')} hint={t('panel.expiryHint')}>
            <Select
              value={v.expiryRule}
              onValueChange={(expiryRule) => set({ expiryRule })}
              options={expiryRules.map((r) => ({ value: r, label: t(`expiry.${r}`) }))}
            />
          </FormField>
          <FormField label={t('panel.contract')}>
            <Select
              value={v.contractId || '*'}
              onValueChange={(id) => set({ contractId: id === '*' ? '' : id })}
              options={[
                { value: '*', label: t('panel.noContract') },
                ...(contracts.data ?? [])
                  .filter((c) => c.status === 'active')
                  .map((c) => ({
                    value: c.id,
                    label: [c.number, c.name].filter(Boolean).join(' · ') || c.id,
                  })),
              ]}
            />
          </FormField>
          <FormField label={t('panel.accessUntil')} hint={t('panel.accessUntilHint')}>
            <DatePicker value={v.accessUntil || undefined} onChange={(d) => set({ accessUntil: d ?? '' })} />
          </FormField>
          {assign.error && <FieldErrors error={assign.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function InviteDialog({
  project,
  assignment,
  onClose,
}: {
  project: ProjectFull;
  assignment: WaselAssignment;
  onClose: () => void;
}) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const invite = usePanelMutation(project.id, waselApi.invite);
  const [v, setV] = useState({ email: '', name: '', role: 'vendor-lead', language: 'ar' });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('panel.invite')}
        description={t('panel.inviteHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={v.email.trim() === ''}
              loading={invite.isPending}
              data-testid="wasel-invite-send"
              onClick={() =>
                invite.mutate(
                  {
                    projectId: project.id,
                    vendorId: assignment.vendorId,
                    email: v.email.trim(),
                    name: v.name.trim() || null,
                    role: v.role,
                    language: v.language,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('panel.invited') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('panel.send')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="wasel-invite-dialog">
          <FormField label={t('panel.email')} required>
            <Input
              type="email"
              dir="ltr"
              value={v.email}
              onChange={(e) => set({ email: e.target.value })}
              data-testid="wasel-invite-email"
            />
          </FormField>
          <FormField label={t('panel.name')}>
            <Input dir="auto" value={v.name} onChange={(e) => set({ name: e.target.value })} />
          </FormField>
          <FormField label={t('panel.role')} hint={t('panel.roleHint')}>
            <Select
              value={v.role}
              onValueChange={(role) => set({ role })}
              options={vendorRoles.map((r) => ({ value: r, label: t(`roles.${r}`) }))}
            />
          </FormField>
          <FormField label={t('panel.language')}>
            <Select
              value={v.language}
              onValueChange={(language) => set({ language })}
              options={[
                { value: 'ar', label: 'العربية' },
                { value: 'en', label: 'English' },
              ]}
            />
          </FormField>
          {invite.error && <FieldErrors error={invite.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function RevokeDialog({
  project,
  target,
  onClose,
}: {
  project: ProjectFull;
  target: { kind: 'member' | 'assignment'; id: string };
  onClose: () => void;
}) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const member = usePanelMutation(project.id, waselApi.revokeMember);
  const assignment = usePanelMutation(project.id, waselApi.revokeAssignment);
  const [reason, setReason] = useState('');
  const error = member.error ?? assignment.error;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={target.kind === 'member' ? t('panel.revoke') : t('panel.revokeAssignment')}
        description={t('panel.revokeHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={reason.trim() === ''}
              loading={member.isPending || assignment.isPending}
              onClick={() =>
                target.kind === 'member'
                  ? member.mutate({ memberId: target.id, reason: reason.trim() }, { onSuccess: onClose })
                  : assignment.mutate(
                      { assignmentId: target.id, reason: reason.trim() },
                      { onSuccess: onClose },
                    )
              }
            >
              {t('panel.revoke')}
            </Button>
          </>
        }
      >
        <FormField label={t('panel.reason')} required>
          <Textarea dir="auto" value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {error && <FieldErrors error={error} />}
      </DialogContent>
    </Dialog>
  );
}

function MilestoneVendorsSection({
  project,
  panel,
  names,
  onComments,
}: {
  project: ProjectFull;
  panel: WaselPanel;
  names: Map<string, string>;
  onComments: (s: CommentSubject) => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const assign = usePanelMutation(project.id, waselApi.assignMilestone);
  const vendors = panel.assignments.filter((a) => a.status === 'active');
  const leaves = panel.milestones.filter((m) => !m.isHeader);
  if (leaves.length === 0) return null;
  return (
    <section className="flex flex-col gap-3" aria-labelledby="wasel-milestones">
      <div>
        <h2 id="wasel-milestones" className="font-semibold">
          {t('panel.milestones')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('panel.milestonesHint')}</p>
      </div>
      <div className="overflow-x-auto rounded-lg border bg-card">
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('panel.milestone')}</TableHeaderCell>
              <TableHeaderCell>{t('panel.weight')}</TableHeaderCell>
              <TableHeaderCell>{t('panel.vendor')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('comments.title')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {leaves.map((m) => (
              <TableRow key={m.id} data-testid="wasel-milestone-row">
                <TableCell>
                  <span className="text-xs text-muted-foreground">{m.code}</span>{' '}
                  <span dir="auto">{pickText(i18n.language, m.titleAr, m.titleEn)}</span>
                </TableCell>
                <TableCell>{percent(m.weight, i18n.language)}</TableCell>
                <TableCell>
                  {panel.canManage ? (
                    <Select
                      value={m.vendorId ?? '*'}
                      disabled={assign.isPending}
                      onValueChange={(id) =>
                        assign.mutate(
                          { milestoneId: m.id, vendorId: id === '*' ? null : id },
                          { onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) },
                        )
                      }
                      options={[
                        { value: '*', label: t('panel.pmOnly') },
                        ...vendors.map((a) => ({
                          value: a.vendorId,
                          label: names.get(a.vendorId) ?? a.vendorNumber,
                        })),
                      ]}
                    />
                  ) : m.vendorId ? (
                    (names.get(m.vendorId) ?? '—')
                  ) : (
                    t('panel.pmOnly')
                  )}
                </TableCell>
                <TableCell>
                  {m.vendorId && (
                    <Button
                      size="sm"
                      variant="ghost"
                      aria-label={t('comments.open')}
                      onClick={() =>
                        onComments({
                          type: 'milestone',
                          id: m.id,
                          label: `${m.code} · ${pickText(i18n.language, m.titleAr, m.titleEn)}`,
                        })
                      }
                    >
                      <MessageSquare aria-hidden />
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </section>
  );
}

function DeliverablesSection({
  project,
  panel,
  names,
  onComments,
}: {
  project: ProjectFull;
  panel: WaselPanel;
  names: Map<string, string>;
  onComments: (s: CommentSubject) => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const [deciding, setDeciding] = useState<{ row: PmDeliverable; accept: boolean } | null>(null);
  return (
    <section className="flex flex-col gap-3" aria-labelledby="wasel-deliverables">
      <h2 id="wasel-deliverables" className="font-semibold">
        {t('deliverables.title')}
      </h2>
      {panel.deliverables.length === 0 ? (
        <EmptyState title={t('deliverables.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('deliverables.deliverable')}</TableHeaderCell>
                <TableHeaderCell>{t('panel.vendor')}</TableHeaderCell>
                <TableHeaderCell>{t('deliverables.files')}</TableHeaderCell>
                <TableHeaderCell>{t('panel.memberStatus')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{t('panel.actions')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {panel.deliverables.map((d) => (
                <TableRow key={d.id} data-testid="wasel-deliverable">
                  <TableCell>
                    <span dir="auto" className="font-medium">
                      {d.title}
                    </span>
                    {d.description && (
                      <span className="block text-xs text-muted-foreground">{d.description}</span>
                    )}
                    {d.pmComment && <span className="block text-xs">{d.pmComment}</span>}
                  </TableCell>
                  <TableCell>
                    {names.get(d.vendorId) ?? pickText(i18n.language, d.vendorNameAr, d.vendorNameEn)}
                  </TableCell>
                  <TableCell>
                    <EvidenceLinks files={d.files} />
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={statusTone[d.status] ?? 'neutral'}>
                      {t(`itemStatus.${d.status}`, d.status)}
                    </StatusChip>
                  </TableCell>
                  <TableCell className="whitespace-nowrap">
                    {panel.canManage && d.status === 'submitted' && (
                      <>
                        <Button
                          size="sm"
                          onClick={() => setDeciding({ row: d, accept: true })}
                          data-testid="wasel-deliverable-accept"
                        >
                          {t('actions.accept')}
                        </Button>{' '}
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => setDeciding({ row: d, accept: false })}
                        >
                          {t('actions.return')}
                        </Button>
                      </>
                    )}
                    <Button
                      size="sm"
                      variant="ghost"
                      aria-label={t('comments.open')}
                      onClick={() => onComments({ type: 'deliverable', id: d.id, label: d.title })}
                    >
                      <MessageSquare aria-hidden />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {deciding && (
        <DecisionDialog
          project={project}
          title={deciding.row.title}
          accept={deciding.accept}
          onConfirm={(comment) =>
            waselApi.decideDeliverable({
              id: deciding.row.id,
              accept: deciding.accept,
              comment,
              version: deciding.row.version,
            })
          }
          onClose={() => setDeciding(null)}
        />
      )}
    </section>
  );
}

function ItemsSection({
  project,
  panel,
  names,
}: {
  project: ProjectFull;
  panel: WaselPanel;
  names: Map<string, string>;
}) {
  const { t } = useTranslation('vendors');
  const [deciding, setDeciding] = useState<{ row: PmVendorItem; accept: boolean } | null>(null);
  return (
    <section className="flex flex-col gap-3" aria-labelledby="wasel-items">
      <div>
        <h2 id="wasel-items" className="font-semibold">
          {t('items.title')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('items.hint')}</p>
      </div>
      {panel.items.length === 0 ? (
        <EmptyState title={t('items.empty')} />
      ) : (
        <ul className="flex flex-col gap-2">
          {panel.items.map((item) => (
            <li
              key={item.id}
              className="flex flex-wrap items-start justify-between gap-2 rounded-lg border bg-card p-3"
              data-testid="wasel-item"
            >
              <div>
                <p className="flex flex-wrap items-center gap-2">
                  <StatusChip tone="info">{t(`items.kinds.${item.kind}`, item.kind)}</StatusChip>
                  <span dir="auto" className="font-medium">
                    {item.title}
                  </span>
                  <StatusChip tone={statusTone[item.status] ?? 'neutral'}>
                    {t(`itemStatus.${item.status}`, item.status)}
                  </StatusChip>
                </p>
                {item.description && (
                  <p dir="auto" className="text-sm text-muted-foreground">
                    {item.description}
                  </p>
                )}
                {item.kind === 'risk' && item.probability && item.impactLevel && (
                  <p className="text-xs text-muted-foreground">
                    {t('items.score', { probability: item.probability, impact: item.impactLevel })}
                  </p>
                )}
                <p className="text-xs text-muted-foreground">{names.get(item.vendorId)}</p>
              </div>
              {panel.canManage && item.status === 'proposed' && (
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    onClick={() => setDeciding({ row: item, accept: true })}
                    data-testid="wasel-item-accept"
                  >
                    {t('actions.accept')}
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => setDeciding({ row: item, accept: false })}
                  >
                    {t('actions.reject')}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {deciding && (
        <DecisionDialog
          project={project}
          title={deciding.row.title}
          accept={deciding.accept}
          rejectLabel={t('actions.reject')}
          onConfirm={(comment) =>
            waselApi.decideItem({
              id: deciding.row.id,
              accept: deciding.accept,
              comment,
              version: deciding.row.version,
            })
          }
          onClose={() => setDeciding(null)}
        />
      )}
    </section>
  );
}

/** Accept (comment optional) or return / reject (comment required) — shared by deliverables and vendor drafts. */
function DecisionDialog({
  project,
  title,
  accept,
  rejectLabel,
  onConfirm,
  onClose,
}: {
  project: ProjectFull;
  title: string;
  accept: boolean;
  rejectLabel?: string;
  onConfirm: (comment: string | null) => Promise<WaselPanel>;
  onClose: () => void;
}) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const decide = usePanelMutation(project.id, onConfirm);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={title}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={accept ? 'primary' : 'danger'}
              disabled={!accept && comment.trim() === ''}
              loading={decide.isPending}
              data-testid="wasel-decision-confirm"
              onClick={() => decide.mutate(comment.trim() || null, { onSuccess: onClose })}
            >
              {accept ? t('actions.accept') : (rejectLabel ?? t('actions.return'))}
            </Button>
          </>
        }
      >
        <FormField label={t('panel.comment')} required={!accept}>
          <Textarea dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
        </FormField>
        {decide.error && <FieldErrors error={decide.error} />}
      </DialogContent>
    </Dialog>
  );
}

function RatingsSection({ project, panel }: { project: ProjectFull; panel: WaselPanel }) {
  const { t, i18n } = useTranslation('vendors');
  const [rating, setRating] = useState(false);
  const lang = i18n.language;
  const closing = ['closure', 'impact', 'completed'].includes(project.stage);
  return (
    <section className="flex flex-col gap-3" aria-labelledby="wasel-ratings">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 id="wasel-ratings" className="font-semibold">
            {t('rating.title')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('rating.hint')}</p>
        </div>
        {panel.canManage && panel.assignments.length > 0 && (
          <Button variant="outline" onClick={() => setRating(true)} data-testid="wasel-rate">
            <Star aria-hidden />
            {t('rating.rate')}
          </Button>
        )}
      </div>
      {panel.ratings.length === 0 ? (
        <EmptyState title={t('detail.noRatings')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <RatingsTable ratings={panel.ratings} lang={lang} showProject={false} />
        </div>
      )}
      {rating && (
        <RatingDialog project={project} panel={panel} closing={closing} onClose={() => setRating(false)} />
      )}
    </section>
  );
}

export function RatingsTable({
  ratings,
  lang,
  showProject,
}: {
  ratings: WaselPanel['ratings'];
  lang: string;
  showProject: boolean;
}) {
  const { t } = useTranslation('vendors');
  return (
    <Table>
      <TableHead>
        <TableRow>
          {showProject && <TableHeaderCell>{t('detail.project')}</TableHeaderCell>}
          <TableHeaderCell>{t('rating.timeliness')}</TableHeaderCell>
          <TableHeaderCell>{t('rating.quality')}</TableHeaderCell>
          <TableHeaderCell>{t('rating.responsiveness')}</TableHeaderCell>
          <TableHeaderCell>{t('rating.reporting')}</TableHeaderCell>
          <TableHeaderCell>{t('rating.overall')}</TableHeaderCell>
          <TableHeaderCell>{t('panel.comment')}</TableHeaderCell>
          <TableHeaderCell>{t('rating.ratedAt')}</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {ratings.map((r) => (
          <TableRow key={r.id} data-testid="vendor-rating-row">
            {showProject && (
              <TableCell>
                {r.projectNumber} · {pickText(lang, r.projectNameAr, r.projectNameEn)}
              </TableCell>
            )}
            <TableCell>{r.timeliness}</TableCell>
            <TableCell>{r.quality}</TableCell>
            <TableCell>{r.responsiveness}</TableCell>
            <TableCell>{r.reporting}</TableCell>
            <TableCell className="font-medium">
              {new Intl.NumberFormat(lang, { maximumFractionDigits: 2 }).format(r.overall)}
            </TableCell>
            <TableCell dir="auto">
              {r.comment}
              {r.milestoneId && (
                <span className="block text-xs text-muted-foreground">{t('rating.perMilestone')}</span>
              )}
            </TableCell>
            <TableCell>{formatDate(r.ratedAt, lang)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

const criteria = ['timeliness', 'quality', 'responsiveness', 'reporting'] as const;

function RatingDialog({
  project,
  panel,
  closing,
  onClose,
}: {
  project: ProjectFull;
  panel: WaselPanel;
  closing: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const rate = usePanelMutation(project.id, waselApi.rate);
  const [vendorId, setVendorId] = useState(panel.assignments[0]?.vendorId ?? '');
  const [milestoneId, setMilestoneId] = useState(
    closing ? '' : (panel.milestones.find((m) => !m.isHeader)?.id ?? ''),
  );
  const [scores, setScores] = useState<Record<(typeof criteria)[number], number>>({
    timeliness: 4,
    quality: 4,
    responsiveness: 4,
    reporting: 4,
  });
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('rating.rate')}
        description={closing ? t('rating.closureHint') : t('rating.milestoneHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!vendorId || (!closing && !milestoneId)}
              loading={rate.isPending}
              data-testid="wasel-rate-save"
              onClick={() =>
                rate.mutate(
                  {
                    projectId: project.id,
                    vendorId,
                    milestoneId: milestoneId || null,
                    values: { ...scores, comment: comment.trim() || null },
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('rating.saved') });
                      onClose();
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="wasel-rate-dialog">
          <FormField label={t('panel.vendor')} required>
            <Select
              value={vendorId || undefined}
              onValueChange={setVendorId}
              options={panel.assignments.map((a) => ({
                value: a.vendorId,
                label: `${a.vendorNumber} · ${pickText(i18n.language, a.vendorNameAr, a.vendorNameEn)}`,
              }))}
            />
          </FormField>
          <FormField label={t('panel.milestone')} hint={t('rating.scopeHint')}>
            <Select
              value={milestoneId || '*'}
              onValueChange={(id) => setMilestoneId(id === '*' ? '' : id)}
              options={[
                ...(closing ? [{ value: '*', label: t('rating.wholeProject') }] : []),
                ...panel.milestones
                  .filter((m) => !m.isHeader && m.vendorId === vendorId)
                  .map((m) => ({
                    value: m.id,
                    label: `${m.code} · ${pickText(i18n.language, m.titleAr, m.titleEn)}`,
                  })),
              ]}
            />
          </FormField>
          {criteria.map((c) => (
            <FormField key={c} label={t(`rating.${c}`)} required>
              <div className="flex gap-1" role="radiogroup" aria-label={t(`rating.${c}`)}>
                {[1, 2, 3, 4, 5].map((n) => (
                  <Button
                    key={n}
                    size="sm"
                    variant={scores[c] === n ? 'primary' : 'outline'}
                    role="radio"
                    aria-checked={scores[c] === n}
                    data-testid={`wasel-rate-${c}-${n}`}
                    onClick={() => setScores((s) => ({ ...s, [c]: n }))}
                  >
                    {n}
                  </Button>
                ))}
              </div>
            </FormField>
          ))}
          <FormField label={t('panel.comment')}>
            <Textarea dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {rate.error && <FieldErrors error={rate.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Links to files the PM may read (the generic file content endpoint checks access and audits the download). */
export function EvidenceLinks({ files }: { files: { id: string; name: string; scanStatus: string }[] }) {
  const { t } = useTranslation('vendors');
  if (files.length === 0) return <span className="text-muted-foreground">—</span>;
  return (
    <ul className="flex flex-col gap-1">
      {files.map((f) => (
        <li key={f.id}>
          <a className="text-primary hover:underline" href={contentUrl(f.id, undefined, true)} dir="auto">
            {f.name}
          </a>
          {f.scanStatus !== 'clean' && f.scanStatus !== 'not-scanned' && (
            <StatusChip tone="warning" className="ms-1">
              {t('submission.scan', { status: f.scanStatus })}
            </StatusChip>
          )}
        </li>
      ))}
    </ul>
  );
}
