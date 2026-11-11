import { useMutation } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Download, FileText, Inbox, Search, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useAiCapabilities } from '@/features/ai/api';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { pickText } from '@/lib/format';
import {
  Button,
  buttonVariants,
  EmptyState,
  PageHeader,
  RecordLayout,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
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
  useToast,
  type Stage,
} from '@/ui';
import {
  IP_OWNER_TYPE,
  ipApi,
  ipDownloads,
  ipLookups,
  useIpRecord,
  useIpRecordMutation,
  type IpMoetDraft,
  type IpRecord,
  type IpSimilarItem,
} from './api';
import { actionError, Detail, IpNav, IpStatusChip, useDate, useIpT, useLookupLabel } from './components';
import { ContributorsTab } from './ContributorsTab';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { ipStageKeys, stageOf } from './logic';
import { RecordActionDialog, type IpDialog } from './RecordDialogs';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/ip/records/$recordId');

export const recordTabs = [
  'overview',
  'contributors',
  'registration',
  'documents',
  'linked',
  'history',
] as const;
type RecordTab = (typeof recordTabs)[number];

/** Statuses from which the MOET form makes sense (the record was approved to file). */
const formStatuses = new Set([
  'approved_to_file',
  'filed',
  'rejected_by_moet',
  'registered',
  'expired',
  'renewed',
]);

