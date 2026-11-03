import { Clock, Plus, Star } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { useServerErrors } from '@/features/settings/common';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  useBusinessHours,
  useBusinessHoursAction,
  useCalculate,
  useSaveBusinessHours,
  useSetUnitBusinessHours,
  useUnitAssignments,
  weekdayName,
  type BusinessHours,
  type Day,
} from './api';

const defaultWeek: Day[] = [1, 2, 3, 4, 5, 6, 7].map((day) =>
  day <= 5
    ? { day, mode: 'hours', start: '07:30', end: '15:30' }
    : { day, mode: 'closed', start: null, end: null },
);

function useDayText() {
  const { t } = useTranslation('calendar');
  return (day: Day) =>
    day.mode === 'closed'
      ? t('businessHours.closed')
      : day.mode === 'allDay'
        ? t('businessHours.allDay')
        : `${day.start ?? ''}–${day.end ?? ''}`;
}

/** Create / edit dialog: names, time zone and the seven days (closed, 24 hours or one interval). */
function BusinessHoursDialog({ record, onClose }: { record?: BusinessHours; onClose: () => void }) {
  const { t, i18n } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useSaveBusinessHours();
  const errors = useServerErrors();
  const [nameAr, setNameAr] = useState(record?.nameAr ?? '');
  const [nameEn, setNameEn] = useState(record?.nameEn ?? '');
  const [timeZone, setTimeZone] = useState(record?.timeZone ?? '');
  const [days, setDays] = useState<Day[]>(record?.days ?? defaultWeek);
  const zones = useMemo(() => {
    try {
      return Intl.supportedValuesOf('timeZone');
    } catch {
      return [];
    }
  }, []);
  const setDay = (day: number, patch: Partial<Day>) =>
    setDays(days.map((d) => (d.day === day ? { ...d, ...patch } : d)));

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={record ? t('businessHours.edit') : t('businessHours.new')}
        className="max-w-3xl"
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
                    ...(record ? { id: record.id } : {}),
                    nameAr: nameAr.trim() || null,
                    nameEn: nameEn.trim() || null,
                    timeZone: timeZone.trim() || null,
                    days,
                    version: record?.version ?? 0,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('businessHours.saved') });
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
        <div className="grid gap-4 sm:grid-cols-3">
          <FormField label={t('businessHours.nameAr')} error={errors.errorFor('nameAr')}>
            <Input dir="rtl" lang="ar" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('businessHours.nameEn')} error={errors.errorFor('nameEn')}>
            <Input dir="ltr" lang="en" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          <FormField
            label={t('businessHours.timeZone')}
            hint={t('businessHours.timeZoneHint')}
            error={errors.errorFor('timeZone')}
          >
            <Input
              dir="ltr"
              list="business-hours-zones"
              value={timeZone}
              onChange={(e) => setTimeZone(e.target.value)}
            />
          </FormField>
          <datalist id="business-hours-zones">
            {zones.map((zone) => (
              <option key={zone} value={zone} />
            ))}
          </datalist>
        </div>
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 text-sm font-semibold">{t('businessHours.week')}</legend>
          {errors.errorFor('days') && <p className="text-sm text-danger">{errors.errorFor('days')}</p>}
          {days.map((day, index) => (
            <div key={day.day} className="grid items-start gap-2 sm:grid-cols-[8rem_10rem_1fr_1fr]">
              <span className="pt-2 text-sm font-medium">{weekdayName(day.day, i18n.language)}</span>
              <Select
                aria-label={`${t('businessHours.mode')} — ${weekdayName(day.day, i18n.language)}`}
                value={day.mode}
                onValueChange={(mode) =>
                  setDay(
                    day.day,
                    mode === 'hours'
                      ? { mode, start: day.start ?? '07:30', end: day.end ?? '15:30' }
                      : { mode, start: null, end: null },
                  )
                }
                options={[
                  { value: 'hours', label: t('businessHours.modes.hours') },
                  { value: 'allDay', label: t('businessHours.modes.allDay') },
                  { value: 'closed', label: t('businessHours.modes.closed') },
                ]}
              />
              {day.mode === 'hours' && (
                <>
                  <FormField label={t('businessHours.from')} error={errors.errorFor(`days[${index}].start`)}>
                    <Input
                      type="time"
                      dir="ltr"
                      value={day.start ?? ''}
                      onChange={(e) => setDay(day.day, { start: e.target.value })}
                    />
                  </FormField>
                  <FormField label={t('businessHours.to')} error={errors.errorFor(`days[${index}].end`)}>
                    <Input
                      type="time"
                      dir="ltr"
                      value={day.end ?? ''}
                      onChange={(e) => setDay(day.day, { end: e.target.value })}
                    />
                  </FormField>
                </>
              )}
            </div>
          ))}
        </fieldset>
      </DialogContent>
    </Dialog>
  );
}

