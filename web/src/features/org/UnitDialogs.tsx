import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { api, unwrap } from '@/api/client';
import { applyFieldErrors, errorMessage, translateFieldMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Select,
  useToast,
} from '@/ui';
import {
  nullIfEmpty,
  todayIso,
  useInvalidateOrg,
  useLevels,
  useUnitTree,
  type OrgTreeNode,
  type OrgUnit,
} from './api';
import { EmployeePicker } from './EmployeePicker';

const maxLength = (max: number) => `validation.maximumLength|${JSON.stringify({ maxLength: max })}`;

const schema = z
  .object({
    code: z
      .string()
      .trim()
      .min(1, 'validation.notEmpty')
      .max(50, maxLength(50))
      .regex(/^[A-Za-z0-9][A-Za-z0-9_.-]*$/u, 'validation.codeFormat'),
    nameAr: z.string().trim().max(200, maxLength(200)),
    nameEn: z.string().trim().max(200, maxLength(200)),
    levelCode: z.string().min(1, 'validation.notEmpty'),
    headEmployeeId: z.string().optional(),
    deputyEmployeeId: z.string().optional(),
    costCentre: z.string().trim().max(50, maxLength(50)),
    effectiveDate: z.string().optional(),
  })
  .refine((value) => value.nameAr !== '' || value.nameEn !== '', {
    path: ['nameAr'],
    message: 'validation.atLeastOneLanguage',
  });

type FormValues = z.infer<typeof schema>;
const fields = [
  'code',
  'nameAr',
  'nameEn',
  'levelCode',
  'headEmployeeId',
  'deputyEmployeeId',
  'costCentre',
  'effectiveDate',
] as const;

interface UnitDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Edit this unit; omit to create. */
  unit?: OrgUnit;
  /** Parent of a new unit (none = a root unit). */
  parent?: OrgTreeNode | undefined;
  onSaved: (unit: OrgUnit) => void;
}

