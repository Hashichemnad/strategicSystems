import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { applyFieldErrors, errorMessage, translateFieldMessage } from '@/api/problem';
import { Button, Dialog, DialogClose, DialogContent, FormField, Input, Textarea, useToast } from '@/ui';
import { useCreateSampleItem, useUpdateSampleItem, type SampleItem } from './api';

const maxLength = (max: number) => `validation.maximumLength|${JSON.stringify({ maxLength: max })}`;

/** Mirrors the server validator so users get instant feedback; the server stays the authority. */
const schema = z
  .object({
    nameAr: z.string().trim().max(200, maxLength(200)),
    nameEn: z.string().trim().max(200, maxLength(200)),
    descriptionAr: z.string().trim().max(2000, maxLength(2000)),
    descriptionEn: z.string().trim().max(2000, maxLength(2000)),
  })
  .refine((value) => value.nameAr !== '' || value.nameEn !== '', {
    path: ['nameAr'],
    message: 'validation.atLeastOneLanguage',
  });

type FormValues = z.infer<typeof schema>;
const fields = ['nameAr', 'nameEn', 'descriptionAr', 'descriptionEn'] as const;

interface SampleItemDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The item to edit; omit to create. */
  item?: SampleItem | undefined;
}

export function SampleItemDialog({ open, onOpenChange, item }: SampleItemDialogProps) {
  const { t } = useTranslation('sample');
  const { t: tc } = useTranslation();
  const { t: te } = useTranslation('errors');
  const toast = useToast();
  const create = useCreateSampleItem();
  const update = useUpdateSampleItem();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      nameAr: item?.nameAr ?? '',
      nameEn: item?.nameEn ?? '',
      descriptionAr: item?.descriptionAr ?? '',
      descriptionEn: item?.descriptionEn ?? '',
    },
  });
  const { errors } = form.formState;
  const error = (name: (typeof fields)[number]) => translateFieldMessage(tc, errors[name]?.message);

  const submit = form.handleSubmit(async (values) => {
    const body = Object.fromEntries(
      Object.entries(values).map(([key, value]) => [key, value === '' ? null : value]),
    ) as {
      [K in keyof FormValues]: string | null;
    };
    try {
      if (item) {
        await update.mutateAsync({ id: item.id, version: item.version, ...body });
        toast({ title: t('toast.updated') });
      } else {
        await create.mutateAsync(body);
        toast({ title: t('toast.created') });
      }
      onOpenChange(false);
    } catch (failure) {
      if (!applyFieldErrors(failure, form.setError, fields)) {
        toast({ tone: 'error', title: te('title'), description: errorMessage(tc, failure) });
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={item ? t('editTitle') : t('createTitle')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button type="submit" form="sample-item-form" loading={form.formState.isSubmitting}>
              {item ? tc('actions.save') : tc('actions.create')}
            </Button>
          </>
        }
      >
        <form
          id="sample-item-form"
          noValidate
          onSubmit={(event) => void submit(event)}
          className="grid gap-4 sm:grid-cols-2"
        >
          <FormField label={t('fields.nameAr')} error={error('nameAr')}>
            <Input dir="rtl" lang="ar" autoComplete="off" {...form.register('nameAr')} />
          </FormField>
          <FormField label={t('fields.nameEn')} error={error('nameEn')}>
            <Input dir="ltr" lang="en" autoComplete="off" {...form.register('nameEn')} />
          </FormField>
          <FormField label={t('fields.descriptionAr')} error={error('descriptionAr')}>
            <Textarea dir="rtl" lang="ar" {...form.register('descriptionAr')} />
          </FormField>
          <FormField label={t('fields.descriptionEn')} error={error('descriptionEn')}>
            <Textarea dir="ltr" lang="en" {...form.register('descriptionEn')} />
          </FormField>
        </form>
      </DialogContent>
    </Dialog>
  );
}
