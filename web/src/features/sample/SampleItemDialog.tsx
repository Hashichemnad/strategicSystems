import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect, useState, type ReactElement, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { applyFieldErrors, errorMessage, translateFieldMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import {
  AiFieldReview,
  aiPendingClass,
  FillFromDocumentButton,
  FillFromDocumentDialog,
  SimilarRecords,
  TranslateButton,
  type AiSuggestion,
} from '@/features/ai/AiDraft';
import type { ExtractionResult } from '@/features/ai/api';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  StatusBanner,
  Textarea,
  useToast,
} from '@/ui';
import { useCreateSampleItem, useUpdateSampleItem, type SampleItem } from './api';

const maxLength = (max: number) => `validation.maximumLength|${JSON.stringify({ maxLength: max })}`;

/** Mirrors the server validator so users get instant feedback; the server stays the authority. */
const schema = z
  .object({
    nameAr: z.string().trim().max(200, maxLength(200)),
    nameEn: z.string().trim().max(200, maxLength(200)),
    descriptionAr: z.string().trim().max(2000, maxLength(2000)),
    descriptionEn: z.string().trim().max(2000, maxLength(2000)),
    amount: z
      .string()
      .trim()
      .refine((value) => value === '' || (/^\d{1,16}(\.\d{1,2})?$/u.test(value) && Number(value) >= 0), {
        message: 'validation.precisionScale',
      }),
  })
  .refine((value) => value.nameAr !== '' || value.nameEn !== '', {
    path: ['nameAr'],
    message: 'validation.atLeastOneLanguage',
  });

type FormValues = z.infer<typeof schema>;
const fields = ['nameAr', 'nameEn', 'descriptionAr', 'descriptionEn', 'amount'] as const;
type FieldName = (typeof fields)[number];

/** The form key the server knows (Sample module, Phase 5 worked example). */
export const sampleAiFormKey = 'sample.item';

interface SampleItemDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The item to edit; omit to create. */
  item?: SampleItem | undefined;
}

