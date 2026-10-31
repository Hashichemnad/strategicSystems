import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { api, unwrap } from '@/api/client';
import { applyFieldErrors, errorMessage, translateFieldMessage } from '@/api/problem';
import { useLookupOptions } from '@/features/settings/lookups';
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
import { nullIfEmpty, todayIso, useInvalidateOrg, useUnitTree, type Employee } from './api';
import { EmployeePicker } from './EmployeePicker';

const maxLength = (max: number) => `validation.maximumLength|${JSON.stringify({ maxLength: max })}`;

const schema = z
  .object({
    employeeNo: z.string().trim().min(1, 'validation.notEmpty').max(50, maxLength(50)),
    nameAr: z.string().trim().max(200, maxLength(200)),
    nameEn: z.string().trim().max(200, maxLength(200)),
    email: z
      .string()
      .trim()
      .min(1, 'validation.notEmpty')
      .max(256, maxLength(256))
      .pipe(z.email('validation.email')),
    mobile: z.string().trim().max(30, maxLength(30)),
    jobTitleAr: z.string().trim().max(200, maxLength(200)),
    jobTitleEn: z.string().trim().max(200, maxLength(200)),
    grade: z.string().trim().max(50, maxLength(50)),
    unitId: z.string().min(1, 'validation.notEmpty'),
    managerEmployeeId: z.string().optional(),
    joinDate: z.string().optional(),
    effectiveDate: z.string().optional(),
  })
  .refine((value) => value.nameAr !== '' || value.nameEn !== '', {
    path: ['nameAr'],
    message: 'validation.atLeastOneLanguage',
  });

type FormValues = z.infer<typeof schema>;
const fields = [
  'employeeNo',
  'nameAr',
  'nameEn',
  'email',
  'mobile',
  'jobTitleAr',
  'jobTitleEn',
  'grade',
  'unitId',
  'managerEmployeeId',
  'joinDate',
  'effectiveDate',
] as const;

