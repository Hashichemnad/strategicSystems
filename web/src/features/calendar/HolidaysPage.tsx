import { FileUp, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useServerErrors } from '@/features/settings/common';
import { formatDate, pickText } from '@/lib/format';
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
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  downloadImportReport,
  importHolidays,
  useBusinessHours,
  useHolidays,
  useRemoveHoliday,
  useSaveHoliday,
  type BusinessHours,
  type Holiday,
  type HolidayImportResult,
} from './api';

function HolidayDialog({
  holiday,
  year,
  records,
  onClose,
}: {
  holiday?: Holiday;
  year: number;
  records: readonly BusinessHours[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useSaveHoliday();
  const errors = useServerErrors();
  const [nameAr, setNameAr] = useState(holiday?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(holiday?.nameEn ?? '');
  const [startDate, setStartDate] = useState(holiday?.startDate ?? `${year}-01-01`);
  const [endDate, setEndDate] = useState(
    holiday && holiday.endDate !== holiday.startDate ? holiday.endDate : '',
  );
  const [allDay, setAllDay] = useState(holiday?.allDay ?? true);
  const [startTime, setStartTime] = useState(holiday?.startTime ?? '12:00');
  const [endTime, setEndTime] = useState(holiday?.endTime ?? '15:30');
  const [recurring, setRecurring] = useState(holiday?.recurring ?? false);
  const [appliesToAll, setAppliesToAll] = useState(holiday?.appliesToAll ?? true);
  const [selected, setSelected] = useState<string[]>(holiday?.businessHoursIds ?? []);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={holiday ? t('holidays.edit') : t('holidays.new')}
        className="max-w-2xl"
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
                    ...(holiday ? { id: holiday.id } : {}),
                    nameAr: nameAr.trim() || null,
                    nameEn: nameEn.trim() || null,
                    startDate,
                    endDate: endDate || null,
                    allDay,
                    startTime: allDay ? null : startTime,
                    endTime: allDay ? null : endTime,
                    recurring,
                    appliesToAll,
                    businessHoursIds: appliesToAll ? [] : selected,
                    version: holiday?.version ?? 0,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('holidays.saved') });
                      onClose();
                    },
                    onError: errors.fail,
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('holidays.nameAr')} error={errors.errorFor('nameAr')}>
            <Input dir="rtl" lang="ar" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('holidays.nameEn')} error={errors.errorFor('nameEn')}>
            <Input dir="ltr" lang="en" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          <FormField label={t('holidays.startDate')} error={errors.errorFor('startDate')}>
            <Input type="date" dir="ltr" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
          </FormField>
          <FormField label={t('holidays.endDate')} error={errors.errorFor('endDate')}>
            <Input type="date" dir="ltr" value={endDate} onChange={(e) => setEndDate(e.target.value)} />
          </FormField>
        </div>
        <Switch checked={allDay} onCheckedChange={setAllDay} label={t('holidays.allDaySwitch')} />
        {!allDay && (
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('holidays.from')} error={errors.errorFor('startTime')}>
              <Input type="time" dir="ltr" value={startTime} onChange={(e) => setStartTime(e.target.value)} />
            </FormField>
            <FormField label={t('holidays.to')} error={errors.errorFor('endTime')}>
              <Input type="time" dir="ltr" value={endTime} onChange={(e) => setEndTime(e.target.value)} />
            </FormField>
          </div>
        )}
        <Switch
          checked={recurring}
          onCheckedChange={setRecurring}
          label={t('holidays.recurringSwitch')}
          description={t('holidays.recurringHint')}
        />
        <Switch checked={appliesToAll} onCheckedChange={setAppliesToAll} label={t('holidays.appliesToAll')} />
        {!appliesToAll && (
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-semibold">{t('holidays.records')}</legend>
            {records
              .filter((r) => r.active)
              .map((record) => (
                <Checkbox
                  key={record.id}
                  label={pickText(i18n.language, record.nameAr, record.nameEn)}
                  checked={selected.includes(record.id)}
                  onChange={(e) =>
                    setSelected(
                      e.target.checked ? [...selected, record.id] : selected.filter((id) => id !== record.id),
                    )
                  }
                />
              ))}
            {errors.errorFor('businessHoursIds') && (
              <p className="text-sm text-danger">{errors.errorFor('businessHoursIds')}</p>
            )}
          </fieldset>
        )}
      </DialogContent>
    </Dialog>
  );
}

