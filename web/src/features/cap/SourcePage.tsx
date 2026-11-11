import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Plus, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkBadge } from '@/features/links/LinkBadge';
import {
  Button,
  FormField,
  PageHeader,
  RecordLayout,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Section,
} from '@/ui';
import { capApi, useCapRemarks, useCapSource, type CapRemarkValues, type RemarkExtraction } from './api';
import {
  CapNav,
  PriorityChip,
  Rows,
  SimpleDialog,
  lookupTypes,
  useCapLabels,
  useCapT,
  useDate,
  useNames,
  useToastMutation,
} from './components';
import { RemarkDialog, emptyRemark } from './forms';
import { RemarksTable } from './RemarksTable';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/cap/sources/$sourceId');

/** Source page: details, live link to the originating record, its remarks, documents, history. */
type SourceTab = 'remarks' | 'overview' | 'documents' | 'history';

export function CapSourcePage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const { sourceId } = route.useParams();
  const source = useCapSource(sourceId);
  const remarks = useCapRemarks({ sourceId, pageSize: 200 });
  const names = useNames();
  const labels = useCapLabels();
  const date = useDate();
  const [adding, setAdding] = useState<CapRemarkValues | null>(null);
  const [extracting, setExtracting] = useState(false);
  const aiFeature = useAiFeature('cap-extract');
  const aiAllowed = useCan('cap.ai.extract');
  const canExtract = aiFeature && aiAllowed;
  const [tab, setTab] = useState<SourceTab>('remarks');

  if (source.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (source.isError) return <StatusBanner tone="danger" title={errorMessage(tc, source.error)} />;
  const s = source.data;
  const v = s.values;
  return (
    <RecordLayout
      testId="cap-source-page"
      // CAP's own navigation first, as on every CAP page (CapPageLayout); hidden in the workspace themes (app tabs).
      back={
        <>
          <CapNav />
          <Link to="/cap/sources" className="flex items-center gap-1 text-sm text-primary hover:underline">
            <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
            {t('sources.title')}
          </Link>
        </>
      }
      highlights={[
        { key: 'number', label: t('fields.number'), value: <bdi>{s.number}</bdi> },
        {
          key: 'sourceType',
          label: t('fields.sourceType'),
          value: labels.lookup(lookupTypes.sourceType, v.sourceTypeCode),
        },
        { key: 'receivedOn', label: t('fields.receivedOn'), value: date(v.receivedOn) },
        { key: 'unit', label: t('fields.unit'), value: names.unit(s.unit) },
        { key: 'owner', label: t('fields.sourceOwner'), value: names.person(s.owner) },
        { key: 'origin', label: t('fields.origin'), value: k(`origin.${s.origin}`) },
      ]}
      related={(['remarks', 'documents', 'history'] as const).map((key) => ({
        key,
        label: t(`tabs.${key}`),
        count: key === 'remarks' ? remarks.data?.totalCount : undefined,
        onOpen: () => setTab(key),
      }))}
      header={
        <PageHeader
          title={names.text(v.titleAr, v.titleEn) || s.number}
          description={
            <>
              <bdi data-testid="cap-source-number">{s.number}</bdi> ·{' '}
              {labels.lookup(lookupTypes.sourceType, v.sourceTypeCode)}
            </>
          }
          meta={
            <div className="flex flex-wrap items-center gap-2">
              <OriginBadge type="cap.source" id={s.id} />
              <StatusChip tone={s.status === 'active' ? 'info' : 'success'}>
                {k(`sourceStatus.${s.status}`)}
              </StatusChip>
              {s.linked && <LinkBadge view={s.linked} />}
            </div>
          }
          actions={
            s.can.addRemark && (
              <div className="flex flex-wrap gap-2">
                {canExtract && (
                  <Button variant="outline" onClick={() => setExtracting(true)} data-testid="cap-ai-extract">
                    <Sparkles aria-hidden />
                    {t('ai.extract')}
                  </Button>
                )}
                <Button onClick={() => setAdding(emptyRemark())} data-testid="cap-remark-add">
                  <Plus aria-hidden />
                  {t('remark.add')}
                </Button>
              </div>
            )
          }
        />
      }
    >
      <Tabs value={tab} onValueChange={(value) => setTab(value as SourceTab)}>
        <TabsList>
          <TabsTrigger value="remarks">{t('tabs.remarks')}</TabsTrigger>
          <TabsTrigger value="overview">{t('tabs.overview')}</TabsTrigger>
          <TabsTrigger value="documents">{t('tabs.documents')}</TabsTrigger>
          <TabsTrigger value="history">{t('tabs.history')}</TabsTrigger>
        </TabsList>
        <TabsContent value="remarks">
          {!remarks.data ? (
            <Skeleton className="h-48" />
          ) : remarks.data.items.length === 0 ? (
            <p className="rounded-lg border bg-card p-6 text-sm text-muted-foreground">
              {t('sources.noRemarks')}
            </p>
          ) : (
            <RemarksTable rows={remarks.data.items} showSource={false} />
          )}
        </TabsContent>
        <TabsContent value="overview">
          <Rows
            rows={[
              [t('fields.sourceType'), labels.lookup(lookupTypes.sourceType, v.sourceTypeCode)],
              [t('fields.receivedOn'), date(v.receivedOn)],
              [t('fields.unit'), names.unit(s.unit)],
              [t('fields.sourceOwner'), names.person(s.owner)],
              [t('fields.origin'), k(`origin.${s.origin}`)],
              [t('fields.viewers'), s.viewers.map((p) => names.person(p)).join('، ')],
              [t('fields.description'), v.description],
            ]}
          />
          <ExternalReferencesSection type="cap.source" id={s.id} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel ownerType="cap.source" ownerId={s.id} canEdit={s.can.edit || s.can.addRemark} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="capSource" id={s.id} />
        </TabsContent>
      </Tabs>
      {adding && <RemarkDialog sourceId={s.id} initial={adding} onClose={() => setAdding(null)} />}
      {extracting && (
        <ExtractDialog
          sourceId={s.id}
          onClose={() => setExtracting(false)}
          onUse={(r) => {
            setExtracting(false);
            setAdding(r);
          }}
        />
      )}
    </RecordLayout>
  );
}

