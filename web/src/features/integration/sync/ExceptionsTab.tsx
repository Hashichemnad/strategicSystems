import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  Pagination,
  ScrollRegion,
  Select,
  Skeleton,
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
  EXCEPTION_REASONS,
  EXCEPTION_STATUSES,
  useLookupCodes,
  useSyncException,
  useSyncExceptions,
  useSyncMutation,
  useSyncTargets,
  type ExceptionFilters,
  type SyncException,
  type SyncExceptionDetail,
  type SyncTargetField,
} from './api';
import { Code, exceptionTone, useMessage, useSyncT } from './shared';

const ALL = '-';

/** Exceptions queue (ADR 0205): filters, payload, Map value, Map person, Re-process, Ignore, bulk re-process. */
export function ExceptionsTab({ connectionId, canManage }: { connectionId: string; canManage: boolean }) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [status, setStatus] = useState('open');
  const [reason, setReason] = useState(ALL);
  const [mapping, setMapping] = useState('');
  const [searchText, setSearchText] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchText.trim());
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timer);
  }, [searchText]);
  const filters: ExceptionFilters = {
    status: status === ALL ? undefined : status,
    reason: reason === ALL ? undefined : reason,
    mapping: mapping.trim() || undefined,
    search: search || undefined,
  };
  const exceptions = useSyncExceptions(connectionId, filters, page);
  const bulk = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/connections/{id}/exceptions/reprocess', {
        params: { path: { id: connectionId } },
        body: { reason: filters.reason ?? null, mappingKey: filters.mapping ?? null },
      }),
    ),
  );
  const items = exceptions.data?.items ?? [];
  const message = useMessage();
  return (
    <div className="flex flex-col gap-4 pt-4" data-testid="sync-exceptions">
      <div className="flex flex-wrap items-end gap-3">
        <div className="w-40">
          <Select
            aria-label={t('fields.status')}
            value={status}
            onValueChange={(value) => {
              setStatus(value);
              setPage(1);
            }}
            options={[
              { value: ALL, label: t('sync.exceptionsTab.all') },
              ...EXCEPTION_STATUSES.map((s) => ({ value: s, label: tx(`sync.exceptionStatus.${s}`) })),
            ]}
          />
        </div>
        <div className="w-56">
          <Select
            aria-label={t('sync.exceptionsTab.reason')}
            value={reason}
            onValueChange={(value) => {
              setReason(value);
              setPage(1);
            }}
            options={[
              { value: ALL, label: t('sync.exceptionsTab.allReasons') },
              ...EXCEPTION_REASONS.map((r) => ({ value: r, label: tx(`sync.reasons.${r}`) })),
            ]}
          />
        </div>
        <Input
          dir="ltr"
          className="w-48"
          name="mapping"
          aria-label={t('sync.exceptionsTab.object')}
          placeholder={t('sync.exceptionsTab.object')}
          value={mapping}
          onChange={(e) => {
            setMapping(e.target.value);
            setPage(1);
          }}
        />
        <Input
          className="w-56"
          type="search"
          name="search"
          aria-label={tc('actions.search')}
          placeholder={t('sync.exceptionsTab.search')}
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
        />
        {canManage && (
          <Button
            variant="secondary"
            loading={bulk.isPending}
            onClick={() =>
              bulk.mutate(undefined, {
                onSuccess: (result) =>
                  toast({
                    tone: 'success',
                    title: t('sync.exceptionsTab.reprocessed', {
                      processed: formatNumber(result.processed, language),
                      resolved: formatNumber(result.resolved, language),
                      open: formatNumber(result.stillOpen, language),
                    }),
                  }),
                onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
              })
            }
          >
            {t('sync.exceptionsTab.reprocessAll')}
          </Button>
        )}
      </div>
      {exceptions.isPending ? (
        <Skeleton className="h-48 rounded-lg" />
      ) : items.length === 0 ? (
        <EmptyState title={t('sync.exceptionsTab.empty')} />
      ) : (
        <>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('sync.exceptionsTab.reason')}</TableHeaderCell>
                <TableHeaderCell>{t('sync.exceptionsTab.object')}</TableHeaderCell>
                <TableHeaderCell>{t('sync.exceptionsTab.record')}</TableHeaderCell>
                <TableHeaderCell>{t('sync.exceptionsTab.detail')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('sync.exceptionsTab.updated')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{tc('actions.more')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((e) => (
                <TableRow key={e.id} data-testid={`exception-${e.externalId}`}>
                  <TableCell>{tx(`sync.reasons.${e.reason}`, { defaultValue: e.reason })}</TableCell>
                  <TableCell>
                    <Code>{e.mappingKey ?? e.sourceObject}</Code>
                  </TableCell>
                  <TableCell>
                    <Code>{e.externalId}</Code>
                  </TableCell>
                  <TableCell className="max-w-sm text-sm">{describeException(e, message)}</TableCell>
                  <TableCell>
                    <StatusChip tone={exceptionTone[e.status] ?? 'neutral'}>
                      {tx(`sync.exceptionStatus.${e.status}`, { defaultValue: e.status })}
                    </StatusChip>
                  </TableCell>
                  <TableCell>{formatDateTime(e.updatedAt, language)}</TableCell>
                  <TableCell className="text-end">
                    <Button variant="ghost" size="sm" onClick={() => setOpen(e.id)}>
                      {t('sync.exceptionsTab.open')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pagination
            page={page}
            totalPages={exceptions.data?.totalPages ?? 1}
            totalCount={exceptions.data?.totalCount ?? 0}
            onPageChange={setPage}
          />
        </>
      )}
      {open && <ExceptionDialog exceptionId={open} canManage={canManage} onClose={() => setOpen(null)} />}
    </div>
  );
}

function describeException(
  e: SyncException,
  message: (key: string | null | undefined, params?: Record<string, unknown> | null) => string,
): string {
  return message(e.messageKey ?? `integration.sync.exceptions.${e.reason}`, {
    field: e.field ?? '',
    value: e.value ?? '',
    max: '',
  });
}

/** The exception with its source payload and the fixes that apply to its reason. */
export function ExceptionDialog({
  exceptionId,
  canManage,
  onClose,
}: {
  exceptionId: string;
  canManage: boolean;
  onClose: () => void;
}) {
  const { t, tx, language } = useSyncT();
  const { t: tc } = useTranslation();
  const detail = useSyncException(exceptionId);
  const message = useMessage();
  const e = detail.data?.exception;
  return (
    <Dialog open onOpenChange={(isOpen) => !isOpen && onClose()}>
      <DialogContent
        className="max-w-3xl"
        title={e ? tx(`sync.reasons.${e.reason}`, { defaultValue: e.reason }) : t('sync.exceptionsTab.open')}
        description={e ? describeException(e, message) : undefined}
        footer={
          <DialogClose asChild>
            <Button variant="outline">{tc('actions.close')}</Button>
          </DialogClose>
        }
      >
        {!detail.data || !e ? (
          <Skeleton className="h-48 rounded-lg" />
        ) : (
          <div className="flex flex-col gap-4" data-testid="exception-detail">
            <dl className="grid gap-3 text-sm sm:grid-cols-3">
              <div>
                <dt className="text-xs text-muted-foreground">{t('sync.exceptionsTab.object')}</dt>
                <dd>
                  <Code>{e.sourceObject}</Code>
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">{t('sync.exceptionsTab.record')}</dt>
                <dd>
                  <Code>{e.externalId}</Code>
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">{t('sync.exceptionsTab.attempts')}</dt>
                <dd>
                  {formatNumber(e.attempts, language)} · {formatDateTime(e.createdAt, language)}
                </dd>
              </div>
              {e.field && (
                <div>
                  <dt className="text-xs text-muted-foreground">{t('sync.exceptionsTab.field')}</dt>
                  <dd>
                    <Code>{e.field}</Code>
                  </dd>
                </div>
              )}
              {e.value && (
                <div>
                  <dt className="text-xs text-muted-foreground">{t('sync.exceptionsTab.value')}</dt>
                  <dd>
                    <Code>{e.value}</Code>
                  </dd>
                </div>
              )}
            </dl>
            {e.params && Object.keys(e.params).length > 0 && (
              <section
                aria-labelledby="write-details-title"
                className="flex flex-col gap-2"
                data-testid="exception-write-details"
              >
                <h3 id="write-details-title" className="font-medium">
                  {t('sync.exceptionsTab.writeDetails')}
                </h3>
                <dl className="grid gap-2 text-sm sm:grid-cols-2">
                  {Object.entries(e.params)
                    .filter(([, value]) => value)
                    .map(([key, value]) => (
                      <div key={key}>
                        <dt className="text-xs text-muted-foreground">
                          {tx(`sync.exceptionsTab.writeDetail.${key}`, { defaultValue: key })}
                        </dt>
                        <dd dir="ltr" className="break-words">
                          <Code>{value}</Code>
                        </dd>
                      </div>
                    ))}
                </dl>
              </section>
            )}
            {canManage && e.status === 'open' && <ExceptionActions detail={detail.data} onDone={onClose} />}
            <section aria-labelledby="payload-title" className="flex flex-col gap-2">
              <h3 id="payload-title" className="font-medium">
                {t('sync.exceptionsTab.payload')}
              </h3>
              <ScrollRegion
                className="max-h-80 overflow-auto rounded-md border"
                label={t('sync.exceptionsTab.payload')}
              >
                <table className="w-full text-xs">
                  <tbody className="divide-y">
                    {Object.entries(detail.data.payload).map(([key, value]) => (
                      <tr key={key}>
                        <th
                          scope="row"
                          className="w-1/3 px-2 py-1 text-start font-normal text-muted-foreground"
                        >
                          <Code>{key}</Code>
                        </th>
                        <td className="px-2 py-1">
                          <span dir="auto" className="[overflow-wrap:anywhere]">
                            {value || '—'}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </ScrollRegion>
            </section>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function ExceptionActions({ detail, onDone }: { detail: SyncExceptionDetail; onDone: () => void }) {
  const { t, language } = useSyncT();
  const { t: tc } = useTranslation();
  const toast = useToast();
  const e = detail.exception;
  const targets = useSyncTargets();
  const targetField = targets.data
    ?.find((x) => x.code === detail.targetType)
    ?.fields.find((f) => f.code === e.field);
  const [code, setCode] = useState('');
  const [employeeId, setEmployeeId] = useState<string | undefined>();
  const onError = (failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) });
  const after = (title: string) => () => {
    toast({ tone: 'success', title });
    onDone();
  };
  const reprocess = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/exceptions/{exceptionId}/reprocess', {
        params: { path: { exceptionId: e.id } },
      }),
    ),
  );
  const ignore = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/exceptions/{exceptionId}/ignore', {
        params: { path: { exceptionId: e.id } },
      }),
    ),
  );
  const mapValue = useSyncMutation(async () =>
    unwrap(
      await api.POST('/api/v1/integration/sync/exceptions/{exceptionId}/map-value', {
        params: { path: { exceptionId: e.id } },
        body: { value: e.value, code: code.trim() },
      }),
    ),
  );
  const mapPerson = useSyncMutation(async (id: string) =>
    unwrap(
      await api.POST('/api/v1/integration/sync/exceptions/{exceptionId}/map-person', {
        params: { path: { exceptionId: e.id } },
        body: { employeeId: id },
      }),
    ),
  );
  const resolvedTitle = (result: SyncException) =>
    result.status === 'resolved' ? t('sync.exceptionsTab.resolved') : t('sync.exceptionsTab.stillOpen');
  return (
    <section
      className="flex flex-col gap-3 rounded-md border bg-muted/40 p-3"
      aria-label={t('sync.exceptionsTab.fix')}
    >
      {e.reason === 'unmappedValue' && (
        <div className="flex flex-wrap items-end gap-2">
          <FormField
            label={t('sync.exceptionsTab.mapValue', { value: e.value ?? '' })}
            hint={t('sync.exceptionsTab.mapValueHint')}
          >
            <CodeInput field={targetField} value={code} onChange={setCode} language={language} />
          </FormField>
          <Button
            disabled={!code.trim()}
            loading={mapValue.isPending}
            onClick={() =>
              mapValue.mutate(undefined, {
                onSuccess: (result) => after(resolvedTitle(result))(),
                onError,
              })
            }
          >
            {t('sync.exceptionsTab.mapAndReprocess')}
          </Button>
        </div>
      )}
      {e.reason === 'unknownUser' && (
        <div className="flex flex-wrap items-end gap-2">
          <div className="min-w-64 flex-1">
            <FormField label={t('sync.exceptionsTab.mapPerson', { value: e.value ?? '' })}>
              <EmployeePicker value={employeeId} onChange={setEmployeeId} />
            </FormField>
          </div>
          <Button
            disabled={!employeeId}
            loading={mapPerson.isPending}
            onClick={() =>
              employeeId &&
              mapPerson.mutate(employeeId, { onSuccess: (result) => after(resolvedTitle(result))(), onError })
            }
          >
            {t('sync.exceptionsTab.mapAndReprocess')}
          </Button>
        </div>
      )}
      <div className="flex flex-wrap gap-2">
        <Button
          variant="secondary"
          loading={reprocess.isPending}
          onClick={() =>
            reprocess.mutate(undefined, { onSuccess: (result) => after(resolvedTitle(result))(), onError })
          }
        >
          {t('sync.exceptionsTab.reprocess')}
        </Button>
        <Button
          variant="outline"
          loading={ignore.isPending}
          onClick={() =>
            ignore.mutate(undefined, { onSuccess: after(t('sync.exceptionsTab.ignored')), onError })
          }
        >
          {t('sync.exceptionsTab.ignore')}
        </Button>
      </div>
    </section>
  );
}

/** A code for the target field: the lookup's active values, the fixed choices, or free text. */
function CodeInput({
  field,
  value,
  onChange,
  language,
}: {
  field: SyncTargetField | undefined;
  value: string;
  onChange: (value: string) => void;
  language: string;
}) {
  const { t } = useSyncT();
  const lookup = useLookupCodes(field?.lookupType);
  const options =
    field?.lookupType && lookup.data
      ? lookup.data
          .filter((v) => v.active)
          .map((v) => ({
            value: v.code,
            label: `${pickText(language, v.labelAr, v.labelEn) || v.code} (${v.code})`,
          }))
      : field?.choices?.length
        ? field.choices.map((c) => ({ value: c, label: c }))
        : null;
  if (options && options.length > 0)
    return (
      <Select
        aria-label={t('sync.exceptionsTab.code')}
        value={value || undefined}
        placeholder={t('sync.exceptionsTab.chooseCode')}
        onValueChange={onChange}
        options={options}
      />
    );
  return (
    <Input
      dir="ltr"
      name="code"
      aria-label={t('sync.exceptionsTab.code')}
      value={value}
      onChange={(ev) => onChange(ev.target.value)}
    />
  );
}