function RecordCard({ record, onEdit }: { record: BusinessHours; onEdit: () => void }) {
  const { t, i18n } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canManage = useCan('calendar.business-hours.manage');
  const action = useBusinessHoursAction();
  const dayText = useDayText();
  const [confirm, setConfirm] = useState(false);
  const name = pickText(i18n.language, record.nameAr, record.nameEn);
  const run = (kind: 'make-default' | 'activate' | 'deactivate', message: string) =>
    action.mutate(
      { record, action: kind },
      {
        onSuccess: () => {
          setConfirm(false);
          toast({ title: message });
        },
        onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
      },
    );
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={name}>
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex flex-col gap-1">
          <h3 className="flex flex-wrap items-center gap-2 font-semibold">
            {name}
            {record.isDefault && (
              <StatusChip tone="success">
                <Star className="size-3" aria-hidden /> {t('businessHours.default')}
              </StatusChip>
            )}
            {!record.active && <StatusChip>{t('businessHours.inactive')}</StatusChip>}
          </h3>
          <p className="text-sm text-muted-foreground">
            <span dir="ltr">{record.timeZone}</span>
            {record.unitCount > 0 && ` · ${t('businessHours.usedBy', { count: record.unitCount })}`}
          </p>
        </div>
        {canManage && (
          <div className="flex flex-wrap gap-2">
            <Button size="sm" variant="outline" onClick={onEdit}>
              {tc('actions.edit')}
            </Button>
            {!record.isDefault && record.active && (
              <Button
                size="sm"
                variant="outline"
                onClick={() => run('make-default', t('businessHours.madeDefault'))}
              >
                {t('businessHours.makeDefault')}
              </Button>
            )}
            {!record.isDefault &&
              (record.active ? (
                <Button size="sm" variant="ghost" onClick={() => setConfirm(true)}>
                  {t('businessHours.deactivate')}
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => run('activate', t('businessHours.activated'))}
                >
                  {t('businessHours.activate')}
                </Button>
              ))}
          </div>
        )}
      </header>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm sm:grid-cols-4 lg:grid-cols-7">
        {record.days.map((day) => (
          <div key={day.day} className="flex flex-col rounded-md bg-muted/50 px-3 py-2">
            <dt className="text-muted-foreground">{weekdayName(day.day, i18n.language)}</dt>
            <dd
              className={day.mode === 'closed' ? 'text-muted-foreground' : 'font-medium'}
              dir={day.mode === 'hours' ? 'ltr' : undefined}
            >
              {dayText(day)}
            </dd>
          </div>
        ))}
      </dl>
      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title={t('businessHours.deactivate')}
        description={t('businessHours.confirmDeactivate', { name })}
        confirmLabel={t('businessHours.deactivate')}
        destructive
        loading={action.isPending}
        onConfirm={() => run('deactivate', t('businessHours.deactivated'))}
      />
    </section>
  );
}