/** Create or edit a unit. Every change takes effect on a date, which opens a new version in the unit's history. */
export function UnitDialog({ open, onOpenChange, unit, parent, onSaved }: UnitDialogProps) {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const { t: te } = useTranslation('errors');
  const levels = useLevels();
  const invalidate = useInvalidateOrg();
  const toast = useToast();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: unit?.code ?? '',
      nameAr: unit?.nameAr ?? '',
      nameEn: unit?.nameEn ?? '',
      levelCode: unit?.levelCode ?? '',
      headEmployeeId: unit?.head?.id,
      deputyEmployeeId: unit?.deputy?.id,
      costCentre: unit?.costCentre ?? '',
      effectiveDate: todayIso(),
    },
  });
  const { errors, isSubmitting } = form.formState;
  const error = (name: (typeof fields)[number]) => translateFieldMessage(tc, errors[name]?.message);

  const submit = form.handleSubmit(async (values) => {
    const body = {
      code: values.code,
      nameAr: nullIfEmpty(values.nameAr),
      nameEn: nullIfEmpty(values.nameEn),
      levelCode: values.levelCode,
      headEmployeeId: values.headEmployeeId ?? null,
      deputyEmployeeId: values.deputyEmployeeId ?? null,
      costCentre: nullIfEmpty(values.costCentre),
    };
    try {
      const saved = unit
        ? unwrap(
            await api.PUT('/api/v1/org/units/{id}', {
              params: { path: { id: unit.id } },
              body: { ...body, effectiveDate: values.effectiveDate ?? null, version: unit.version },
            }),
          )
        : unwrap(
            await api.POST('/api/v1/org/units', {
              body: { ...body, parentId: parent?.id ?? null, effectiveFrom: values.effectiveDate ?? null },
            }),
          );
      toast({ title: unit ? t('units.updated') : t('units.created') });
      await invalidate();
      onSaved(saved);
    } catch (failure) {
      if (!applyFieldErrors(failure, form.setError, fields)) {
        toast({ tone: 'error', title: te('title'), description: errorMessage(tc, failure) });
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={
          unit
            ? t('units.editTitle')
            : parent
              ? t('units.createChildTitle', { parent: pickText(i18n.language, parent.nameAr, parent.nameEn) })
              : t('units.createRoot')
        }
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button type="submit" form="unit-form" loading={isSubmitting}>
              {unit ? tc('actions.save') : tc('actions.create')}
            </Button>
          </>
        }
      >
        <form
          id="unit-form"
          noValidate
          onSubmit={(event) => void submit(event)}
          className="grid gap-4 sm:grid-cols-2"
        >
          <FormField label={t('fields.code')} error={error('code')} required hint={t('hints.code')}>
            <Input dir="ltr" autoComplete="off" {...form.register('code')} />
          </FormField>
          <FormField label={t('fields.level')} error={error('levelCode')} required>
            <Controller
              control={form.control}
              name="levelCode"
              render={({ field }) => (
                <Select
                  value={field.value || undefined}
                  onValueChange={field.onChange}
                  placeholder={t('fields.chooseLevel')}
                  options={(levels.data ?? [])
                    .filter((l) => l.active)
                    .map((level) => ({
                      value: level.code,
                      label: pickText(i18n.language, level.nameAr, level.nameEn),
                    }))}
                />
              )}
            />
          </FormField>
          <FormField label={t('fields.nameAr')} error={error('nameAr')}>
            <Input dir="rtl" lang="ar" autoComplete="off" {...form.register('nameAr')} />
          </FormField>
          <FormField label={t('fields.nameEn')} error={error('nameEn')}>
            <Input dir="ltr" lang="en" autoComplete="off" {...form.register('nameEn')} />
          </FormField>
          <FormField label={t('fields.head')} error={error('headEmployeeId')}>
            <Controller
              control={form.control}
              name="headEmployeeId"
              render={({ field }) => <EmployeePicker value={field.value} onChange={field.onChange} />}
            />
          </FormField>
          <FormField label={t('fields.deputy')} error={error('deputyEmployeeId')}>
            <Controller
              control={form.control}
              name="deputyEmployeeId"
              render={({ field }) => <EmployeePicker value={field.value} onChange={field.onChange} />}
            />
          </FormField>
          <FormField label={t('fields.costCentre')} error={error('costCentre')}>
            <Input dir="ltr" autoComplete="off" {...form.register('costCentre')} />
          </FormField>
          <FormField
            label={unit ? t('fields.effectiveDate') : t('fields.effectiveFrom')}
            error={error('effectiveDate')}
            hint={t('hints.effectiveDate')}
          >
            <Controller
              control={form.control}
              name="effectiveDate"
              render={({ field }) => <DatePicker value={field.value} onChange={field.onChange} />}
            />
          </FormField>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Re-parent a unit (and its whole subtree) from an effective date. History keeps the old structure. */
export function MoveUnitDialog({
  open,
  onOpenChange,
  unit,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  unit: OrgUnit;
}) {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const tree = useUnitTree();
  const invalidate = useInvalidateOrg();
  const toast = useToast();
  const [parentId, setParentId] = useState<string>(unit.parentId ?? 'root');
  const [effective, setEffective] = useState<string | undefined>(todayIso());

  // Exclude the unit itself and its descendants (moving below them would be a cycle).
  const blocked = new Set<string>([unit.id]);
  for (const node of tree.data ?? []) {
    if (node.parentId && blocked.has(node.parentId)) blocked.add(node.id);
  }

  const move = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/org/units/{id}/move', {
          params: { path: { id: unit.id } },
          body: {
            newParentId: parentId === 'root' ? null : parentId,
            effectiveDate: effective ?? null,
            version: unit.version,
          },
        }),
      ),
    onSuccess: async () => {
      toast({ title: t('units.moved') });
      await invalidate();
      onOpenChange(false);
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });

  const options = [
    { value: 'root', label: t('units.noParent') },
    ...(tree.data ?? [])
      .filter((node) => !blocked.has(node.id) && node.active)
      .map((node) => ({
        value: node.id,
        label: `${'— '.repeat(node.depth)}${pickText(i18n.language, node.nameAr, node.nameEn)}`,
      })),
  ];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t('units.moveTitle', { name: pickText(i18n.language, unit.nameAr, unit.nameEn) })}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={move.isPending}
              onClick={() => move.mutate()}
              disabled={parentId === (unit.parentId ?? 'root')}
            >
              {t('units.move')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">{t('units.moveDescription')}</p>
          <FormField label={t('units.newParent')}>
            <Select value={parentId} onValueChange={setParentId} options={options} />
          </FormField>
          <FormField label={t('fields.effectiveDate')} hint={t('hints.effectiveDate')}>
            <DatePicker value={effective} onChange={setEffective} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}