/** AI: paste the report text, get draft remarks; each is reviewed in the remark form before it is saved (AI drafts, people decide). */
function ExtractDialog({
  sourceId,
  onClose,
  onUse,
}: {
  sourceId: string;
  onClose: () => void;
  onUse: (values: CapRemarkValues) => void;
}) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const [text, setText] = useState('');
  const [result, setResult] = useState<RemarkExtraction>();
  const extract = useToastMutation(capApi.extract);
  return (
    <SimpleDialog
      title={t('ai.extract')}
      description={t('ai.extractHint')}
      onClose={onClose}
      busy={extract.busy}
      disabled={!text.trim()}
      submitLabel={t('ai.run')}
      testId="cap-ai-extract-run"
      wide
      onSubmit={() => extract.run({ sourceId, text }, setResult)}
    >
      <FormField label={t('ai.reportText')}>
        <Textarea
          rows={6}
          value={text}
          onChange={(e) => setText(e.target.value)}
          data-testid="cap-ai-extract-text"
        />
      </FormField>
      {result && (
        <Section
          title={
            <span className="flex items-center gap-2">
              {t('ai.drafts')} <AiBadge />
            </span>
          }
        >
          <ul className="flex flex-col divide-y" data-testid="cap-ai-extract-results">
            {result.remarks.map((r, i) => (
              <li key={i} className="flex items-start justify-between gap-3 py-2 text-sm">
                <span className="flex flex-col gap-1">
                  <span>{names.text(r.textAr, r.textEn)}</span>
                  {r.priority && (
                    <span>
                      <PriorityChip priority={r.priority} />
                    </span>
                  )}
                </span>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() =>
                    onUse({
                      ...emptyRemark(),
                      textAr: r.textAr ?? '',
                      textEn: r.textEn ?? '',
                      criterionCode: r.criterionCode,
                      typeCode: r.typeCode ?? 'observation',
                      priority: r.priority ?? 'medium',
                      departmentUnitId: r.departmentUnitId ?? '',
                    })
                  }
                  data-testid="cap-ai-extract-use"
                >
                  {t('ai.use')}
                </Button>
              </li>
            ))}
          </ul>
        </Section>
      )}
    </SimpleDialog>
  );
}