/** Create or edit an employee. A change of unit, manager or job title is recorded in the placement history. */
export function EmployeeDialog({
  open,
  onOpenChange,
  employee,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  employee?: Employee;
}) {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const { t: te } = useTranslation('errors');
  const units = useUnitTree();
  const invalidate = useInvalidateOrg();
  const toast = useToast();
  const navigate = useNavigate();
  const gradeOptions = useLookupOptions('employee.grade', employee?.grade, '—');
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      employeeNo: employee?.employeeNo ?? '',
      nameAr: employee?.nameAr ?? '',
      nameEn: employee?.nameEn ?? '',
      email: employee?.email ?? '',
      mobile: employee?.mobile ?? '',
      jobTitleAr: employee?.jobTitleAr ?? '',
      jobTitleEn: employee?.jobTitleEn ?? '',
      grade: employee?.grade ?? '',
      unitId: employee?.unitId ?? '',
      managerEmployeeId: employee?.manager?.id,
      joinDate: employee?.joinDate ?? undefined,
      effectiveDate: todayIso(),
    },
  });
  const { errors, isSubmitting } = form.formState;
  const error = (name: (typeof fields)[number]) => translateFieldMessage(tc, errors[name]?.message);

  const submit = form.handleSubmit(async (values) => {
    const body = {
      employeeNo: values.employeeNo,
      nameAr: nullIfEmpty(values.nameAr),
      nameEn: nullIfEmpty(values.nameEn),
      email: values.email,
      mobile: nullIfEmpty(values.mobile),
      jobTitleAr: nullIfEmpty(values.jobTitleAr),
      jobTitleEn: nullIfEmpty(values.jobTitleEn),
      grade: nullIfEmpty(values.grade),
      unitId: values.unitId,
      managerEmployeeId: values.managerEmployeeId ?? null,
      joinDate: values.joinDate ?? null,
    };
    try {
      if (employee) {
        unwrap(
          await api.PUT('/api/v1/org/employees/{id}', {
            params: { path: { id: employee.id } },
            body: { ...body, effectiveDate: values.effectiveDate ?? null, version: employee.version },
          }),
        );
        toast({ title: t('employees.updated') });
        onOpenChange(false);
      } else {
        const created = unwrap(await api.POST('/api/v1/org/employees', { body }));
        toast({ title: t('employees.created') });
        onOpenChange(false);
        await navigate({ to: '/org/employees/$employeeId', params: { employeeId: created.id } });
      }
      await invalidate();
    } catch (failure) {
      if (!applyFieldErrors(failure, form.setError, fields)) {
        toast({ tone: 'error', title: te('title'), description: errorMessage(tc, failure) });
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={employee ? t('employees.editTitle') : t('employees.create')}
        className="max-w-3xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button type="submit" form="employee-form" loading={isSubmitting}>
              {employee ? tc('actions.save') : tc('actions.create')}
            </Button>
          </>
        }
      >
        <form
          id="employee-form"
          noValidate
          onSubmit={(event) => void submit(event)}
          className="grid gap-4 sm:grid-cols-2"
        >
          <FormField label={t('fields.employeeNo')} error={error('employeeNo')} required>
            <Input dir="ltr" autoComplete="off" {...form.register('employeeNo')} />
          </FormField>
          <FormField label={t('fields.email')} error={error('email')} required>
            <Input dir="ltr" type="email" autoComplete="off" {...form.register('email')} />
          </FormField>
          <FormField label={t('fields.nameAr')} error={error('nameAr')}>
            <Input dir="rtl" lang="ar" autoComplete="off" {...form.register('nameAr')} />
          </FormField>
          <FormField label={t('fields.nameEn')} error={error('nameEn')}>
            <Input dir="ltr" lang="en" autoComplete="off" {...form.register('nameEn')} />
          </FormField>
          <FormField label={t('fields.jobTitleAr')} error={error('jobTitleAr')}>
            <Input dir="rtl" lang="ar" autoComplete="off" {...form.register('jobTitleAr')} />
          </FormField>
          <FormField label={t('fields.jobTitleEn')} error={error('jobTitleEn')}>
            <Input dir="ltr" lang="en" autoComplete="off" {...form.register('jobTitleEn')} />
          </FormField>
          <FormField label={t('fields.unit')} error={error('unitId')} required>
            <Controller
              control={form.control}
              name="unitId"
              render={({ field }) => (
                <Select
                  value={field.value || undefined}
                  onValueChange={field.onChange}
                  placeholder={t('fields.chooseUnit')}
                  options={(units.data ?? [])
                    .filter((unit) => unit.active)
                    .map((unit) => ({
                      value: unit.id,
                      label: `${'— '.repeat(unit.depth)}${pickText(i18n.language, unit.nameAr, unit.nameEn)}`,
                    }))}
                />
              )}
            />
          </FormField>
          <FormField label={t('fields.manager')} error={error('managerEmployeeId')}>
            <Controller
              control={form.control}
              name="managerEmployeeId"
              render={({ field }) => (
                <EmployeePicker value={field.value} onChange={field.onChange} excludeId={employee?.id} />
              )}
            />
          </FormField>
          <FormField label={t('fields.mobile')} error={error('mobile')}>
            <Input dir="ltr" type="tel" autoComplete="off" {...form.register('mobile')} />
          </FormField>
          <FormField label={t('fields.grade')} error={error('grade')}>
            <Controller
              control={form.control}
              name="grade"
              render={({ field }) => (
                <Select
                  value={field.value || '-'}
                  onValueChange={(value) => field.onChange(value === '-' ? '' : value)}
                  options={gradeOptions}
                />
              )}
            />
          </FormField>
          <FormField label={t('fields.joinDate')} error={error('joinDate')}>
            <Controller
              control={form.control}
              name="joinDate"
              render={({ field }) => <DatePicker value={field.value} onChange={field.onChange} />}
            />
          </FormField>
          {employee && (
            <FormField
              label={t('fields.effectiveDate')}
              error={error('effectiveDate')}
              hint={t('hints.placementDate')}
            >
              <Controller
                control={form.control}
                name="effectiveDate"
                render={({ field }) => <DatePicker value={field.value} onChange={field.onChange} />}
              />
            </FormField>
          )}
        </form>
      </DialogContent>
    </Dialog>
  );
}
