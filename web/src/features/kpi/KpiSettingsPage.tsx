import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
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
  Switch,
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
  Textarea,
  useToast,
} from '@/ui';
import {
  currentYear,
  kpiApi,
  useKpiElements,
  useKpiMutation,
  useKpiOptions,
  useKpiSettings,
  useKpiWindows,
  type KpiElement,
  type KpiElementInput,
  type KpiSettingsValue,
  type KpiWindow,
} from './api';
import { useKpiT, yearOptions } from './components';
import { ResponsibilitiesLink } from '@/features/access/ResponsibilitiesLink';

/** Settings → Performance (docs/design/kpi.md §4, §5, §7): general rules, the strategy hierarchy and each year's windows. */
export function KpiSettingsPage() {
  const { t } = useTranslation('kpi');
  const canSettings = useCan('kpi.settings.manage');
  const canStrategy = useCan('kpi.strategy.manage');
  const canPlans = useCan('kpi.plan.manage');
  const tabs = [
    ...(canSettings ? ['general'] : []),
    ...(canStrategy ? ['hierarchy'] : []),
    ...(canPlans ? ['windows'] : []),
  ] as ('general' | 'hierarchy' | 'windows')[];
  const [tab, setTab] = useState<string>();
  return (
    <div className="flex flex-col gap-6" data-testid="kpi-settings">
      <ResponsibilitiesLink system="kpi" />
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <Tabs value={tab ?? tabs[0] ?? 'general'} onValueChange={setTab}>
        <TabsList>
          {tabs.map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`settings.tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="general">{canSettings && <General />}</TabsContent>
        <TabsContent value="hierarchy">{canStrategy && <Hierarchy />}</TabsContent>
        <TabsContent value="windows">{canPlans && <Windows />}</TabsContent>
      </Tabs>
    </div>
  );
}

function General() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k } = useKpiT();
  const toast = useToast();
  const doc = useKpiSettings();
  const options = useKpiOptions();
  const save = useKpiMutation(kpiApi.saveSettings);
  const [value, setValue] = useState<KpiSettingsValue>();
  useEffect(() => {
    if (doc.data?.value) setValue(doc.data.value);
  }, [doc.data]);
  if (doc.isError) return <StatusBanner tone="danger" title={errorMessage(tc, doc.error)} />;
  if (!value || !doc.data) return <Skeleton className="h-64" />;
  const set = (patch: Partial<KpiSettingsValue>) => setValue((v) => ({ ...v, ...patch }));
  const rules = value.windowRules ?? [];
  const methods = value.measuringMethods ?? [];
  const checklist = value.checklist ?? [];
  const methodCodes = (options.data?.lookups['kpi.measuring-method'] ?? []).map((o) => o.code);
  return (
    <div className="flex flex-col gap-6">
      <section className="grid gap-4 rounded-lg border bg-card p-5 md:grid-cols-2">
        <FormField label={t('settings.reminder')}>
          <Input
            type="number"
            value={value.reminderWorkingDaysBefore ?? 0}
            onChange={(e) => set({ reminderWorkingDaysBefore: Number(e.target.value) })}
          />
        </FormField>
        <FormField label={t('settings.escalation')}>
          <Input
            type="number"
            value={value.escalationWorkingDaysAfter ?? 0}
            onChange={(e) => set({ escalationWorkingDaysAfter: Number(e.target.value) })}
          />
        </FormField>
        <FormField label={t('settings.green')}>
          <Input
            type="number"
            value={Math.round((value.greenFrom ?? 0) * 100)}
            onChange={(e) => set({ greenFrom: Number(e.target.value) / 100 })}
          />
        </FormField>
        <FormField label={t('settings.amber')}>
          <Input
            type="number"
            value={Math.round((value.amberFrom ?? 0) * 100)}
            onChange={(e) => set({ amberFrom: Number(e.target.value) / 100 })}
          />
        </FormField>
        <FormField label={t('settings.jump')}>
          <Input
            type="number"
            value={Math.round((value.anomalyJumpRatio ?? 0) * 100)}
            onChange={(e) => set({ anomalyJumpRatio: Number(e.target.value) / 100 })}
          />
        </FormField>
        <div className="flex flex-col gap-3">
          <Switch
            label={t('settings.requireEvidence')}
            checked={!!value.requireEvidence}
            onCheckedChange={(c) => set({ requireEvidence: c })}
          />
          <Switch
            label={t('settings.requireConfirmation')}
            checked={!!value.requirePlanConfirmation}
            onCheckedChange={(c) => set({ requirePlanConfirmation: c })}
          />
          <Switch
            label={t('settings.manualCodes')}
            checked={!!value.manualCodes}
            onCheckedChange={(c) => set({ manualCodes: c })}
          />
          <Switch
            label={t('settings.missedAmber')}
            checked={!!value.missedIncludesAmber}
            onCheckedChange={(c) => set({ missedIncludesAmber: c })}
          />
        </div>
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <h2 className="font-semibold">{t('settings.windowRules')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.windowRulesHint')}</p>
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.period')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.opensAfter')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.dueAfter')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rules.map((r, i) => (
              <TableRow key={r.periodCode}>
                <TableCell>{k(`period.${r.periodCode}`)}</TableCell>
                <TableCell>
                  <Input
                    type="number"
                    aria-label={t('settings.opensAfter')}
                    value={r.opensDaysAfterEnd}
                    onChange={(e) =>
                      set({
                        windowRules: rules.map((x, j) =>
                          j === i ? { ...x, opensDaysAfterEnd: Number(e.target.value) } : x,
                        ),
                      })
                    }
                  />
                </TableCell>
                <TableCell>
                  <Input
                    type="number"
                    aria-label={t('settings.dueAfter')}
                    value={r.dueDaysAfterOpen}
                    onChange={(e) =>
                      set({
                        windowRules: rules.map((x, j) =>
                          j === i ? { ...x, dueDaysAfterOpen: Number(e.target.value) } : x,
                        ),
                      })
                    }
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <h2 className="font-semibold">{t('settings.methods')}</h2>
        {methods.map((m, i) => (
          <div key={i} className="grid grid-cols-[1fr_1fr_auto] gap-2">
            <Select
              aria-label={t('fields.measuringMethodCode')}
              value={m.code}
              onValueChange={(code) =>
                set({ measuringMethods: methods.map((x, j) => (j === i ? { ...x, code } : x)) })
              }
              options={[...new Set([m.code, ...methodCodes])]
                .filter(Boolean)
                .map((c) => ({ value: c, label: c }))}
            />
            <Select
              aria-label={t('settings.rule')}
              value={m.rule}
              onValueChange={(rule) =>
                set({ measuringMethods: methods.map((x, j) => (j === i ? { ...x, rule } : x)) })
              }
              options={['sum', 'average'].map((r) => ({ value: r, label: k(`aggregation.${r}`) }))}
            />
            <Button
              variant="ghost"
              size="icon"
              aria-label={t('actions.remove')}
              onClick={() => set({ measuringMethods: methods.filter((_, j) => j !== i) })}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        ))}
        <Button
          variant="outline"
          size="sm"
          className="w-fit"
          onClick={() => set({ measuringMethods: [...methods, { code: methodCodes[0] ?? '', rule: 'sum' }] })}
        >
          <Plus aria-hidden />
          {t('settings.addMethod')}
        </Button>
      </section>
      <section
        className="flex flex-col gap-3 rounded-lg border bg-card p-5"
        data-testid="kpi-settings-checklist"
      >
        <h2 className="font-semibold">{t('settings.checklist')}</h2>
        {checklist.map((c, i) => (
          <div key={i} className="grid gap-2 md:grid-cols-[10rem_1fr_1fr_auto_auto] md:items-center">
            <Input
              dir="ltr"
              aria-label={t('settings.code')}
              value={c.code}
              onChange={(e) =>
                set({ checklist: checklist.map((x, j) => (j === i ? { ...x, code: e.target.value } : x)) })
              }
            />
            <Input
              dir="rtl"
              aria-label={t('fields.nameAr')}
              value={c.labelAr}
              onChange={(e) =>
                set({ checklist: checklist.map((x, j) => (j === i ? { ...x, labelAr: e.target.value } : x)) })
              }
            />
            <Input
              dir="ltr"
              aria-label={t('fields.nameEn')}
              value={c.labelEn}
              onChange={(e) =>
                set({ checklist: checklist.map((x, j) => (j === i ? { ...x, labelEn: e.target.value } : x)) })
              }
            />
            <Checkbox
              label={t('settings.required')}
              checked={c.required}
              onChange={(e) =>
                set({
                  checklist: checklist.map((x, j) => (j === i ? { ...x, required: e.target.checked } : x)),
                })
              }
            />
            <Button
              variant="ghost"
              size="icon"
              aria-label={t('actions.remove')}
              onClick={() => set({ checklist: checklist.filter((_, j) => j !== i) })}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        ))}
        <Button
          variant="outline"
          size="sm"
          className="w-fit"
          onClick={() =>
            set({ checklist: [...checklist, { code: '', labelAr: '', labelEn: '', required: true }] })
          }
        >
          <Plus aria-hidden />
          {t('settings.addItem')}
        </Button>
      </section>
      <Button
        className="w-fit"
        loading={save.isPending}
        data-testid="kpi-settings-save"
        onClick={() =>
          save.mutate(
            { settings: value, version: doc.data.version },
            {
              onSuccess: () => toast({ title: t('settings.saved') }),
              onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
            },
          )
        }
      >
        {tc('actions.save')}
      </Button>
    </div>
  );
}

const emptyElement = (kind: string, parentId: string | null): KpiElementInput => ({
  id: null,
  kind,
  code: '',
  nameAr: null,
  nameEn: null,
  descriptionAr: null,
  descriptionEn: null,
  parentId,
  weight: null,
  sort: 0,
  active: true,
  cycleStartYear: null,
  cycleEndYear: null,
  version: null,
});

function Hierarchy() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const elements = useKpiElements();
  const [editing, setEditing] = useState<KpiElementInput>();
  if (elements.isError) return <StatusBanner tone="danger" title={errorMessage(tc, elements.error)} />;
  if (!elements.data) return <Skeleton className="h-64" />;
  const children = (parentId: string | null, kind?: string) =>
    elements.data
      .filter((e) => e.parentId === parentId && (!kind || e.kind === kind))
      .sort((a, b) => a.sort - b.sort || a.code.localeCompare(b.code));
  const edit = (e: KpiElement) => setEditing({ ...e, version: e.version });
  const row = (e: KpiElement, depth: number) => (
    <li key={e.id} className="flex flex-col">
      <div
        className="flex flex-wrap items-center justify-between gap-2 rounded-md px-2 py-2 hover:bg-muted/50"
        style={{ paddingInlineStart: `${depth * 1.5 + 0.5}rem` }}
        data-testid="kpi-element-row"
      >
        <span className="flex flex-wrap items-center gap-2">
          <StatusChip tone="neutral">{k(`elementKind.${e.kind}`)}</StatusChip>
          <bdi className="text-xs text-muted-foreground">{e.code}</bdi>
          <span className="font-medium">{pickText(language, e.nameAr, e.nameEn)}</span>
          {e.weight != null && (
            <span className="text-xs text-muted-foreground">
              ({t('scorecard.weight', { weight: e.weight })})
            </span>
          )}
          {!e.active && <StatusChip tone="warning">{t('settings.inactive')}</StatusChip>}
          {e.kind === 'objective' && (
            <span className="text-xs text-muted-foreground">
              {t('common.kpiCount', { count: e.cardCount, formatted: String(e.cardCount) })}
            </span>
          )}
        </span>
        <span className="flex gap-1">
          {e.kind !== 'objective' && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() =>
                setEditing(emptyElement(e.kind === 'vision' ? 'perspective' : 'objective', e.id))
              }
            >
              <Plus aria-hidden />
              {k(`settings.add.${e.kind === 'vision' ? 'perspective' : 'objective'}`)}
            </Button>
          )}
          <Button variant="ghost" size="sm" onClick={() => edit(e)}>
            {t('actions.edit')}
          </Button>
        </span>
      </div>
      {e.kind !== 'objective' && <ul>{children(e.id).map((c) => row(c, depth + 1))}</ul>}
    </li>
  );
  return (
    <div className="flex flex-col gap-4" data-testid="kpi-hierarchy">
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" onClick={() => setEditing(emptyElement('vision', null))}>
          <Plus aria-hidden />
          {t('settings.add.vision')}
        </Button>
        <Button
          variant="outline"
          onClick={() => setEditing(emptyElement('perspective', null))}
          data-testid="kpi-add-perspective"
        >
          <Plus aria-hidden />
          {t('settings.add.perspective')}
        </Button>
      </div>
      <ul className="rounded-lg border bg-card p-2">{children(null).map((e) => row(e, 0))}</ul>
      {editing && (
        <ElementDialog value={editing} elements={elements.data} onClose={() => setEditing(undefined)} />
      )}
    </div>
  );
}

function ElementDialog({
  value,
  elements,
  onClose,
}: {
  value: KpiElementInput;
  elements: KpiElement[];
  onClose: () => void;
}) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const toast = useToast();
  const save = useKpiMutation(kpiApi.saveElement);
  const [v, setV] = useState(value);
  const parentKind = v.kind === 'objective' ? 'perspective' : v.kind === 'perspective' ? 'vision' : null;
  const n = (s: string) => (s === '' ? null : Number(s));
  const NONE = '-';
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={k(`elementKind.${v.kind}`)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              data-testid="kpi-element-save"
              onClick={() =>
                save.mutate(v, {
                  onSuccess: onClose,
                  onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                })
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('settings.code')} required>
            <Input
              dir="ltr"
              value={v.code}
              onChange={(e) => setV({ ...v, code: e.target.value })}
              data-testid="kpi-element-code"
            />
          </FormField>
          {parentKind && (
            <FormField label={t('settings.parent')}>
              <Select
                value={v.parentId ?? NONE}
                onValueChange={(p) => setV({ ...v, parentId: p === NONE ? null : p })}
                options={[
                  ...(v.kind === 'perspective' ? [{ value: NONE, label: t('form.none') }] : []),
                  ...elements
                    .filter((e) => e.kind === parentKind)
                    .map((e) => ({
                      value: e.id,
                      label: `${e.code} · ${pickText(language, e.nameAr, e.nameEn)}`,
                    })),
                ]}
              />
            </FormField>
          )}
          <FormField label={t('fields.nameAr')}>
            <Input
              dir="rtl"
              value={v.nameAr ?? ''}
              onChange={(e) => setV({ ...v, nameAr: e.target.value || null })}
              data-testid="kpi-element-name-ar"
            />
          </FormField>
          <FormField label={t('fields.nameEn')}>
            <Input
              dir="ltr"
              value={v.nameEn ?? ''}
              onChange={(e) => setV({ ...v, nameEn: e.target.value || null })}
              data-testid="kpi-element-name-en"
            />
          </FormField>
          <FormField label={t('fields.descriptionAr')}>
            <Textarea
              dir="rtl"
              value={v.descriptionAr ?? ''}
              onChange={(e) => setV({ ...v, descriptionAr: e.target.value || null })}
            />
          </FormField>
          <FormField label={t('fields.descriptionEn')}>
            <Textarea
              dir="ltr"
              value={v.descriptionEn ?? ''}
              onChange={(e) => setV({ ...v, descriptionEn: e.target.value || null })}
            />
          </FormField>
          <FormField label={t('fields.weight')}>
            <Input
              type="number"
              value={v.weight ?? ''}
              onChange={(e) => setV({ ...v, weight: n(e.target.value) })}
            />
          </FormField>
          <FormField label={t('settings.sort')}>
            <Input
              type="number"
              value={v.sort}
              onChange={(e) => setV({ ...v, sort: Number(e.target.value) })}
            />
          </FormField>
          {v.kind === 'vision' && (
            <>
              <FormField label={t('settings.cycleStart')}>
                <Input
                  type="number"
                  value={v.cycleStartYear ?? ''}
                  onChange={(e) => setV({ ...v, cycleStartYear: n(e.target.value) })}
                />
              </FormField>
              <FormField label={t('settings.cycleEnd')}>
                <Input
                  type="number"
                  value={v.cycleEndYear ?? ''}
                  onChange={(e) => setV({ ...v, cycleEndYear: n(e.target.value) })}
                />
              </FormField>
            </>
          )}
          <Checkbox
            label={t('settings.active')}
            checked={v.active}
            onChange={(e) => setV({ ...v, active: e.target.checked })}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function Windows() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k } = useKpiT();
  const toast = useToast();
  const [year, setYear] = useState(currentYear());
  const windows = useKpiWindows(year);
  const save = useKpiMutation(kpiApi.saveWindows);
  const lock = useKpiMutation(kpiApi.lockPeriod);
  const [rows, setRows] = useState<KpiWindow[]>([]);
  const [locking, setLocking] = useState<KpiWindow>();
  const [reason, setReason] = useState('');
  useEffect(() => {
    if (windows.data) setRows(windows.data);
  }, [windows.data]);
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });
  return (
    <div className="flex flex-col gap-4" data-testid="kpi-windows">
      <FormField label={t('common.year')} className="max-w-40">
        <Select
          value={String(year)}
          onValueChange={(v) => setYear(Number(v))}
          options={yearOptions(currentYear(), 2, 2)}
        />
      </FormField>
      {!windows.data ? (
        <Skeleton className="h-40" />
      ) : rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('settings.noWindows')}</p>
      ) : (
        <>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.period')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.opensOn')}</TableHeaderCell>
                <TableHeaderCell>{t('result.dueOn')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((w, i) => (
                <TableRow key={w.periodCode}>
                  <TableCell>{k(`period.${w.periodCode}`)}</TableCell>
                  <TableCell>
                    <Input
                      type="date"
                      aria-label={t('settings.opensOn')}
                      value={w.opensOn}
                      onChange={(e) =>
                        setRows((r) => r.map((x, j) => (j === i ? { ...x, opensOn: e.target.value } : x)))
                      }
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      type="date"
                      aria-label={t('result.dueOn')}
                      value={w.dueOn}
                      onChange={(e) =>
                        setRows((r) => r.map((x, j) => (j === i ? { ...x, dueOn: e.target.value } : x)))
                      }
                    />
                  </TableCell>
                  <TableCell>
                    <span className="flex items-center gap-2">
                      {w.locked && <StatusChip tone="neutral">{t('settings.locked')}</StatusChip>}
                      <Button variant="ghost" size="sm" onClick={() => setLocking(w)}>
                        {w.locked ? t('settings.reopenPeriod') : t('settings.lockPeriod')}
                      </Button>
                    </span>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Button
            className="w-fit"
            loading={save.isPending}
            onClick={() =>
              save.mutate(
                {
                  year,
                  windows: rows.map(({ periodCode, opensOn, dueOn }) => ({ periodCode, opensOn, dueOn })),
                },
                { onSuccess: () => toast({ title: t('settings.saved') }), onError },
              )
            }
          >
            {tc('actions.save')}
          </Button>
        </>
      )}
      {locking && (
        <Dialog open onOpenChange={(open) => !open && setLocking(undefined)}>
          <DialogContent
            title={locking.locked ? t('settings.reopenPeriod') : t('settings.lockPeriod')}
            description={k(`period.${locking.periodCode}`)}
            footer={
              <>
                <DialogClose asChild>
                  <Button variant="outline">{tc('actions.cancel')}</Button>
                </DialogClose>
                <Button
                  loading={lock.isPending}
                  disabled={locking.locked && !reason.trim()}
                  onClick={() =>
                    lock.mutate(
                      { year, period: locking.periodCode, lock: !locking.locked, reason: reason || null },
                      {
                        onSuccess: () => {
                          setLocking(undefined);
                          setReason('');
                        },
                        onError,
                      },
                    )
                  }
                >
                  {tc('actions.confirm')}
                </Button>
              </>
            }
          >
            <FormField label={t('reopen.reason')} required={locking.locked}>
              <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
          </DialogContent>
        </Dialog>
      )}
    </div>
  );
}
