import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Pencil } from 'lucide-react';
import { useState } from 'react';
import { newIdempotencyKey } from '@/features/workflow/api';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  PageHeader,
  StageStepper,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
  type Tone,
} from '@/ui';
import { useSampleItem, useSubmitSampleItem, type SampleItem } from './api';
import { SampleItemDialog } from './SampleItemDialog';

const route = getRouteApi('/app/sample/items/$itemId');

/** Record page of the template module (CLAUDE.md §10): header, then Overview, Documents and History tabs. */
export function SampleItemPage() {
  const { t, i18n } = useTranslation('sample');
  const { t: tc } = useTranslation();
  const { t: ta } = useTranslation('audit');
  const { t: tf } = useTranslation('files');
  const { itemId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const item = useSampleItem(itemId);
  const canEdit = useCan('sample.item.update');
  const [editing, setEditing] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  if (item.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (item.isError) return <StatusBanner tone="danger" title={errorMessage(tc, item.error)} />;
  const data = item.data;
  const name = pickText(i18n.language, data.nameAr, data.nameEn);
  const fieldLabels: Record<string, string> = {
    NameAr: t('fields.nameAr'),
    NameEn: t('fields.nameEn'),
    DescriptionAr: t('fields.descriptionAr'),
    DescriptionEn: t('fields.descriptionEn'),
    Status: t('fields.status'),
    Number: t('fields.number'),
    Amount: t('fields.amount'),
    ApprovalStatus: t('fields.approvalStatus'),
  };
  const approvalTone: Record<string, Tone | undefined> = {
    draft: 'neutral',
    pending: 'info',
    approved: 'success',
    rejected: 'danger',
    returned: 'warning',
  };
  const stage =
    data.approvalStatus === 'returned'
      ? 'draft'
      : data.approvalStatus === 'rejected'
        ? 'approved'
        : data.approvalStatus;

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/sample"
        search={{ page: 1, sort: '-createdAt' }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader
        title={name}
        description={data.number ?? undefined}
        meta={
          <>
            <StatusChip tone={data.status === 'active' ? 'success' : 'neutral'}>
              {t(data.status === 'active' ? 'status.active' : 'status.inactive')}
            </StatusChip>
            <StatusChip tone={approvalTone[data.approvalStatus] ?? 'neutral'}>
              {t(`approvalStatus.${data.approvalStatus as 'draft'}`)}
            </StatusChip>
          </>
        }
        actions={
          canEdit &&
          data.approvalStatus !== 'pending' && (
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil aria-hidden />
              {tc('actions.edit')}
            </Button>
          )
        }
      />
      <StageStepper
        stages={[
          { key: 'draft', label: t('stages.draft') },
          { key: 'pending', label: t('stages.pending') },
          { key: 'approved', label: t('stages.approved') },
        ]}
        current={stage}
      />
      <CurrentStatusBanner
        resourceType="sample.item"
        resourceId={data.id}
        onAction={(code) => {
          if (code === 'submit' || code === 'resubmit') setSubmitting(true);
        }}
      />
      <Tabs value={tab} onValueChange={(value) => void navigate({ search: { tab: value as 'overview' } })}>
        <TabsList>
          <TabsTrigger value="overview">{t('tabs.overview')}</TabsTrigger>
          <TabsTrigger value="approvals">{t('tabs.approvals')}</TabsTrigger>
          <TabsTrigger value="documents">{tf('tab')}</TabsTrigger>
          <TabsTrigger value="history">{ta('history.tab')}</TabsTrigger>
        </TabsList>
        <TabsContent value="overview">
          <dl className="grid gap-4 rounded-lg border bg-card p-6 md:grid-cols-2">
            {(
              [
                ['nameAr', data.nameAr, 'ar'],
                ['nameEn', data.nameEn, 'en'],
                ['descriptionAr', data.descriptionAr, 'ar'],
                ['descriptionEn', data.descriptionEn, 'en'],
              ] as const
            ).map(([key, value, lang]) => (
              <div key={key}>
                <dt className="text-sm text-muted-foreground">{t(`fields.${key}`)}</dt>
                <dd lang={lang} dir={lang === 'ar' ? 'rtl' : 'ltr'} className="mt-1 text-start">
                  {value ?? '—'}
                </dd>
              </div>
            ))}
            <div>
              <dt className="text-sm text-muted-foreground">{t('fields.amount')}</dt>
              <dd className="mt-1 tabular-nums">
                {data.amount === null ? '—' : formatNumber(data.amount, i18n.language)}
              </dd>
            </div>
            <div>
              <dt className="text-sm text-muted-foreground">{t('fields.updatedAt')}</dt>
              <dd className="mt-1">{formatDateTime(data.updatedAt, i18n.language)}</dd>
            </div>
          </dl>
        </TabsContent>
        <TabsContent value="approvals">
          <ApprovalPanel resourceType="sample.item" resourceId={data.id} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel
            ownerType="sample.item"
            ownerId={data.id}
            canEdit={canEdit && data.status === 'active'}
          />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="sample" id={data.id} fieldLabels={fieldLabels} />
        </TabsContent>
      </Tabs>
      <SampleItemDialog open={editing} onOpenChange={setEditing} item={data} />
      {submitting && <SubmitDialog item={data} onClose={() => setSubmitting(false)} />}
    </div>
  );
}

/** Submit / resubmit for approval with an optional comment for the approvers. */
function SubmitDialog({ item, onClose }: { item: SampleItem; onClose: () => void }) {
  const { t } = useTranslation('sample');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const submit = useSubmitSampleItem();
  const [comment, setComment] = useState('');
  const [key] = useState(newIdempotencyKey);
  const [error, setError] = useState<string>();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('submitDialog.title')}
        description={t('submitDialog.description')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={submit.isPending}
              onClick={() =>
                submit.mutate(
                  { item, comment: comment.trim() || null, idempotencyKey: key },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.submitted') });
                      onClose();
                    },
                    onError: (failure) => setError(errorMessage(tc, failure)),
                  },
                )
              }
            >
              {t('submitDialog.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('submitDialog.comment')}>
          <Textarea rows={3} maxLength={4000} value={comment} onChange={(e) => setComment(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}