function ImportDialog({ year, onClose }: { year: number; onClose: () => void }) {
  const { t } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<HolidayImportResult>();
  const [busy, setBusy] = useState(false);
  const run = async (mode: 'validate' | 'commit') => {
    if (!file) return;
    setBusy(true);
    try {
      const outcome = await importHolidays(year, file, mode);
      setResult(outcome);
      if (outcome.committed) {
        toast({ title: t('import.imported', { count: outcome.created }) });
        onClose();
      }
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('import.title')}
        description={t('import.description')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button variant="secondary" disabled={!file} loading={busy} onClick={() => void run('validate')}>
              {t('import.validate')}
            </Button>
            <Button
              disabled={!file || (result !== undefined && result.issues.length > 0)}
              loading={busy}
              onClick={() => void run('commit')}
            >
              {t('import.commit')}
            </Button>
          </>
        }
      >
        <a
          className="text-sm text-primary hover:underline"
          href={`/api/v1/calendar/holidays/import/template?year=${year}`}
          download
        >
          {t('import.template')} ({year})
        </a>
        <FormField label={t('import.file')}>
          <Input
            type="file"
            accept=".xlsx,.csv"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setResult(undefined);
            }}
          />
        </FormField>
        {result && result.issues.length === 0 && (
          <StatusBanner tone="success" title={t('import.valid', { count: result.totalRows })} />
        )}
        {result && result.issues.length > 0 && (
          <div className="flex flex-col gap-2" role="alert">
            <p className="text-sm font-semibold">{t('import.issues')}</p>
            <ul className="flex max-h-48 flex-col gap-1 overflow-auto text-sm">
              {result.issues.map((issue, index) => (
                <li key={index}>
                  {t('import.row', { row: issue.row })}
                  {issue.column && (
                    <span className="text-muted-foreground" dir="ltr">
                      {' '}
                      [{issue.column}]
                    </span>
                  )}
                  :{' '}
                  {translateMessageKey(
                    tc,
                    issue.code,
                    (issue.params ?? undefined) as Record<string, unknown> | undefined,
                  )}
                </li>
              ))}
            </ul>
            {file && (
              <Button
                size="sm"
                variant="outline"
                className="self-start"
                onClick={() => void downloadImportReport(year, file)}
              >
                {t('import.report')}
              </Button>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

/** Settings → Holidays (ADR 0027): the year's holidays, editor and Excel import. */
export function HolidaysPage() {
  const { t, i18n } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canManage = useCan('calendar.business-hours.manage');
  const thisYear = new Date().getFullYear();
  const [year, setYear] = useState(thisYear);
  const holidays = useHolidays(year);
  const records = useBusinessHours();
  const remove = useRemoveHoliday();
  const [editing, setEditing] = useState<Holiday | 'new'>();
  const [removing, setRemoving] = useState<Holiday>();
  const [importing, setImporting] = useState(false);
  const recordName = (id: string) => {
    const record = records.data?.find((r) => r.id === id);
    return record ? pickText(i18n.language, record.nameAr, record.nameEn) : '—';
  };
  const datesOf = (holiday: Holiday) => {
    const start = holiday.occursOn ?? holiday.startDate;
    const days = (Date.parse(holiday.endDate) - Date.parse(holiday.startDate)) / 86_400_000;
    const end = new Date(Date.parse(start) + days * 86_400_000).toISOString().slice(0, 10);
    return days > 0
      ? `${formatDate(start, i18n.language)} – ${formatDate(end, i18n.language)}`
      : formatDate(start, i18n.language);
  };
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('holidays.title')}
        description={t('holidays.description')}
        actions={
          canManage && (
            <>
              <Button variant="outline" onClick={() => setImporting(true)}>
                <FileUp aria-hidden />
                {t('holidays.import')}
              </Button>
              <Button onClick={() => setEditing('new')}>
                <Plus aria-hidden />
                {t('holidays.new')}
              </Button>
            </>
          )
        }
      />
      <div className="w-40">
        <FormField label={t('holidays.year')}>
          <Select
            value={String(year)}
            onValueChange={(value) => setYear(Number(value))}
            options={[-1, 0, 1, 2].map((offset) => ({
              value: String(thisYear + offset),
              label: String(thisYear + offset),
            }))}
          />
        </FormField>
      </div>
      {holidays.isError && <StatusBanner tone="danger" title={errorMessage(tc, holidays.error)} />}
      {holidays.isPending && <Skeleton className="h-48 rounded-lg" />}
      {holidays.data?.length === 0 && <EmptyState title={t('holidays.empty', { year })} />}
      {holidays.data && holidays.data.length > 0 && (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('holidays.dates')}</TableHeaderCell>
              <TableHeaderCell>{t('holidays.name')}</TableHeaderCell>
              <TableHeaderCell>{t('holidays.hours')}</TableHeaderCell>
              <TableHeaderCell>{t('holidays.appliesTo')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.edit')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {holidays.data.map((holiday) => (
              <TableRow key={holiday.id} data-testid="holiday-row">
                <TableCell>{datesOf(holiday)}</TableCell>
                <TableCell>
                  <span className="flex flex-wrap items-center gap-2">
                    {pickText(i18n.language, holiday.nameAr, holiday.nameEn)}
                    <StatusChip tone={holiday.recurring ? 'info' : 'neutral'}>
                      {holiday.recurring ? t('holidays.recurring') : t('holidays.oneTime')}
                    </StatusChip>
                  </span>
                </TableCell>
                <TableCell dir={holiday.allDay ? undefined : 'ltr'}>
                  {holiday.allDay
                    ? t('holidays.allDay')
                    : `${holiday.startTime ?? ''}–${holiday.endTime ?? ''}`}
                </TableCell>
                <TableCell>
                  {holiday.appliesToAll
                    ? t('holidays.allRecords')
                    : holiday.businessHoursIds.map(recordName).join('، ')}
                </TableCell>
                <TableCell>
                  {canManage && (
                    <span className="flex gap-1">
                      <Button size="sm" variant="ghost" onClick={() => setEditing(holiday)}>
                        {tc('actions.edit')}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setRemoving(holiday)}>
                        {t('holidays.remove')}
                      </Button>
                    </span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && records.data && (
        <HolidayDialog
          holiday={editing === 'new' ? undefined : editing}
          year={year}
          records={records.data}
          onClose={() => setEditing(undefined)}
        />
      )}
      {importing && <ImportDialog year={year} onClose={() => setImporting(false)} />}
      <ConfirmDialog
        open={removing !== undefined}
        onOpenChange={(open) => !open && setRemoving(undefined)}
        title={t('holidays.remove')}
        description={t('holidays.confirmRemove', {
          name: removing ? pickText(i18n.language, removing.nameAr, removing.nameEn) : '',
        })}
        confirmLabel={t('holidays.remove')}
        destructive
        loading={remove.isPending}
        onConfirm={() =>
          removing &&
          remove.mutate(removing, {
            onSuccess: () => {
              setRemoving(undefined);
              toast({ title: t('holidays.removed') });
            },
            onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
          })
        }
      />
    </div>
  );
}