function UnitAssignments({ records }: { records: readonly BusinessHours[] }) {
  const { t, i18n } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canManage = useCan('calendar.business-hours.manage');
  const units = useUnitAssignments();
  const setUnit = useSetUnitBusinessHours();
  const [unitId, setUnitId] = useState<string>();
  const [recordId, setRecordId] = useState<string>();
  const options = records
    .filter((r) => r.active)
    .map((r) => ({ value: r.id, label: pickText(i18n.language, r.nameAr, r.nameEn) }));
  const nameOf = (id: string | null) => {
    const record = records.find((r) => r.id === id);
    return record ? pickText(i18n.language, record.nameAr, record.nameEn) : '—';
  };
  const assign = (unit: string, businessHoursId: string | null) =>
    setUnit.mutate(
      { unitId: unit, businessHoursId },
      {
        onSuccess: () => {
          toast({ title: t('businessHours.units.saved') });
          setUnitId(undefined);
          setRecordId(undefined);
        },
        onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
      },
    );
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={t('businessHours.units.title')}
    >
      <div>
        <h2 className="font-semibold">{t('businessHours.units.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('businessHours.units.description')}</p>
      </div>
      {units.data && units.data.length === 0 && (
        <p className="text-sm text-muted-foreground">{t('businessHours.units.empty')}</p>
      )}
      {units.data && units.data.length > 0 && (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('businessHours.units.unit')}</TableHeaderCell>
              <TableHeaderCell>{t('businessHours.units.record')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('businessHours.units.inherit')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {units.data.map((unit) => (
              <TableRow key={unit.unitId}>
                <TableCell>
                  {pickText(i18n.language, unit.unitNameAr, unit.unitNameEn)}{' '}
                  <span className="text-xs text-muted-foreground" dir="ltr">
                    ({unit.unitCode})
                  </span>
                </TableCell>
                <TableCell>{nameOf(unit.businessHoursId)}</TableCell>
                <TableCell>
                  {canManage && (
                    <Button size="sm" variant="ghost" onClick={() => assign(unit.unitId, null)}>
                      {t('businessHours.units.inherit')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {canManage && (
        <div className="grid items-end gap-3 sm:grid-cols-[1fr_1fr_auto]">
          <FormField label={t('businessHours.units.unit')}>
            <UnitSelect value={unitId} onChange={setUnitId} />
          </FormField>
          <FormField label={t('businessHours.units.record')}>
            <Select value={recordId} onValueChange={setRecordId} options={options} />
          </FormField>
          <Button
            disabled={!unitId || !recordId}
            loading={setUnit.isPending}
            onClick={() => unitId && recordId && assign(unitId, recordId)}
          >
            {t('businessHours.units.add')}
          </Button>
        </div>
      )}
    </section>
  );
}

function Calculator({ records }: { records: readonly BusinessHours[] }) {
  const { t, i18n } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const calculate = useCalculate();
  const [recordId, setRecordId] = useState(records.find((r) => r.isDefault)?.id);
  const [start, setStart] = useState(() => new Date().toISOString().slice(0, 16));
  const [hours, setHours] = useState('8');
  const record = records.find((r) => r.id === recordId);
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={t('businessHours.calculator.title')}
    >
      <div>
        <h2 className="flex items-center gap-2 font-semibold">
          <Clock className="size-4 text-muted-foreground" aria-hidden />
          {t('businessHours.calculator.title')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('businessHours.calculator.description')}</p>
      </div>
      <div className="grid items-end gap-3 sm:grid-cols-[1fr_1fr_8rem_auto]">
        <FormField label={t('businessHours.calculator.record')}>
          <Select
            value={recordId}
            onValueChange={setRecordId}
            options={records
              .filter((r) => r.active)
              .map((r) => ({ value: r.id, label: pickText(i18n.language, r.nameAr, r.nameEn) }))}
          />
        </FormField>
        <FormField label={t('businessHours.calculator.start')}>
          <Input type="datetime-local" dir="ltr" value={start} onChange={(e) => setStart(e.target.value)} />
        </FormField>
        <FormField label={t('businessHours.calculator.hours')}>
          <Input type="number" min={0} dir="ltr" value={hours} onChange={(e) => setHours(e.target.value)} />
        </FormField>
        <Button
          variant="secondary"
          loading={calculate.isPending}
          disabled={!recordId || !start}
          onClick={() =>
            recordId &&
            calculate.mutate({ id: recordId, start: new Date(start).toISOString(), hours: Number(hours) })
          }
        >
          {t('businessHours.calculator.run')}
        </Button>
      </div>
      {calculate.data && record && (
        <p className="text-sm font-medium" role="status">
          {t('businessHours.calculator.result', {
            date: formatDateTime(calculate.data.due, i18n.language, { timeZone: record.timeZone }),
            zone: record.timeZone,
          })}
        </p>
      )}
      {calculate.isError && <StatusBanner tone="danger" title={errorMessage(tc, calculate.error)} />}
    </section>
  );
}

/** Settings → Business hours (ADR 0027): records with their week, units with their own hours, and a deadline calculator. */
export function BusinessHoursPage() {
  const { t } = useTranslation('calendar');
  const { t: tc } = useTranslation();
  const canManage = useCan('calendar.business-hours.manage');
  const records = useBusinessHours();
  const [editing, setEditing] = useState<BusinessHours | 'new'>();
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('businessHours.title')}
        description={t('businessHours.description')}
        actions={
          canManage && (
            <Button onClick={() => setEditing('new')}>
              <Plus aria-hidden />
              {t('businessHours.new')}
            </Button>
          )
        }
      />
      {records.isError && <StatusBanner tone="danger" title={errorMessage(tc, records.error)} />}
      {records.isPending && <Skeleton className="h-48 rounded-lg" />}
      {records.data?.length === 0 && <EmptyState title={t('businessHours.title')} />}
      {records.data?.map((record) => (
        <RecordCard key={record.id} record={record} onEdit={() => setEditing(record)} />
      ))}
      {records.data && <UnitAssignments records={records.data} />}
      {records.data && records.data.length > 0 && <Calculator records={records.data} />}
      {editing && (
        <BusinessHoursDialog
          record={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(undefined)}
        />
      )}
    </div>
  );
}