/** IP record page (docs/design/requests.md §4.3, §4.8): lifecycle, office actions by `can`, contributors, registration, documents, history. */
export function IpRecordPage() {
  const { t } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const { k, language } = useIpT();
  const { recordId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const record = useIpRecord(recordId);
  const propertyLabel = useLookupLabel(ipLookups.propertyType);
  const protectionLabel = useLookupLabel(ipLookups.protectionType);
  const [dialog, setDialog] = useState<IpDialog>();
  const date = useDate();

  if (record.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (record.isError) return <StatusBanner tone="danger" title={errorMessage(tc, record.error)} />;
  const data = record.data;
  const can = data.can;
  const stages: Stage[] = ipStageKeys.map((key) => ({ key, label: k(`stage.${key}`) }));
  const title = pickText(language, data.nameAr, data.nameEn) || data.number;
  const otherName = language === 'ar' ? data.nameEn : data.nameAr;
  const name = (x: { nameAr: string | null; nameEn: string | null } | null) =>
    x ? pickText(language, x.nameAr, x.nameEn) || '—' : '—';

  return (
    <RecordLayout
      testId="ip-record"
      // The register's own navigation first (IpPage); hidden in the workspace themes (app tabs).
      back={
        <>
          <IpNav />
          <Link to="/ip" className="flex items-center gap-1 text-sm text-primary hover:underline">
            <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
            {t('register.title')}
          </Link>
        </>
      }
      header={
        <>
          <PageHeader
            title={title}
            description={
              <>
                <bdi>{data.number}</bdi>
                {otherName && otherName !== title && (
                  <>
                    {' · '}
                    <span dir="auto">{otherName}</span>
                  </>
                )}
              </>
            }
            meta={
              <>
                <OriginBadge type="ip.record" id={data.id} />
                <IpStatusChip status={data.status} />
                {data.propertyType && (
                  <StatusChip tone="neutral">{propertyLabel(data.propertyType)}</StatusChip>
                )}
                {data.protectionType && (
                  <StatusChip tone="neutral">{protectionLabel(data.protectionType)}</StatusChip>
                )}
                {data.internal && <StatusChip tone="info">{t('record.internal')}</StatusChip>}
              </>
            }
            actions={<Actions record={data} onOpen={setDialog} />}
          />
          <SourceLink record={data} />
        </>
      }
      highlights={[
        { key: 'number', label: t('fields.number'), value: <bdi>{data.number}</bdi> },
        { key: 'ownerUnit', label: t('fields.ownerUnit'), value: name(data.ownerUnit) },
        { key: 'requester', label: t('fields.requester'), value: name(data.requester) },
        { key: 'filingDate', label: t('fields.filingDate'), value: date(data.filingDate) },
        {
          key: 'registrationNo',
          label: t('fields.registrationNo'),
          value: data.registrationNo ? <bdi>{data.registrationNo}</bdi> : '—',
        },
        { key: 'expiryDate', label: t('fields.expiryDate'), value: date(data.expiryDate) },
      ]}
      path={<StageStepper stages={stages} current={stageOf(data.status)} />}
      status={
        <>
          {data.status === 'rejected_by_moet' && (
            <StatusBanner
              tone="danger"
              title={t('record.rejectedByMoet')}
              description={data.moetRejectionReason ?? undefined}
            />
          )}
          {data.status === 'expired' && <StatusBanner tone="warning" title={t('record.expired')} />}
          {(can.file || can.register || can.registerInternally) && <OfficeHint record={data} />}
        </>
      }
      related={[
        {
          key: 'contributors',
          label: t('record.tabs.contributors'),
          count: data.contributors.length,
          onOpen: () => void navigate({ search: { tab: 'contributors' } }),
        },
        ...(['registration', 'documents', 'linked'] as const).map((key) => ({
          key,
          label: t(`record.tabs.${key}`),
          onOpen: () => void navigate({ search: { tab: key } }),
        })),
      ]}
    >
      <Tabs value={tab} onValueChange={(value) => void navigate({ search: { tab: value as RecordTab } })}>
        <TabsList className="flex-wrap">
          {recordTabs.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`ip-tab-${key}`}>
              {t(`record.tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview record={data} />
          <ExternalReferencesSection type="ip.record" id={data.id} />
        </TabsContent>
        <TabsContent value="contributors">
          <ContributorsTab record={data} />
        </TabsContent>
        <TabsContent value="registration">
          <Registration record={data} />
        </TabsContent>
        <TabsContent value="documents">
          <div className="flex flex-col gap-3">
            <p className="text-sm text-muted-foreground">{t('record.documentsHint')}</p>
            <DocumentsPanel
              ownerType={IP_OWNER_TYPE}
              ownerId={data.id}
              canEdit={can.file || can.register || can.renew || can.editContributors}
            />
          </div>
        </TabsContent>
        <TabsContent value="linked">
          <Linked record={data} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="ipRecord" id={data.id} />
        </TabsContent>
      </Tabs>
      <RecordActionDialog kind={dialog} record={data} onClose={() => setDialog(undefined)} />
    </RecordLayout>
  );
}

function Actions({ record, onOpen }: { record: IpRecord; onOpen: (dialog: IpDialog) => void }) {
  const { t } = useTranslation('ip');
  const can = record.can;
  return (
    <>
      {can.file && (
        <Button onClick={() => onOpen('filing')} data-testid="ip-action-filing">
          {t('actions.filing')}
        </Button>
      )}
      {can.registerInternally && (
        <Button onClick={() => onOpen('internal')} data-testid="ip-action-internal">
          {t('actions.internal')}
        </Button>
      )}
      {can.register && (
        <Button onClick={() => onOpen('registration')} data-testid="ip-action-register">
          {t('actions.register')}
        </Button>
      )}
      {can.moetRejection && (
        <Button
          variant="outline"
          onClick={() => onOpen('moetRejection')}
          data-testid="ip-action-moet-rejection"
        >
          {t('actions.moetRejection')}
        </Button>
      )}
      {can.renew && (
        <Button variant="outline" onClick={() => onOpen('renew')} data-testid="ip-action-renew">
          {t('actions.renew')}
        </Button>
      )}
      {can.close && (
        <Button variant="outline" onClick={() => onOpen('close')} data-testid="ip-action-close">
          {t('actions.close')}
        </Button>
      )}
      {can.sensitive && formStatuses.has(record.status) && (
        <a
          href={ipDownloads.moetForm(record.id)}
          className={buttonVariants({ variant: 'outline' })}
          data-testid="ip-moet-form"
          download
        >
          <Download aria-hidden />
          {t('actions.moetForm')}
        </a>
      )}
      {record.certificateFileId && (
        <a
          href={ipDownloads.certificate(record.id)}
          className={buttonVariants({ variant: 'outline' })}
          data-testid="ip-certificate"
          download
        >
          <Download aria-hidden />
          {t('actions.certificate')}
        </a>
      )}
    </>
  );
}

/** Tells the office what the next step needs (MOET form for administrators only). */
function OfficeHint({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  return (
    <StatusBanner
      tone="info"
      title={record.can.register ? t('record.nextRegister') : t('record.nextFile')}
      description={record.can.sensitive ? t('record.moetFormReady') : t('record.moetFormAdminsOnly')}
    />
  );
}

function SourceLink({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  if (record.source === 'document' && record.documentId)
    return (
      <p className="flex items-center gap-2 text-sm">
        <FileText className="size-4 text-muted-foreground" aria-hidden />
        {t('source.document')}:
        <Link
          to="/documents/$documentId"
          params={{ documentId: record.documentId }}
          search={{ tab: 'overview' }}
          className="font-medium text-primary hover:underline"
          data-testid="ip-source-link"
        >
          <bdi>{record.documentNo ?? t('record.openDocument')}</bdi>
        </Link>
      </p>
    );
  if (record.requestId)
    return (
      <p className="flex items-center gap-2 text-sm">
        <Inbox className="size-4 text-muted-foreground" aria-hidden />
        {t('source.request')}:
        <Link
          to="/requests/$requestId"
          params={{ requestId: record.requestId }}
          search={{ tab: 'overview' }}
          className="font-medium text-primary hover:underline"
          data-testid="ip-source-link"
        >
          {t('record.openRequest')}
        </Link>
      </p>
    );
  return null;
}

function useAiEnabled(feature: string): boolean {
  const capabilities = useAiCapabilities();
  return capabilities.data?.enabled === true && capabilities.data.features[feature] === true;
}

function Overview({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  const { k, language } = useIpT();
  const propertyLabel = useLookupLabel(ipLookups.propertyType);
  const protectionLabel = useLookupLabel(ipLookups.protectionType);
  return (
    <div className="flex flex-col gap-4">
      <section className="rounded-lg border bg-card p-5">
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Detail label={t('fields.nameAr')}>
            <span dir="rtl" lang="ar">
              {record.nameAr ?? '—'}
            </span>
          </Detail>
          <Detail label={t('fields.nameEn')}>
            <span dir="ltr" lang="en">
              {record.nameEn ?? '—'}
            </span>
          </Detail>
          <Detail label={t('fields.propertyType')}>{propertyLabel(record.propertyType)}</Detail>
          <Detail label={t('fields.protectionType')}>{protectionLabel(record.protectionType)}</Detail>
          <Detail label={t('fields.ownerUnit')}>
            {record.ownerUnit ? pickText(language, record.ownerUnit.nameAr, record.ownerUnit.nameEn) : '—'}
          </Detail>
          <Detail label={t('fields.requester')}>
            {record.requester ? pickText(language, record.requester.nameAr, record.requester.nameEn) : '—'}
          </Detail>
          <Detail label={t('fields.source')}>{k(`source.${record.source}`)}</Detail>
          <Detail label={t('fields.internal')}>{record.internal ? t('common.yes') : t('common.no')}</Detail>
          <Detail label={t('fields.description')} wide>
            <span dir="auto" className="whitespace-pre-line">
              {record.description ?? '—'}
            </span>
          </Detail>
          {record.notes && (
            <Detail label={t('fields.notes')} wide>
              <span dir="auto" className="whitespace-pre-line">
                {record.notes}
              </span>
            </Detail>
          )}
        </dl>
      </section>
      <MoetDescription record={record} />
      <Similar record={record} />
    </div>
  );
}

/** MOET description of the work (AR / EN): an AI draft labelled "AI-generated" is saved only when the user accepts it. */
function MoetDescription({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const office = useCan('ip.request.process', 'ip.record.manage');
  const aiEnabled = useAiEnabled('ip-moet-description');
  const [draft, setDraft] = useState<IpMoetDraft>();
  const drafting = useMutation({ mutationFn: () => ipApi.draftMoetDescription(record.id) });
  const save = useIpRecordMutation(record.id, ipApi.setMoetDescription);
  const accept = () => {
    if (!draft) return;
    save.mutate(
      { descriptionAr: draft.descriptionAr, descriptionEn: draft.descriptionEn, version: record.version },
      {
        onSuccess: () => {
          setDraft(undefined);
          toast({ title: t('moet.accepted') });
        },
        onError: (e) => toast({ tone: 'error', title: actionError(tc, e) }),
      },
    );
  };
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="ip-moet-description">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('moet.title')}</h2>
        {aiEnabled && office && (
          <Button
            variant="outline"
            size="sm"
            loading={drafting.isPending}
            onClick={() =>
              drafting.mutate(undefined, {
                onSuccess: setDraft,
                onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
              })
            }
            data-testid="ip-moet-draft"
          >
            <Sparkles aria-hidden />
            {t('moet.draft')}
          </Button>
        )}
      </header>
      <div className="grid gap-4 md:grid-cols-2">
        <article dir="rtl" lang="ar" className="min-h-16 rounded-md border p-3 text-sm whitespace-pre-line">
          {record.descriptionAr ?? <span className="text-muted-foreground">{t('moet.emptyAr')}</span>}
        </article>
        <article dir="ltr" lang="en" className="min-h-16 rounded-md border p-3 text-sm whitespace-pre-line">
          {record.descriptionEn ?? <span className="text-muted-foreground">{t('moet.emptyEn')}</span>}
        </article>
      </div>
      {draft && (
        <div
          className="flex flex-col gap-3 rounded-md border border-dashed p-3"
          data-testid="ip-moet-ai-draft"
        >
          <StatusChip tone="info" className="w-fit">
            <Sparkles className="me-1 inline size-3" aria-hidden />
            {t('moet.aiGenerated')}
          </StatusChip>
          <div className="grid gap-4 md:grid-cols-2">
            <article dir="rtl" lang="ar" className="rounded-md bg-muted p-3 text-sm whitespace-pre-line">
              {draft.descriptionAr}
            </article>
            <article dir="ltr" lang="en" className="rounded-md bg-muted p-3 text-sm whitespace-pre-line">
              {draft.descriptionEn}
            </article>
          </div>
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setDraft(undefined)}>
              {t('moet.discard')}
            </Button>
            <Button size="sm" loading={save.isPending} onClick={accept} data-testid="ip-moet-accept">
              {t('moet.accept')}
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}

/** Similar IP records and published documents the user may see (no model needed; visibility rules apply on the server). */
function Similar({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const { language } = useIpT();
  const enabled = useAiEnabled('ip-similarity');
  const similar = useMutation({
    mutationFn: () =>
      ipApi.similar(pickText(language, record.nameAr, record.nameEn) || null, record.description, record.id),
  });
  if (!enabled) return null;
  const items: IpSimilarItem[] = similar.data?.items ?? [];
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="ip-similar">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('similar.title')}</h2>
        <Button variant="outline" size="sm" loading={similar.isPending} onClick={() => similar.mutate()}>
          <Search aria-hidden />
          {t('similar.check')}
        </Button>
      </header>
      {similar.isError && <StatusBanner tone="danger" title={errorMessage(tc, similar.error)} />}
      {similar.isSuccess &&
        (items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('similar.none')}</p>
        ) : (
          <ul className="flex flex-col divide-y">
            {items.map((item) => (
              <li
                key={`${item.resourceType}-${item.id}`}
                className="flex flex-wrap items-center gap-2 py-2 text-sm"
              >
                <StatusChip tone="neutral">
                  {item.resourceType === IP_OWNER_TYPE ? t('similar.ipRecord') : t('similar.document')}
                </StatusChip>
                <Link to={item.link as '/'} className="font-medium text-primary hover:underline">
                  {[item.number, pickText(language, item.titleAr, item.titleEn)].filter(Boolean).join(' · ')}
                </Link>
              </li>
            ))}
          </ul>
        ))}
    </section>
  );
}

function Registration({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  const { k } = useIpT();
  const date = useDate();
  return (
    <div className="flex flex-col gap-4">
      <section className="rounded-lg border bg-card p-5">
        <h2 className="mb-3 font-semibold">{t('registration.filing')}</h2>
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Detail label={t('fields.filingDate')}>{date(record.filingDate)}</Detail>
          <Detail label={t('fields.applicationNo')}>
            <bdi>{record.applicationNo ?? '—'}</bdi>
          </Detail>
        </dl>
      </section>
      <section className="rounded-lg border bg-card p-5">
        <h2 className="mb-3 font-semibold">{t('registration.registration')}</h2>
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Detail label={t('fields.registrationNo')}>
            <bdi>{record.registrationNo ?? '—'}</bdi>
          </Detail>
          <Detail label={t('fields.registrationDate')}>{date(record.registrationDate)}</Detail>
          <Detail label={t('fields.expiryDate')}>
            {record.expiryDate
              ? date(record.expiryDate)
              : record.registrationDate
                ? t('registration.noExpiry')
                : '—'}
          </Detail>
          {record.moetRejectionReason && (
            <Detail label={t('fields.moetRejectionReason')} wide>
              <span dir="auto" className="whitespace-pre-line">
                {record.moetRejectionReason}
              </span>
            </Detail>
          )}
        </dl>
        {!record.can.sensitive && formStatuses.has(record.status) && (
          <p className="mt-3 text-xs text-muted-foreground">{t('record.moetFormAdminsOnly')}</p>
        )}
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <h2 className="font-semibold">{t('registration.periods')}</h2>
        {record.periods.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('registration.noPeriods')}</p>
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('registration.kind')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.registrationNo')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.from')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.to')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.certificate')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {record.periods.map((p) => (
                  <TableRow key={p.id}>
                    <TableCell>{k(`periodKind.${p.kind}`)}</TableCell>
                    <TableCell>
                      <bdi>{p.registrationNo}</bdi>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">{date(p.from)}</TableCell>
                    <TableCell className="whitespace-nowrap">
                      {p.to ? date(p.to) : t('registration.noExpiry')}
                    </TableCell>
                    <TableCell>{p.certificateFileId ? t('common.yes') : '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>
    </div>
  );
}

function Linked({ record }: { record: IpRecord }) {
  const { t } = useTranslation('ip');
  if (!record.documentId && !record.requestId)
    return <EmptyState icon={FileText} title={t('linked.empty')} />;
  return (
    <div className="flex flex-col gap-3">
      {record.documentId && (
        <div
          className="flex flex-wrap items-center gap-3 rounded-lg border bg-card p-4"
          data-testid="ip-linked-document"
        >
          <FileText className="size-5 text-muted-foreground" aria-hidden />
          <span className="flex flex-col">
            <span className="text-xs text-muted-foreground">{t('linked.document')}</span>
            <Link
              to="/documents/$documentId"
              params={{ documentId: record.documentId }}
              search={{ tab: 'overview' }}
              className="font-medium text-primary hover:underline"
            >
              <bdi>{record.documentNo ?? t('record.openDocument')}</bdi>
            </Link>
          </span>
        </div>
      )}
      {record.requestId && (
        <div className="flex flex-wrap items-center gap-3 rounded-lg border bg-card p-4">
          <Inbox className="size-5 text-muted-foreground" aria-hidden />
          <span className="flex flex-col">
            <span className="text-xs text-muted-foreground">{t('linked.request')}</span>
            <Link
              to="/requests/$requestId"
              params={{ requestId: record.requestId }}
              search={{ tab: 'overview' }}
              className="font-medium text-primary hover:underline"
            >
              {t('record.openRequest')}
            </Link>
          </span>
        </div>
      )}
    </div>
  );
}
