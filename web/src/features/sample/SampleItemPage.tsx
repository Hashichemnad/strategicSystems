import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Pencil } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/ui';
import { useSampleItem } from './api';
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
  };

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
          <StatusChip tone={data.status === 'active' ? 'success' : 'neutral'}>
            {t(data.status === 'active' ? 'status.active' : 'status.inactive')}
          </StatusChip>
        }
        actions={
          canEdit && (
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil aria-hidden />
              {tc('actions.edit')}
            </Button>
          )
        }
      />
      <Tabs value={tab} onValueChange={(value) => void navigate({ search: { tab: value as 'overview' } })}>
        <TabsList>
          <TabsTrigger value="overview">{t('tabs.overview')}</TabsTrigger>
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
              <dt className="text-sm text-muted-foreground">{t('fields.updatedAt')}</dt>
              <dd className="mt-1">{formatDateTime(data.updatedAt, i18n.language)}</dd>
            </div>
          </dl>
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
    </div>
  );
}
