import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  ConfirmDialog,
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
  useToast,
} from '@/ui';
import {
  downloadOutageReport,
  useOutageConflicts,
  useOutageReport,
  useStepUpMethods,
  useSyncMutation,
  useSyncStatus,
  type OutageRecordReport,
  type SyncOutage,
} from './api';
import { Code, Fact, useSyncT, type DynamicT } from './shared';

/** Failover (ADR 0207): make this system primary, the outage, its changes report, return to standby, conflicts, close. */
export function FailoverTab({ connectionId }: { connectionId: string }) {
  const { t, tx } = useSyncT();
  const status = useSyncStatus(connectionId);
  const [making, setMaking] = useState(false);
  if (status.isPending) return <Skeleton className="mt-4 h-48 rounded-lg" />;
  if (!status.data) return null;
  const { connection, outage } = status.data;
  const actions = connection.modeActions;
  return (
    <div className="flex flex-col gap-4 pt-4">
      <StatusBanner
        tone={
          connection.mode === 'primary' ? 'danger' : connection.mode === 'reconciling' ? 'warning' : 'info'
        }
        title={tx(`sync.failover.mode.${connection.mode}`, { defaultValue: connection.mode })}
        description={t('sync.failover.explain')}
        actions={
          actions.includes('makePrimary') && (
            <Button variant="danger" onClick={() => setMaking(true)}>
              {t('sync.actions.makePrimary')}
            </Button>
          )
        }
      />
      {outage ? (
        <OutagePanel connectionId={connectionId} outage={outage} actions={actions} />
      ) : (
        <EmptyState title={t('sync.failover.noOutage')} />
      )}
      {making && <MakePrimaryDialog connectionId={connectionId} onClose={() => setMaking(false)} />}
    </div>
  );
}