export function SampleItemDialog({ open, onOpenChange, item }: SampleItemDialogProps) {
  const { t } = useTranslation('sample');
  const { t: ta } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const { t: te } = useTranslation('errors');
  const toast = useToast();
  const create = useCreateSampleItem();
  const update = useUpdateSampleItem();
  const [fillOpen, setFillOpen] = useState(false);
  // AI drafts by field (document-to-form, translation): nothing is saved until each one is accepted or edited (CLAUDE.md §2 rule 6).
  const [drafts, setDrafts] = useState<Partial<Record<FieldName, AiSuggestion>>>({});
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      nameAr: item?.nameAr ?? '',
      nameEn: item?.nameEn ?? '',
      descriptionAr: item?.descriptionAr ?? '',
      descriptionEn: item?.descriptionEn ?? '',
      amount: item?.amount === null || item?.amount === undefined ? '' : String(item.amount),
    },
  });
  useEffect(() => {
    if (!open) setDrafts({});
  }, [open]);
  const { errors } = form.formState;
  const error = (name: FieldName) => translateFieldMessage(tc, errors[name]?.message);
  const pending = fields.filter((name) => drafts[name] && !drafts[name].accepted);
  const accept = (name: FieldName) =>
    setDrafts((current) =>
      current[name] ? { ...current, [name]: { ...current[name], accepted: true } } : current,
    );
  const register = (name: FieldName) => form.register(name, { onChange: () => accept(name) });
  const propose = (name: FieldName, value: string, suggestion: Omit<AiSuggestion, 'accepted'>) => {
    form.setValue(name, value, { shouldDirty: true, shouldValidate: true });
    setDrafts((current) => ({ ...current, [name]: { ...suggestion, accepted: false } }));
  };

  const applyExtraction = (result: ExtractionResult) => {
    const filled = result.fields.filter((field): field is typeof field & { name: FieldName } =>
      (fields as readonly string[]).includes(field.name),
    );
    for (const field of filled) {
      propose(field.name, field.value ?? '', {
        confidence: field.confidence,
        source: field.source ?? null,
        sourceVerified: field.sourceVerified,
        location: field.location ?? null,
      });
    }
    toast({
      title:
        filled.length > 0
          ? ta('fill.filled', { count: filled.length, file: result.fileName })
          : ta('fill.none'),
      ...(result.truncated ? { description: ta('fill.truncated') } : {}),
    });
  };

  const submit = form.handleSubmit(async (values) => {
    if (pending.length > 0) return;
    const { amount, ...text } = values;
    const body = {
      ...(Object.fromEntries(
        Object.entries(text).map(([key, value]) => [key, value === '' ? null : value]),
      ) as {
        [K in Exclude<keyof FormValues, 'amount'>]: string | null;
      }),
      amount: amount === '' ? null : Number(amount),
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

  const field = (name: FieldName, control: ReactElement<{ id?: string }>, extra?: ReactNode) => (
    <div className="flex flex-col gap-1">
      <FormField label={t(`fields.${name}`)} error={error(name)}>
        {control}
      </FormField>
      {extra}
      {drafts[name] && <AiFieldReview suggestion={drafts[name]} onAccept={() => accept(name)} />}
    </div>
  );
  const highlight = (name: FieldName) =>
    drafts[name] && !drafts[name].accepted ? aiPendingClass : undefined;
  const translate = (name: FieldName, other: FieldName, from: 'ar' | 'en') => (
    <div className="flex justify-end">
      <TranslateButton
        from={from}
        systemCode="sample"
        sourceText={form.watch(other)}
        onTranslated={(text) =>
          propose(name, text, { confidence: null, source: null, sourceVerified: true, location: null })
        }
      />
    </div>
  );

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
            <Button
              type="submit"
              form="sample-item-form"
              loading={form.formState.isSubmitting}
              disabled={pending.length > 0}
            >
              {item ? tc('actions.save') : tc('actions.create')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {!item && (
            <div className="flex justify-end">
              <FillFromDocumentButton onClick={() => setFillOpen(true)} />
            </div>
          )}
          {pending.length > 0 && (
            <StatusBanner
              tone="info"
              title={ta('reviewPending', { count: pending.length })}
              actions={
                <Button size="sm" variant="outline" onClick={() => pending.forEach(accept)}>
                  {ta('acceptAll')}
                </Button>
              }
            />
          )}
          <form
            id="sample-item-form"
            noValidate
            onSubmit={(event) => void submit(event)}
            className="grid gap-4 sm:grid-cols-2"
          >
            {field(
              'nameAr',
              <Input
                dir="rtl"
                lang="ar"
                autoComplete="off"
                className={cn(highlight('nameAr'))}
                {...register('nameAr')}
              />,
              translate('nameAr', 'nameEn', 'en'),
            )}
            {field(
              'nameEn',
              <Input
                dir="ltr"
                lang="en"
                autoComplete="off"
                className={cn(highlight('nameEn'))}
                {...register('nameEn')}
              />,
              translate('nameEn', 'nameAr', 'ar'),
            )}
            {!item && (
              <div className="sm:col-span-2">
                <SimilarRecords
                  resourceType="sample.item"
                  text={`${form.watch('nameEn')} ${form.watch('nameAr')}`.trim()}
                />
              </div>
            )}
            {field(
              'descriptionAr',
              <Textarea
                dir="rtl"
                lang="ar"
                className={cn(highlight('descriptionAr'))}
                {...register('descriptionAr')}
              />,
              translate('descriptionAr', 'descriptionEn', 'en'),
            )}
            {field(
              'descriptionEn',
              <Textarea
                dir="ltr"
                lang="en"
                className={cn(highlight('descriptionEn'))}
                {...register('descriptionEn')}
              />,
              translate('descriptionEn', 'descriptionAr', 'ar'),
            )}
            {field(
              'amount',
              <Input
                dir="ltr"
                inputMode="decimal"
                autoComplete="off"
                className={cn(highlight('amount'))}
                {...register('amount')}
              />,
            )}
          </form>
        </div>
        <FillFromDocumentDialog
          formKey={sampleAiFormKey}
          open={fillOpen}
          onOpenChange={setFillOpen}
          onResult={applyExtraction}
        />
      </DialogContent>
    </Dialog>
  );
}
