import { Eye, EyeOff, Pencil, Plus, Trash2, Users } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { useLookupOptions } from '@/features/settings/lookups';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  Select,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  ipApi,
  ipLookups,
  signatureStatuses,
  useIpRecordMutation,
  type IpContributor,
  type IpContributorInput,
  type IpRecord,
} from './api';
import { actionError, useIpT, useLookupLabel } from './components';
import { isEmiratesIdInputValid, sharesAreValid, sharesTotal } from './logic';

/** Role the server gives a contributor removed before filing (kept with share 0 for the history; not shown). */
const REMOVED = 'removed';
const REVEAL_SECONDS = 30;

export function visibleContributors(record: IpRecord): IpContributor[] {
  return record.contributors.filter((c) => c.role !== REMOVED);
}

/** Contributors tab: masked Emirates IDs (reveal for IP administrators, briefly, never stored), shares, signatures. */
export function ContributorsTab({ record }: { record: IpRecord }) {
  const { t, i18n } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const { k, language } = useIpT();
  const toast = useToast();
  const roleLabel = useLookupLabel(ipLookups.contributorRole);
  const [revealed, setRevealed] = useState<Record<string, string>>({});
  const [revealing, setRevealing] = useState<string>();
  const [editing, setEditing] = useState(false);
  const rows = visibleContributors(record);

  // A revealed Emirates ID is shown for a short time only and lives in memory (never cached or stored).
  useEffect(() => {
    if (Object.keys(revealed).length === 0) return;
    const timer = window.setTimeout(() => setRevealed({}), REVEAL_SECONDS * 1000);
    return () => window.clearTimeout(timer);
  }, [revealed]);

  const reveal = (contributorId: string) => {
    setRevealing(contributorId);
    ipApi
      .revealEmiratesId(record.id, contributorId)
      .then((r) => setRevealed((v) => ({ ...v, [contributorId]: r.emiratesId })))
      .catch((e: unknown) => toast({ tone: 'error', title: actionError(tc, e) }))
      .finally(() => setRevealing(undefined));
  };
  const hide = (contributorId: string) =>
    setRevealed((v) => Object.fromEntries(Object.entries(v).filter(([id]) => id !== contributorId)));

  return (
    <div className="flex flex-col gap-4" data-testid="ip-contributors">
      {record.can.editContributors && (
        <div>
          <Button variant="outline" onClick={() => setEditing(true)} data-testid="ip-edit-contributors">
            <Pencil aria-hidden />
            {t('contributors.edit')}
          </Button>
        </div>
      )}
      {rows.length === 0 ? (
        <EmptyState icon={Users} title={t('contributors.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('contributors.name')}</TableHeaderCell>
                <TableHeaderCell>{t('contributors.role')}</TableHeaderCell>
                <TableHeaderCell>{t('contributors.share')}</TableHeaderCell>
                <TableHeaderCell>{t('contributors.emiratesId')}</TableHeaderCell>
                <TableHeaderCell>{t('contributors.signature')}</TableHeaderCell>
                <TableHeaderCell>{t('contributors.certificateSent')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((c) => {
                const clear = revealed[c.id];
                return (
                  <TableRow key={c.id} data-testid={`ip-contributor-${c.id}`}>
                    <TableCell>
                      {c.employee ? (
                        pickText(language, c.employee.nameAr, c.employee.nameEn)
                      ) : (
                        <span className="flex flex-col">
                          <span>{c.externalName ?? '—'}</span>
                          <span className="text-xs text-muted-foreground">
                            {t('contributors.external')}
                            {c.externalOrganisation ? ` · ${c.externalOrganisation}` : ''}
                          </span>
                        </span>
                      )}
                    </TableCell>
                    <TableCell>{roleLabel(c.role)}</TableCell>
                    <TableCell className="tabular-nums">
                      <bdi>{formatNumber(c.sharePercent, i18n.language)} %</bdi>
                    </TableCell>
                    <TableCell>
                      {c.hasEmiratesId ? (
                        <span className="flex flex-wrap items-center gap-2">
                          <span dir="ltr" className="font-mono text-sm" data-testid={`ip-eid-${c.id}`}>
                            {clear ?? c.emiratesIdMasked}
                          </span>
                          {record.can.sensitive &&
                            (clear ? (
                              <Button variant="ghost" size="sm" onClick={() => hide(c.id)}>
                                <EyeOff aria-hidden />
                                {t('contributors.hide')}
                              </Button>
                            ) : (
                              <Button
                                variant="ghost"
                                size="sm"
                                loading={revealing === c.id}
                                onClick={() => reveal(c.id)}
                                data-testid={`ip-reveal-${c.id}`}
                              >
                                <Eye aria-hidden />
                                {t('contributors.reveal')}
                              </Button>
                            ))}
                        </span>
                      ) : (
                        <span className="text-muted-foreground">{t('contributors.noEmiratesId')}</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={c.signatureStatus === 'pending' ? 'warning' : 'success'}>
                        {k(`signature.${c.signatureStatus}`)}
                      </StatusChip>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      {c.certificateSentAt ? formatDateTime(c.certificateSentAt, i18n.language) : '—'}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
      {record.can.sensitive && rows.some((c) => c.hasEmiratesId) && (
        <p className="text-xs text-muted-foreground">
          {t('contributors.revealHint', { count: REVEAL_SECONDS })}
        </p>
      )}
      {editing && <ContributorsDialog record={record} onClose={() => setEditing(false)} />}
    </div>
  );
}

interface Row {
  key: string;
  id: string | null;
  external: boolean;
  employeeId: string | null;
  externalName: string;
  externalOrganisation: string;
  emiratesId: string;
  role: string | null;
  share: string;
  signatureStatus: string;
}

let rowSeq = 0;
const newKey = () => `row-${++rowSeq}`;

function toRow(c: IpContributor): Row {
  return {
    key: c.id,
    id: c.id,
    external: !c.employee,
    employeeId: c.employee?.id ?? null,
    externalName: c.externalName ?? '',
    externalOrganisation: c.externalOrganisation ?? '',
    emiratesId: c.emiratesIdMasked ?? '',
    role: c.role,
    share: String(c.sharePercent),
    signatureStatus: c.signatureStatus,
  };
}

/** Edit contributors before / at filing: employee or external, role, share (total 100), Emirates ID (the mask keeps the stored one). */
function ContributorsDialog({ record, onClose }: { record: IpRecord; onClose: () => void }) {
  const { t, i18n } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const { k } = useIpT();
  const toast = useToast();
  const save = useIpRecordMutation(record.id, ipApi.setContributors);
  const [rows, setRows] = useState<Row[]>(() => visibleContributors(record).map(toRow));
  const [error, setError] = useState<string>();
  const [submitted, setSubmitted] = useState(false);
  const roleOptions = useLookupOptions(ipLookups.contributorRole, undefined, t('contributors.noRole'));
  const update = (key: string, patch: Partial<Row>) =>
    setRows((list) => list.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const shares = rows.map((r) => Number(r.share));
  const total = sharesTotal(shares);
  const sharesOk = sharesAreValid(shares);
  const rowErrors = rows.map((r) => ({
    who: r.external ? !r.externalName.trim() : !r.employeeId,
    eid: !isEmiratesIdInputValid(r.emiratesId),
  }));
  const valid = sharesOk && rowErrors.every((e) => !e.who && !e.eid);

  const submit = () => {
    setSubmitted(true);
    if (!valid) return;
    setError(undefined);
    const contributors: IpContributorInput[] = rows.map((r) => ({
      id: r.id,
      employeeId: r.external ? null : r.employeeId,
      externalName: r.external ? r.externalName.trim() : null,
      externalOrganisation: r.external ? r.externalOrganisation.trim() || null : null,
      emiratesId: r.emiratesId.trim() || null,
      role: r.role,
      sharePercent: Number(r.share),
      signatureStatus: r.signatureStatus,
    }));
    save.mutate(
      { contributors, version: record.version },
      {
        onSuccess: () => {
          toast({ title: t('actions.saved') });
          onClose();
        },
        onError: (e) => setError(actionError(tc, e)),
      },
    );
  };

  const addRow = (external: boolean) =>
    setRows((list) => [
      ...list,
      {
        key: newKey(),
        id: null,
        external,
        employeeId: null,
        externalName: '',
        externalOrganisation: '',
        emiratesId: '',
        role: null,
        share: '0',
        signatureStatus: 'pending',
      },
    ]);

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('contributors.edit')}
        description={t('contributors.editDescription')}
        className="max-w-4xl"
      >
        <div className="flex flex-col gap-3">
          {rows.map((r, index) => (
            <fieldset key={r.key} className="grid gap-3 rounded-md border p-3 md:grid-cols-2 lg:grid-cols-3">
              <legend className="px-1 text-sm font-medium">
                {t('contributors.row', { index: index + 1 })} ·{' '}
                {r.external ? t('contributors.external') : t('contributors.employee')}
              </legend>
              {r.external ? (
                <>
                  <FormField
                    label={t('contributors.externalName')}
                    required
                    error={submitted && rowErrors[index]?.who ? t('errors.contributorWho') : undefined}
                  >
                    <Input
                      value={r.externalName}
                      onChange={(e) => update(r.key, { externalName: e.target.value })}
                      dir="auto"
                    />
                  </FormField>
                  <FormField label={t('contributors.externalOrganisation')}>
                    <Input
                      value={r.externalOrganisation}
                      onChange={(e) => update(r.key, { externalOrganisation: e.target.value })}
                      dir="auto"
                    />
                  </FormField>
                </>
              ) : (
                <FormField
                  label={t('contributors.employee')}
                  required
                  error={submitted && rowErrors[index]?.who ? t('errors.contributorWho') : undefined}
                >
                  <EmployeePicker
                    value={r.employeeId ?? undefined}
                    onChange={(v) => update(r.key, { employeeId: v ?? null })}
                  />
                </FormField>
              )}
              <FormField
                label={t('contributors.emiratesId')}
                hint={
                  r.id && r.emiratesId.includes('*')
                    ? t('contributors.maskHint')
                    : t('contributors.eidFormat')
                }
                error={rowErrors[index]?.eid ? t('contributors.eidInvalid') : undefined}
              >
                <Input
                  dir="ltr"
                  inputMode="numeric"
                  autoComplete="off"
                  placeholder="784-YYYY-NNNNNNN-N"
                  value={r.emiratesId}
                  onChange={(e) => update(r.key, { emiratesId: e.target.value })}
                  data-testid={`ip-eid-input-${index}`}
                />
              </FormField>
              <FormField label={t('contributors.role')}>
                <Select
                  value={r.role ?? '-'}
                  onValueChange={(v) => update(r.key, { role: v === '-' ? null : v })}
                  options={roleOptions}
                />
              </FormField>
              <FormField label={t('contributors.sharePercent')} required>
                <Input
                  type="number"
                  min={0}
                  max={100}
                  step="0.01"
                  value={r.share}
                  onChange={(e) => update(r.key, { share: e.target.value })}
                  data-testid={`ip-share-input-${index}`}
                />
              </FormField>
              <FormField label={t('contributors.signature')}>
                <Select
                  value={r.signatureStatus}
                  onValueChange={(v) => update(r.key, { signatureStatus: v })}
                  options={signatureStatuses.map((s) => ({ value: s, label: k(`signature.${s}`) }))}
                />
              </FormField>
              <div className="flex items-end">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => setRows((list) => list.filter((x) => x.key !== r.key))}
                  aria-label={t('contributors.remove', { index: index + 1 })}
                >
                  <Trash2 aria-hidden />
                  {t('contributors.removeShort')}
                </Button>
              </div>
            </fieldset>
          ))}
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" size="sm" onClick={() => addRow(false)}>
              <Plus aria-hidden />
              {t('contributors.addEmployee')}
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => addRow(true)}>
              <Plus aria-hidden />
              {t('contributors.addExternal')}
            </Button>
          </div>
          <p
            className={sharesOk ? 'text-sm text-muted-foreground' : 'text-sm font-medium text-danger'}
            role="status"
            data-testid="ip-shares-total"
          >
            {t('contributors.total', { value: formatNumber(total, i18n.language) })}
            {!sharesOk && ` — ${t('errors.sharesTotal')}`}
          </p>
          {error && <StatusBanner tone="danger" title={error} />}
        </div>
        <div className="flex flex-wrap justify-end gap-2">
          <DialogClose asChild>
            <Button variant="outline">{tc('actions.cancel')}</Button>
          </DialogClose>
          <Button
            onClick={submit}
            loading={save.isPending}
            disabled={submitted && !valid}
            data-testid="dialog-confirm"
          >
            {tc('actions.save')}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