/** "Make this system primary": reason and a fresh MFA code (TOTP or e-mail) are required; the server verifies both. */
export function MakePrimaryDialog({ connectionId, onClose }: { connectionId: string; onClose: () => void }) {
  const { t, tx } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const methods = useStepUpMethods(true);
  const available = methods.data?.methods ?? [];
  const [reason, setReason] = useState('');
  const [chosen, setChosen] = useState<string | undefined>();
  const [code, setCode] = useState('');
  const [sent, setSent] = useState(false);
  const method = chosen ?? available[0];
  const sendCode = useSyncMutation(async () =>
    unwrap(await api.POST('/api/v1/integration/sync/step-up/email-code')),
  );
  const makePrimary = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/make-primary', {
        params: { path: { id: connectionId } },
        body: { reason: reason.trim(), method: method ?? null, code: code.trim() },
      }),
    ),
  );
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const ready = reason.trim().length > 0 && code.trim().length > 0 && !!method;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('sync.actions.makePrimary')}
        description={t('sync.failover.makePrimaryWarning')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={!ready}
              loading={makePrimary.isPending}
              onClick={() =>
                makePrimary.mutate(undefined, {
                  onSuccess: () => {
                    toast({ tone: 'success', title: t('sync.failover.nowPrimary') });
                    onClose();
                  },
                  onError,
                })
              }
            >
              {t('sync.failover.confirmPrimary')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField label={t('sync.failover.reason')} required>
            <Textarea name="reason" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />
          </FormField>
          {methods.isPending ? (
            <Skeleton className="h-10 rounded-md" />
          ) : available.length === 0 ? (
            <StatusBanner tone="danger" title={t('sync.failover.noMfa')} />
          ) : (
            <>
              {available.length > 1 && (
                <FormField label={t('sync.failover.method')}>
                  <Select
                    value={method}
                    onValueChange={(value) => {
                      setChosen(value);
                      setCode('');
                    }}
                    options={available.map((m) => ({
                      value: m,
                      label: tx(`sync.failover.methods.${m}`, { defaultValue: m }),
                    }))}
                  />
                </FormField>
              )}
              {method === 'email' && (
                <div className="flex flex-wrap items-center gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    loading={sendCode.isPending}
                    onClick={() => sendCode.mutate(undefined, { onSuccess: () => setSent(true), onError })}
                  >
                    {sent ? t('sync.failover.resendCode') : t('sync.failover.sendCode')}
                  </Button>
                  {sent && (
                    <span className="text-sm text-muted-foreground">{t('sync.failover.codeSent')}</span>
                  )}
                </div>
              )}
              <FormField
                label={tx(`sync.failover.codeLabel.${method === 'email' ? 'email' : 'totp'}`)}
                required
              >
                <Input
                  dir="ltr"
                  name="code"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={10}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </FormField>
            </>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function OutagePanel({
  connectionId,
  outage,
  actions,
}: {
  connectionId: string;
  outage: SyncOutage;
  actions: string[];
}) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [acknowledged, setAcknowledged] = useState(false);
  const [confirmReturn, setConfirmReturn] = useState(false);
  const [confirmClose, setConfirmClose] = useState(false);
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const returnToStandby = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/return-to-standby', {
        params: { path: { id: connectionId } },
        body: { acknowledged: true },
      }),
    ),
  );
  const closeOutage = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/close-outage', {
        params: { path: { id: connectionId } },
      }),
    ),
  );
  const needsAck = !outage.reportAcknowledgedAt;
  return (
    <section className="flex flex-col gap-4" aria-labelledby="outage-title">
      <div className="flex flex-col gap-3 rounded-lg border bg-card p-4">
        <h2 id="outage-title" className="flex flex-wrap items-center gap-2 text-base font-semibold">
          {t('sync.outage.title')}
          <StatusChip tone={outage.status === 'open' ? 'danger' : 'warning'}>
            {tx(`sync.outage.status.${outage.status}`, { defaultValue: outage.status })}
          </StatusChip>
        </h2>
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4" data-testid="outage-facts">
          <Fact label={t('sync.outage.started')}>
            {formatDateTime(outage.startedAt, language)}
            {outage.startedByName && ` · ${outage.startedByName}`}
          </Fact>
          <Fact label={t('sync.failover.reason')}>
            <span dir="auto">{outage.reason}</span>
          </Fact>
          <Fact label={t('sync.outage.returned')}>
            {outage.returnedAt ? formatDateTime(outage.returnedAt, language) : '—'}
          </Fact>
          <Fact label={t('sync.outage.acknowledged')}>
            {outage.reportAcknowledgedAt ? formatDateTime(outage.reportAcknowledgedAt, language) : '—'}
          </Fact>
          <Fact label={t('sync.outage.resubmit')}>{formatNumber(outage.resubmitRequired, language)}</Fact>
          <Fact label={t('sync.outage.changed')}>{formatNumber(outage.changedLocally, language)}</Fact>
          <Fact label={t('sync.outage.reEntered')}>{formatNumber(outage.reEntered, language)}</Fact>
          <Fact label={t('sync.outage.conflicts')}>{formatNumber(outage.pendingConflicts, language)}</Fact>
        </dl>
        <div className="flex flex-wrap items-end gap-3">
          {actions.includes('returnToStandby') && (
            <>
              {needsAck && (
                <Checkbox
                  label={t('sync.outage.ackCheckbox')}
                  checked={acknowledged}
                  onChange={(e) => setAcknowledged(e.target.checked)}
                />
              )}
              <Button disabled={needsAck && !acknowledged} onClick={() => setConfirmReturn(true)}>
                {t('sync.actions.returnToStandby')}
              </Button>
            </>
          )}
          {actions.includes('closeOutage') && (
            <div className="flex flex-col gap-1">
              <Button
                variant="secondary"
                disabled={outage.pendingConflicts > 0}
                onClick={() => setConfirmClose(true)}
              >
                {t('sync.actions.closeOutage')}
              </Button>
              {outage.pendingConflicts > 0 && (
                <span className="text-xs text-muted-foreground">
                  {t('sync.outage.closeBlocked', { count: outage.pendingConflicts })}
                </span>
              )}
            </div>
          )}
        </div>
      </div>
      {outage.status === 'reconciling' && <ConflictsList outageId={outage.id} />}
      <OutageReportView outageId={outage.id} acknowledgedAt={outage.reportAcknowledgedAt} />
      <ConfirmDialog
        open={confirmReturn}
        onOpenChange={setConfirmReturn}
        title={t('sync.actions.returnToStandby')}
        description={t('sync.outage.returnWarning')}
        confirmLabel={t('sync.actions.returnToStandby')}
        loading={returnToStandby.isPending}
        onConfirm={() =>
          returnToStandby.mutate(undefined, { onSuccess: () => setConfirmReturn(false), onError })
        }
      />
      <ConfirmDialog
        open={confirmClose}
        onOpenChange={setConfirmClose}
        title={t('sync.actions.closeOutage')}
        description={t('sync.outage.closeWarning')}
        confirmLabel={t('sync.actions.closeOutage')}
        loading={closeOutage.isPending}
        onConfirm={() => closeOutage.mutate(undefined, { onSuccess: () => setConfirmClose(false), onError })}
      />
    </section>
  );
}

function RecordTitle({ link, number, title }: { link: string | null; number: string | null; title: string }) {
  const text = [number, title].filter(Boolean).join(' — ') || '—';
  if (!link) return <span dir="auto">{text}</span>;
  return (
    <Link to={link as '/'} className="text-primary underline-offset-4 hover:underline" dir="auto">
      {text}
    </Link>
  );
}

