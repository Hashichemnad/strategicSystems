import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogTrigger,
  DatePicker,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
  Wizard,
} from '@/ui';

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-xl border bg-card p-5">
      <h2 className="text-base font-semibold">{title}</h2>
      {children}
    </section>
  );
}

/** Living reference of the shared UI kit, reviewed in Arabic and English, light and dark. */
export function UiKitPage() {
  const { t } = useTranslation('uiKit');
  const { t: tc } = useTranslation();
  const { t: tv } = useTranslation('validation');
  const toast = useToast();
  const [category, setCategory] = useState<string | undefined>();
  const [date, setDate] = useState<string | undefined>('2026-10-22');
  const [title, setTitle] = useState('');

  const stages = (['request', 'charter', 'planning', 'execution', 'closure'] as const).map((key) => ({
    key,
    label: t(`demo.stages.${key}`),
  }));
  const categories = (['strategic', 'operational', 'compliance'] as const).map((value) => ({
    value,
    label: t(`demo.categories.${value}`),
  }));

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('title')} description={t('description')} />

      <Section title={t('sections.stepper')}>
        <StageStepper stages={stages} current="charter" />
        <StatusBanner
          tone="warning"
          title={t('demo.bannerTitle')}
          description={t('demo.bannerDescription')}
          actions={
            <>
              <Button size="sm">{t('demo.approve')}</Button>
              <Button size="sm" variant="outline">
                {t('demo.returnForCorrection')}
              </Button>
            </>
          }
        />
      </Section>

      <Section title={t('sections.buttons')}>
        <div className="flex flex-wrap gap-2">
          <Button>{tc('actions.save')}</Button>
          <Button variant="secondary">{tc('actions.saveDraft')}</Button>
          <Button variant="outline">{tc('actions.cancel')}</Button>
          <Button variant="ghost">{tc('actions.more')}</Button>
          <Button variant="danger">{tc('actions.confirm')}</Button>
          <Button loading>{tc('status.loading')}</Button>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusChip>{t('demo.stages.request')}</StatusChip>
          <StatusChip tone="info">{t('demo.stages.planning')}</StatusChip>
          <StatusChip tone="success">{t('demo.stages.execution')}</StatusChip>
          <StatusChip tone="warning">{t('demo.stages.charter')}</StatusChip>
          <StatusChip tone="danger">{t('demo.stages.closure')}</StatusChip>
        </div>
      </Section>

      <Section title={t('sections.form')}>
        <div className="grid gap-4 md:grid-cols-3">
          <FormField
            label={t('demo.title')}
            required
            error={title.length > 40 ? tv('maximumLength', { maxLength: 40 }) : undefined}
          >
            <Input
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              placeholder={t('demo.titlePlaceholder')}
            />
          </FormField>
          <FormField label={t('demo.category')}>
            <Select
              value={category}
              onValueChange={setCategory}
              options={categories}
              placeholder={t('demo.categoryPlaceholder')}
            />
          </FormField>
          <FormField label={t('demo.startDate')}>
            <DatePicker value={date} onChange={setDate} />
          </FormField>
          <FormField label={t('demo.notes')} className="md:col-span-3">
            <Textarea />
          </FormField>
        </div>
      </Section>

      <Section title={t('sections.wizard')}>
        <Wizard
          steps={[
            {
              key: 'basics',
              title: t('demo.wizardSteps.basics'),
              content: <Input aria-label={t('demo.title')} placeholder={t('demo.titlePlaceholder')} />,
            },
            {
              key: 'details',
              title: t('demo.wizardSteps.details'),
              content: <Textarea aria-label={t('demo.notes')} />,
            },
            {
              key: 'review',
              title: t('demo.wizardSteps.review'),
              content: <p className="text-sm text-muted-foreground">{t('demo.reviewText')}</p>,
            },
          ]}
          onSaveDraft={() => toast({ title: tc('wizard.draftSaved') })}
          onFinish={() => toast({ title: tc('status.saved') })}
        />
      </Section>

      <Section title={t('sections.tabs')}>
        <Tabs defaultValue="overview">
          <TabsList>
            {(['overview', 'documents', 'history'] as const).map((tab) => (
              <TabsTrigger key={tab} value={tab}>
                {t(`demo.tabs.${tab}`)}
              </TabsTrigger>
            ))}
          </TabsList>
          {(['overview', 'documents', 'history'] as const).map((tab) => (
            <TabsContent key={tab} value={tab}>
              <p className="text-sm text-muted-foreground">
                {t('demo.tabContent', { tab: t(`demo.tabs.${tab}`) })}
              </p>
            </TabsContent>
          ))}
        </Tabs>
      </Section>

      <Section title={t('sections.feedback')}>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            onClick={() => toast({ title: t('demo.toastTitle'), description: t('demo.toastDescription') })}
          >
            {t('demo.showToast')}
          </Button>
          <Dialog>
            <DialogTrigger asChild>
              <Button variant="outline">{t('demo.openDialog')}</Button>
            </DialogTrigger>
            <DialogContent
              title={t('demo.dialogTitle')}
              description={t('demo.dialogDescription')}
              footer={
                <DialogClose asChild>
                  <Button>{tc('actions.close')}</Button>
                </DialogClose>
              }
            />
          </Dialog>
        </div>
        <div className="flex flex-col gap-2" aria-label={t('demo.skeleton')}>
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-4 w-1/2" />
        </div>
        <EmptyState title={t('demo.emptyTitle')} description={t('demo.emptyDescription')} />
      </Section>
    </div>
  );
}
