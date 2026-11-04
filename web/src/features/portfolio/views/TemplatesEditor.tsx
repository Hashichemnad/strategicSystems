import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
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
  useToast,
} from '@/ui';
import { FieldErrors } from '../project/FieldErrors';
import {
  methodologies,
  planningCategories,
  useProjectTemplates,
  useSaveProjectTemplate,
  useSetTemplateActive,
  type ProjectTemplate,
  type ProjectTemplateInput,
  type TemplateEpic,
  type TemplateMilestone,
  type TemplatePlanningItem,
  type TemplateRisk,
} from './viewsApi';

/** Settings → Portfolio → Project templates (docs/design/portfolio.md §35): milestones, planning items, risks, epics. */
export function TemplatesEditor() {
  const { t, i18n } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const templates = useProjectTemplates();
  const setActive = useSetTemplateActive();
  const [editing, setEditing] = useState<ProjectTemplate | 'new' | null>(null);
  const n = (v: number) => formatNumber(v, i18n.language);

  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-6" data-testid="portfolio-templates">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('templates.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('templates.description')}</p>
        </div>
        <Button variant="outline" data-testid="template-new" onClick={() => setEditing('new')}>
          <Plus aria-hidden />
          {t('templates.new')}
        </Button>
      </div>
      {templates.isPending ? (
        <Skeleton className="h-32 rounded-lg" />
      ) : templates.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, templates.error)} />
      ) : templates.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('templates.empty')}</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('templates.code')}</TableHeaderCell>
                <TableHeaderCell>{t('templates.name')}</TableHeaderCell>
                <TableHeaderCell>{t('templates.methodology')}</TableHeaderCell>
                <TableHeaderCell>{t('templates.contents')}</TableHeaderCell>
                <TableHeaderCell>{t('templates.status')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{t('templates.actions')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {templates.data.map((tpl) => {
                const active = tpl.status === 'active';
                return (
                  <TableRow key={tpl.id} data-testid="template-row">
                    <TableCell dir="ltr">{tpl.code}</TableCell>
                    <TableCell>{pickText(i18n.language, tpl.nameAr, tpl.nameEn)}</TableCell>
                    <TableCell>
                      {t(`methodology.${tpl.methodology}`, { defaultValue: tpl.methodology })}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {t('templates.counts', {
                        milestones: n(tpl.content.milestones?.length ?? 0),
                        items: n(tpl.content.planningItems?.length ?? 0),
                        risks: n(tpl.content.risks?.length ?? 0),
                        epics: n(tpl.content.epics?.length ?? 0),
                      })}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={active ? 'success' : 'neutral'}>
                        {active ? t('templates.active') : t('templates.inactive')}
                      </StatusChip>
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={t('templates.editNamed', { code: tpl.code })}
                          onClick={() => setEditing(tpl)}
                        >
                          <Pencil aria-hidden />
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          loading={setActive.isPending && setActive.variables.id === tpl.id}
                          onClick={() =>
                            setActive.mutate(
                              { id: tpl.id, active: !active },
                              { onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) },
                            )
                          }
                        >
                          {active ? t('templates.deactivate') : t('templates.activate')}
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && (
        <TemplateDialog template={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />
      )}
    </section>
  );
}

const emptyMilestone = (): TemplateMilestone => ({
  titleAr: '',
  titleEn: '',
  weight: 0,
  offsetDays: 0,
  durationDays: 30,
  isHeader: false,
  parentIndex: null,
});

function num(value: string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function TemplateDialog({
  template,
  onClose,
}: {
  template?: ProjectTemplate | undefined;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useSaveProjectTemplate();
  const [input, setInput] = useState<ProjectTemplateInput>(() => ({
    code: template?.code ?? '',
    nameAr: template?.nameAr ?? '',
    nameEn: template?.nameEn ?? '',
    methodology: template?.methodology ?? 'waterfall',
    content: {
      milestones: template?.content.milestones ?? [],
      planningItems: template?.content.planningItems ?? [],
      risks: template?.content.risks ?? [],
      epics: template?.content.epics ?? [],
    },
    version: template?.version ?? null,
  }));
  const milestones = input.content.milestones ?? [];
  const items = input.content.planningItems ?? [];
  const risks = input.content.risks ?? [];
  const epics = input.content.epics ?? [];
  const setContent = (patch: Partial<ProjectTemplateInput['content']>) =>
    setInput((v) => ({ ...v, content: { ...v.content, ...patch } }));
  const editMilestone = (i: number, patch: Partial<TemplateMilestone>) =>
    setContent({ milestones: milestones.map((m, j) => (j === i ? { ...m, ...patch } : m)) });
  const editItem = (i: number, patch: Partial<TemplatePlanningItem>) =>
    setContent({ planningItems: items.map((m, j) => (j === i ? { ...m, ...patch } : m)) });
  const editRisk = (i: number, patch: Partial<TemplateRisk>) =>
    setContent({ risks: risks.map((m, j) => (j === i ? { ...m, ...patch } : m)) });
  const editEpic = (i: number, patch: Partial<TemplateEpic>) =>
    setContent({ epics: epics.map((m, j) => (j === i ? { ...m, ...patch } : m)) });
  const pctValue = (ratio: number) => String(Math.round(ratio * 10000) / 100);
  const weightSum = milestones.filter((m) => !m.isHeader).reduce((s, m) => s + (m.weight || 0), 0);
  const epicSum = epics.reduce((s, e) => s + (e.weight || 0), 0);
  const sumText = (ratio: number) =>
    t('percent', { value: formatNumber(Math.round(ratio * 10000) / 100, i18n.language) });
  const headers = milestones
    .map((m, i) => ({ m, i }))
    .filter(({ m }) => m.isHeader)
    .map(({ m, i }) => ({
      value: String(i),
      label: `${i + 1}. ${pickText(i18n.language, m.titleAr, m.titleEn) || '—'}`,
    }));

  const submit = () =>
    save.mutate(
      {
        id: template?.id,
        input: {
          ...input,
          content: {
            ...input.content,
            milestones: milestones.map((m) => (m.isHeader ? { ...m, weight: 0, parentIndex: null } : m)),
          },
        },
      },
      {
        onSuccess: () => {
          toast({ title: t('templates.saved') });
          onClose();
        },
      },
    );

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={template ? t('templates.edit') : t('templates.new')}
        className="max-w-5xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!input.code.trim() || (!input.nameAr?.trim() && !input.nameEn?.trim())}
              data-testid="template-save"
              onClick={submit}
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-5" data-testid="template-dialog">
          <div className="grid gap-3 md:grid-cols-4">
            <FormField label={t('templates.code')} required>
              <Input
                dir="ltr"
                value={input.code}
                onChange={(e) => setInput((v) => ({ ...v, code: e.target.value }))}
              />
            </FormField>
            <FormField label={t('templates.nameAr')}>
              <Input
                dir="rtl"
                lang="ar"
                value={input.nameAr ?? ''}
                onChange={(e) => setInput((v) => ({ ...v, nameAr: e.target.value }))}
              />
            </FormField>
            <FormField label={t('templates.nameEn')}>
              <Input
                dir="ltr"
                lang="en"
                value={input.nameEn ?? ''}
                onChange={(e) => setInput((v) => ({ ...v, nameEn: e.target.value }))}
              />
            </FormField>
            <FormField label={t('templates.methodology')}>
              <Select
                value={input.methodology}
                onValueChange={(v) => setInput((x) => ({ ...x, methodology: v }))}
                options={methodologies.map((m) => ({ value: m, label: t(`methodology.${m}`) }))}
              />
            </FormField>
          </div>

          <Rows
            title={t('templates.milestones')}
            summary={t('templates.weightSum', { value: sumText(weightSum) })}
            warn={weightSum > 1.0001}
            addLabel={t('templates.addMilestone')}
            onAdd={() => setContent({ milestones: [...milestones, emptyMilestone()] })}
            testId="template-milestones"
          >
            {milestones.map((m, i) => (
              <div
                key={i}
                className="grid items-end gap-2 rounded-md border p-2 md:grid-cols-[2rem_1fr_1fr_5rem_5rem_5rem_auto_10rem_auto]"
                data-testid="template-milestone-row"
              >
                <span className="pb-2 text-xs text-muted-foreground tabular-nums">{i + 1}</span>
                <FormField label={t('templates.titleAr')}>
                  <Input
                    dir="rtl"
                    value={m.titleAr ?? ''}
                    onChange={(e) => editMilestone(i, { titleAr: e.target.value })}
                  />
                </FormField>
                <FormField label={t('templates.titleEn')}>
                  <Input
                    dir="ltr"
                    value={m.titleEn ?? ''}
                    onChange={(e) => editMilestone(i, { titleEn: e.target.value })}
                  />
                </FormField>
                <FormField label={t('templates.weight')}>
                  <Input
                    dir="ltr"
                    inputMode="decimal"
                    disabled={m.isHeader}
                    value={m.isHeader ? '' : pctValue(m.weight)}
                    onChange={(e) => editMilestone(i, { weight: num(e.target.value) / 100 })}
                  />
                </FormField>
                <FormField label={t('templates.offsetDays')}>
                  <Input
                    dir="ltr"
                    inputMode="numeric"
                    value={String(m.offsetDays)}
                    onChange={(e) => editMilestone(i, { offsetDays: Math.round(num(e.target.value)) })}
                  />
                </FormField>
                <FormField label={t('templates.durationDays')}>
                  <Input
                    dir="ltr"
                    inputMode="numeric"
                    value={String(m.durationDays)}
                    onChange={(e) => editMilestone(i, { durationDays: Math.round(num(e.target.value)) })}
                  />
                </FormField>
                <div className="pb-2">
                  <Checkbox
                    label={t('templates.header')}
                    checked={m.isHeader === true}
                    onChange={(e) => editMilestone(i, { isHeader: e.target.checked })}
                  />
                </div>
                <FormField label={t('templates.parent')}>
                  <Select
                    value={
                      m.parentIndex === null || m.parentIndex === undefined ? 'none' : String(m.parentIndex)
                    }
                    disabled={m.isHeader}
                    onValueChange={(v) => editMilestone(i, { parentIndex: v === 'none' ? null : Number(v) })}
                    options={[
                      { value: 'none', label: t('templates.noParent') },
                      ...headers.filter((h) => h.value !== String(i)),
                    ]}
                  />
                </FormField>
                <RemoveButton
                  label={t('templates.remove')}
                  onClick={() =>
                    setContent({
                      milestones: milestones
                        .filter((_, j) => j !== i)
                        .map((x) =>
                          x.parentIndex === null || x.parentIndex === undefined
                            ? x
                            : x.parentIndex === i
                              ? { ...x, parentIndex: null }
                              : x.parentIndex > i
                                ? { ...x, parentIndex: x.parentIndex - 1 }
                                : x,
                        ),
                    })
                  }
                />
              </div>
            ))}
          </Rows>

          <Rows
            title={t('templates.planningItems')}
            addLabel={t('templates.addPlanningItem')}
            onAdd={() => setContent({ planningItems: [...items, { category: 'in-scope', description: '' }] })}
            testId="template-planning"
          >
            {items.map((p, i) => (
              <div key={i} className="grid items-end gap-2 md:grid-cols-[12rem_1fr_auto]">
                <FormField label={t('templates.category')}>
                  <Select
                    value={p.category}
                    onValueChange={(v) => editItem(i, { category: v })}
                    options={planningCategories.map((c) => ({
                      value: c,
                      label: t(`templates.categories.${c}`),
                    }))}
                  />
                </FormField>
                <FormField label={t('templates.itemDescription')}>
                  <Input
                    value={p.description}
                    onChange={(e) => editItem(i, { description: e.target.value })}
                  />
                </FormField>
                <RemoveButton
                  label={t('templates.remove')}
                  onClick={() => setContent({ planningItems: items.filter((_, j) => j !== i) })}
                />
              </div>
            ))}
          </Rows>

          <Rows
            title={t('templates.risks')}
            addLabel={t('templates.addRisk')}
            onAdd={() =>
              setContent({ risks: [...risks, { title: '', categoryCode: null, probability: 3, impact: 3 }] })
            }
            testId="template-risks"
          >
            {risks.map((r, i) => (
              <div key={i} className="grid items-end gap-2 md:grid-cols-[1fr_10rem_6rem_6rem_auto]">
                <FormField label={t('templates.riskTitle')}>
                  <Input value={r.title} onChange={(e) => editRisk(i, { title: e.target.value })} />
                </FormField>
                <FormField label={t('templates.riskCategory')}>
                  <Input
                    dir="ltr"
                    value={r.categoryCode ?? ''}
                    onChange={(e) => editRisk(i, { categoryCode: e.target.value || null })}
                  />
                </FormField>
                <FormField label={t('templates.probability')}>
                  <Select
                    value={String(r.probability)}
                    onValueChange={(v) => editRisk(i, { probability: Number(v) })}
                    options={scale}
                  />
                </FormField>
                <FormField label={t('templates.impact')}>
                  <Select
                    value={String(r.impact)}
                    onValueChange={(v) => editRisk(i, { impact: Number(v) })}
                    options={scale}
                  />
                </FormField>
                <RemoveButton
                  label={t('templates.remove')}
                  onClick={() => setContent({ risks: risks.filter((_, j) => j !== i) })}
                />
              </div>
            ))}
          </Rows>

          <Rows
            title={t('templates.epics')}
            summary={t('templates.weightSum', { value: sumText(epicSum) })}
            warn={epicSum > 1.0001}
            addLabel={t('templates.addEpic')}
            onAdd={() => setContent({ epics: [...epics, { titleAr: '', titleEn: '', weight: 0 }] })}
            testId="template-epics"
          >
            {epics.map((e, i) => (
              <div key={i} className="grid items-end gap-2 md:grid-cols-[1fr_1fr_6rem_auto]">
                <FormField label={t('templates.titleAr')}>
                  <Input
                    dir="rtl"
                    value={e.titleAr ?? ''}
                    onChange={(ev) => editEpic(i, { titleAr: ev.target.value })}
                  />
                </FormField>
                <FormField label={t('templates.titleEn')}>
                  <Input
                    dir="ltr"
                    value={e.titleEn ?? ''}
                    onChange={(ev) => editEpic(i, { titleEn: ev.target.value })}
                  />
                </FormField>
                <FormField label={t('templates.weight')}>
                  <Input
                    dir="ltr"
                    inputMode="decimal"
                    value={pctValue(e.weight)}
                    onChange={(ev) => editEpic(i, { weight: num(ev.target.value) / 100 })}
                  />
                </FormField>
                <RemoveButton
                  label={t('templates.remove')}
                  onClick={() => setContent({ epics: epics.filter((_, j) => j !== i) })}
                />
              </div>
            ))}
          </Rows>

          {save.isError && <FieldErrors error={save.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

const scale = [1, 2, 3, 4, 5].map((v) => ({ value: String(v), label: String(v) }));

function Rows({
  title,
  summary,
  warn,
  addLabel,
  onAdd,
  testId,
  children,
}: {
  title: string;
  summary?: string;
  warn?: boolean;
  addLabel: string;
  onAdd: () => void;
  testId: string;
  children: ReactNode;
}) {
  return (
    <fieldset className="flex flex-col gap-2" data-testid={testId}>
      <legend className="flex w-full flex-wrap items-center justify-between gap-2 font-medium">
        <span>{title}</span>
        {summary && (
          <span
            className={warn ? 'text-sm text-danger' : 'text-sm text-muted-foreground'}
            role={warn ? 'alert' : undefined}
          >
            {summary}
          </span>
        )}
      </legend>
      {children}
      <div>
        <Button variant="outline" size="sm" onClick={onAdd}>
          <Plus aria-hidden />
          {addLabel}
        </Button>
      </div>
    </fieldset>
  );
}

function RemoveButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <Button variant="ghost" size="sm" aria-label={label} onClick={onClick}>
      <Trash2 aria-hidden />
    </Button>
  );
}