/** Outage changes report grouped by system → record, with the re-entered mark per record, export and acknowledge. */
export function OutageReportView({
  outageId,
  acknowledgedAt,
}: {
  outageId: string;
  acknowledgedAt: string | null;
}) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const report = useOutageReport(outageId);
  const { t: ta } = useTranslation('audit');
  const auditAction = (action: string) =>
    (ta as unknown as DynamicT)(`actions.${action.replaceAll('.', '_')}`, { defaultValue: action });
  const [exporting, setExporting] = useState<string | null>(null);
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const reEntered = useSyncMutation(async (input: { record: OutageRecordReport; value: boolean }) =>
    unwrap(
      await api.POST('/api/v1/integration/sync/outages/{outageId}/records/{type}/{recordId}/re-entered', {
        params: { path: { outageId, type: input.record.entityType, recordId: input.record.recordId } },
        body: { reEntered: input.value },
      }),
    ),
  );
  const acknowledge = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/outages/{outageId}/acknowledge', {
        params: { path: { outageId } },
      }),
    ),
  );
  const exportAs = async (format: 'xlsx' | 'pdf') => {
    setExporting(format);
    try {
      await downloadOutageReport(outageId, format, language);
      void report.refetch();
    } catch {
      toast({ tone: 'error', title: t('sync.report.exportFailed') });
    } finally {
      setExporting(null);
    }
  };
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-4"
      aria-labelledby="outage-report-title"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-col gap-1">
          <h2 id="outage-report-title" className="text-base font-semibold">
            {t('sync.report.title')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('sync.report.hint')}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" loading={exporting === 'xlsx'} onClick={() => void exportAs('xlsx')}>
            {t('sync.report.excel')}
          </Button>
          <Button variant="outline" loading={exporting === 'pdf'} onClick={() => void exportAs('pdf')}>
            {t('sync.report.pdf')}
          </Button>
          {!acknowledgedAt && (
            <Button
              variant="secondary"
              loading={acknowledge.isPending}
              onClick={() => acknowledge.mutate(undefined, { onError })}
            >
              {t('sync.report.acknowledge')}
            </Button>
          )}
        </div>
      </div>
      {report.isPending ? (
        <Skeleton className="h-48 rounded-lg" />
      ) : !report.data || report.data.systems.length === 0 ? (
        <EmptyState title={t('sync.report.empty')} />
      ) : (
        <div className="flex flex-col gap-4" data-testid="outage-report">
          <p className="text-sm text-muted-foreground">
            {t('sync.report.period', {
              from: formatDateTime(report.data.from, language),
              to: formatDateTime(report.data.to, language),
              count: report.data.changes,
            })}
          </p>
          {report.data.truncated && <StatusBanner tone="warning" title={t('sync.report.truncated')} />}
          {report.data.systems.map((system) => (
            <section
              key={system.systemCode}
              className="flex flex-col gap-3"
              aria-labelledby={`report-system-${system.systemCode}`}
              data-testid={`report-system-${system.systemCode}`}
            >
              <h3 id={`report-system-${system.systemCode}`} className="font-medium">
                {tx(`sync.systems.${system.systemCode}`, { defaultValue: system.systemCode })}
                <span className="ms-2 text-sm text-muted-foreground">
                  {t('sync.report.records', { count: system.records.length })}
                </span>
              </h3>
              {system.records.map((record) => (
                <article
                  key={`${record.entityType}:${record.recordId}`}
                  className="flex flex-col gap-2 rounded-md border p-3"
                  data-testid={`report-record-${record.recordId}`}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs text-muted-foreground">
                        {pickText(language, record.typeNameAr, record.typeNameEn) || record.entityType}
                      </span>
                      <RecordTitle
                        link={record.link}
                        number={record.number}
                        title={pickText(language, record.titleAr, record.titleEn)}
                      />
                      {record.salesforceId ? (
                        <span className="text-xs text-muted-foreground">
                          {t('sync.report.salesforceId')} <Code>{record.salesforceId}</Code>
                        </span>
                      ) : (
                        <StatusChip tone="info">{t('sync.report.createdHere')}</StatusChip>
                      )}
                      {record.resubmitRequired && (
                        <StatusChip tone="warning">{t('sync.badges.resubmit')}</StatusChip>
                      )}
                      {record.changedLocally && (
                        <StatusChip tone="info">{t('sync.report.changed')}</StatusChip>
                      )}
                      {record.conflict && <StatusChip tone="danger">{t('sync.report.conflict')}</StatusChip>}
                    </div>
                    <Checkbox
                      label={t('sync.report.reEntered')}
                      checked={record.reEntered}
                      disabled={reEntered.isPending}
                      onChange={(e) => reEntered.mutate({ record, value: e.target.checked }, { onError })}
                    />
                  </div>
                  {record.changes.length > 0 && (
                    <Table>
                      <TableHead>
                        <TableRow>
                          <TableHeaderCell>{t('sync.report.at')}</TableHeaderCell>
                          <TableHeaderCell>{t('sync.report.action')}</TableHeaderCell>
                          <TableHeaderCell>{t('sync.report.field')}</TableHeaderCell>
                          <TableHeaderCell>{t('sync.report.change')}</TableHeaderCell>
                          <TableHeaderCell>{t('sync.report.user')}</TableHeaderCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {record.changes.map((change, i) => (
                          <TableRow key={i}>
                            <TableCell>{formatDateTime(change.at, language)}</TableCell>
                            <TableCell>{auditAction(change.action)}</TableCell>
                            <TableCell>{change.field ? <Code>{change.field}</Code> : '—'}</TableCell>
                            <TableCell>
                              <span dir="auto" className="[overflow-wrap:anywhere]">
                                {change.oldValue ?? '—'} → {change.newValue ?? '—'}
                              </span>
                            </TableCell>
                            <TableCell>{change.userName ?? '—'}</TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  )}
                </article>
              ))}
            </section>
          ))}
        </div>
      )}
    </section>
  );
}

/** Records changed here during the outage for which Salesforce sent a newer version: Keep local or Take Salesforce. */
function ConflictsList({ outageId }: { outageId: string }) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const conflicts = useOutageConflicts(outageId);
  const [open, setOpen] = useState<string | null>(null);
  const resolve = useSyncMutation(async (input: { recordId: string; choice: 'keep_local' | 'take_source' }) =>
    unwrap(
      await api.POST('/api/v1/integration/sync/outages/{outageId}/conflicts/{recordId}/resolve', {
        params: { path: { outageId, recordId: input.recordId } },
        body: { choice: input.choice },
      }),
    ),
  );
  const items = conflicts.data ?? [];
  const pending = items.find((c) => c.recordId === open)?.pending;
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4" aria-labelledby="conflicts-title">
      <h2 id="conflicts-title" className="text-base font-semibold">
        {t('sync.conflicts.title')}
      </h2>
      <p className="text-sm text-muted-foreground">{t('sync.conflicts.hint')}</p>
      {conflicts.isPending ? (
        <Skeleton className="h-24 rounded-lg" />
      ) : items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('sync.conflicts.empty')}</p>
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('sync.conflicts.record')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.report.salesforceId')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.conflicts.sourceModified')}</TableHeaderCell>
              <TableHeaderCell>{t('sync.conflicts.resolution')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map((c) => (
              <TableRow key={c.recordId} data-testid={`conflict-${c.recordId}`}>
                <TableCell>
                  <RecordTitle
                    link={c.link}
                    number={c.number}
                    title={pickText(language, c.titleAr, c.titleEn)}
                  />
                </TableCell>
                <TableCell>{c.salesforceId ? <Code>{c.salesforceId}</Code> : '—'}</TableCell>
                <TableCell>
                  {c.sourceModifiedAt ? formatDateTime(c.sourceModifiedAt, language) : '—'}
                </TableCell>
                <TableCell>
                  <StatusChip tone={c.resolution === 'pending' ? 'warning' : 'info'}>
                    {tx(`sync.conflicts.resolutions.${c.resolution}`, { defaultValue: c.resolution })}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">
                  <div className="flex flex-wrap justify-end gap-2">
                    {c.pending && (
                      <Button variant="ghost" size="sm" onClick={() => setOpen(c.recordId)}>
                        {t('sync.conflicts.viewPending')}
                      </Button>
                    )}
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={resolve.isPending}
                      onClick={() =>
                        resolve.mutate(
                          { recordId: c.recordId, choice: 'keep_local' },
                          { onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }) },
                        )
                      }
                    >
                      {t('sync.conflicts.keepLocal')}
                    </Button>
                    <Button
                      variant="secondary"
                      size="sm"
                      disabled={resolve.isPending}
                      onClick={() =>
                        resolve.mutate(
                          { recordId: c.recordId, choice: 'take_source' },
                          { onError: (f) => toast({ tone: 'error', title: errorMessage(tc, f) }) },
                        )
                      }
                    >
                      {t('sync.conflicts.takeSource')}
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {open && pending && (
        <Dialog open onOpenChange={(isOpen) => !isOpen && setOpen(null)}>
          <DialogContent
            className="max-w-2xl"
            title={t('sync.conflicts.pendingTitle')}
            footer={
              <DialogClose asChild>
                <Button variant="outline">{tc('actions.close')}</Button>
              </DialogClose>
            }
          >
            <table className="w-full text-xs">
              <tbody className="divide-y">
                {Object.entries(pending).map(([key, value]) => (
                  <tr key={key}>
                    <th scope="row" className="w-1/3 px-2 py-1 text-start font-normal text-muted-foreground">
                      <Code>{key}</Code>
                    </th>
                    <td className="px-2 py-1" dir="auto">
                      {value || '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </DialogContent>
        </Dialog>
      )}
    </section>
  );
}
