import { getRouteApi, Link } from '@tanstack/react-router';
import {
  AlarmClock,
  ArrowLeft,
  BellRing,
  CalendarCheck,
  CalendarClock,
  CheckCheck,
  Download,
  Eye,
  FilePlus2,
  FileX2,
  GitCompare,
  History,
  Pencil,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { LinkBadge } from '@/features/links/LinkBadge';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  DatePicker,
  EmptyState,
  FormField,
  PageHeader,
  RecordLayout,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import {
  daysUntil,
  docregApi,
  downloadUrl,
  isConflict,
  newVersionValues,
  useDocRegAi,
  useDocRegMutation,
  useDocument,
  versionTone,
  withdrawalValues,
  type DocumentDetail,
  type SummaryDraft,
} from './api';
import {
  AiDraftPanel,
  DocRegNav,
  DocRegPage,
  DocStatusChip,
  documentName,
  SensitivityChip,
  StampedPdfViewer,
  useDocT,
  useFormatDate,
  useLookupLabel,
  ValidityChips,
} from './components';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/documents/$documentId');
type DocumentTab = 'overview' | 'versions' | 'validity' | 'linked' | 'tasks' | 'history';

/** Document page (docs/design/requests.md §3.6): header, validity banner with the owner's actions, and the record tabs. */
export function DocumentPage() {
  const { t } = useTranslation('docreg');
  const { t: tc, i18n } = useTranslation();
  const { documentId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const document = useDocument(documentId);
  const [dialog, setDialog] = useState<'confirm' | 'expiry'>();
  const toast = useToast();
  const acknowledge = useDocRegMutation(docregApi.acknowledge);

  if (document.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (document.isError)
    return (
      <DocRegPage>
        <StatusBanner tone="danger" title={errorMessage(tc, document.error)} />
      </DocRegPage>
    );
  const doc = document.data;
  const name = documentName(i18n.language, doc, t('document.untitled'));
  const can = doc.can;

  const day = (value: string | null) => (value ? formatDate(value, i18n.language) : '—');
  // DocRegPage's secondary navigation comes first (Classic keeps the same order); the record layout adds the theme's slots.
  return (
    <RecordLayout
      testId="document-page"
      back={
        <>
          <DocRegNav />
          <Link
            to="/documents"
            className="flex w-fit items-center gap-1 text-sm text-primary hover:underline"
          >
            <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
            {t('document.back')}
          </Link>
        </>
      }
      header={
        <PageHeader
          title={name}
          description={
            <span className="flex flex-wrap items-center gap-x-2">
              <bdi data-testid="document-number">{doc.documentNo ?? t('document.noNumber')}</bdi>
              {doc.currentVersion && (
                <>
                  <span aria-hidden>·</span>
                  <span>
                    {t('fields.version')} <bdi>{doc.currentVersion}</bdi>
                  </span>
                </>
              )}
            </span>
          }
          meta={
            <>
              <OriginBadge type="docreg.document" id={doc.id} />
              <DocStatusChip status={doc.status} />
              <SensitivityChip code={doc.sensitivity} />
              <ValidityChips
                status={doc.status}
                expiryDate={doc.expiryDate}
                underRevision={doc.underRevision}
              />
            </>
          }
          actions={
            <div className="flex flex-wrap gap-2">
              {can.download && (
                <Button variant="outline" asChild>
                  <a
                    href={downloadUrl(doc.id)}
                    data-testid="document-download"
                    title={t('document.downloadHint')}
                  >
                    <Download aria-hidden />
                    {t('document.download')}
                  </a>
                </Button>
              )}
              {can.acknowledge && (
                <Button
                  loading={acknowledge.isPending}
                  data-testid="document-acknowledge"
                  onClick={() =>
                    acknowledge.mutate(doc.id, {
                      onSuccess: () => toast({ tone: 'success', title: t('document.acknowledgedToast') }),
                      onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                    })
                  }
                >
                  <CheckCheck aria-hidden />
                  {t('document.acknowledge')}
                </Button>
              )}
            </div>
          }
        />
      }
      highlights={[
        { key: 'number', label: t('fields.documentNo'), value: <bdi>{doc.documentNo ?? '—'}</bdi> },
        { key: 'version', label: t('fields.version'), value: <bdi>{doc.currentVersion ?? '—'}</bdi> },
        {
          key: 'ownerUnit',
          label: t('fields.ownerUnit'),
          value: doc.ownerUnit ? pickText(i18n.language, doc.ownerUnit.nameAr, doc.ownerUnit.nameEn) : '—',
        },
        {
          key: 'owner',
          label: t('fields.owner'),
          value: doc.owner ? pickText(i18n.language, doc.owner.nameAr, doc.owner.nameEn) : '—',
        },
        { key: 'effective', label: t('fields.effectiveDate'), value: day(doc.effectiveDate) },
        { key: 'expiry', label: t('fields.expiryDate'), value: day(doc.expiryDate) },
      ]}
      status={
        <>
          {doc.status === 'withdrawn' && (
            <StatusBanner
              tone="warning"
              title={t('document.withdrawnBanner')}
              description={doc.withdrawnReason ?? undefined}
            />
          )}
          {!['published', 'expired', 'withdrawn'].includes(doc.status) && (
            <StatusBanner tone="info" title={t('document.inReview')} />
          )}
          {(doc.status === 'published' || doc.status === 'expired') && (
            <ValidityBanner
              doc={doc}
              onConfirm={() => setDialog('confirm')}
              onEditExpiry={() => setDialog('expiry')}
            />
          )}
          <AckLine doc={doc} />
        </>
      }
      activeSection={tab}
      onSectionChange={(key) => void navigate({ search: { tab: key as DocumentTab } })}
      sections={[
        {
          key: 'overview',
          label: t('document.tabs.overview'),
          testId: 'document-tab-overview',
          content: (
            <>
              <Overview doc={doc} name={name} />
              <ExternalReferencesSection type="docreg.document" id={doc.id} />
            </>
          ),
        },
        {
          key: 'versions',
          label: t('document.tabs.versions'),
          testId: 'document-tab-versions',
          count: doc.versions.length,
          content: <Versions doc={doc} />,
        },
        {
          key: 'validity',
          label: t('document.tabs.validity'),
          testId: 'document-tab-validity',
          count: doc.validity.length,
          content: <ValidityTimeline doc={doc} />,
        },
        {
          key: 'linked',
          label: t('document.tabs.linked'),
          testId: 'document-tab-linked',
          count: doc.linked.length,
          content: (
            <>
              {doc.linked.length === 0 ? (
                <EmptyState title={t('linked.empty')} />
              ) : (
                <ul
                  className="flex flex-col gap-3 rounded-lg border bg-card p-4"
                  data-testid="document-linked"
                >
                  {doc.linked.map((view) => (
                    <li key={`${view.type}:${view.id}`}>
                      <LinkBadge view={view} />
                    </li>
                  ))}
                </ul>
              )}
            </>
          ),
        },
        {
          key: 'tasks',
          label: t('document.tabs.tasks'),
          testId: 'document-tab-tasks',
          content: <>{tab === 'tasks' && <LinkedTasksPanel type="docreg.document" id={doc.id} />}</>,
        },
        {
          key: 'history',
          label: t('document.tabs.history'),
          testId: 'document-tab-history',
          content: <>{tab === 'history' && <HistoryTab record="document" id={doc.id} />}</>,
        },
      ]}
    >
      {dialog === 'confirm' && <ConfirmDialog doc={doc} onClose={() => setDialog(undefined)} />}
      {dialog === 'expiry' && <ExpiryDialog doc={doc} onClose={() => setDialog(undefined)} />}
    </RecordLayout>
  );
}

/** Expiry, days left, expired warning and the owner's actions (Confirm still valid, new version, withdrawal, edit expiry). */
function ValidityBanner({
  doc,
  onConfirm,
  onEditExpiry,
}: {
  doc: DocumentDetail;
  onConfirm: () => void;
  onEditExpiry: () => void;
}) {
  const { t } = useTranslation('docreg');
  const date = useFormatDate();
  const can = doc.can;
  const days = doc.expiryDate ? daysUntil(doc.expiryDate) : null;
  const expired = doc.status === 'expired';
  const tone = expired ? 'danger' : days != null && days <= 90 ? 'warning' : 'info';
  const title = !doc.expiryDate
    ? t('validity.noExpiry')
    : expired
      ? t('validity.expiredOn', { date: date(doc.expiryDate) })
      : t('validity.expiresOn', { date: date(doc.expiryDate) });
  const description = expired
    ? t('validity.expiredHint')
    : days != null && days >= 0
      ? t('validity.daysLeft', { count: days })
      : undefined;
  const actions =
    can.confirmValid || can.newVersion || can.withdraw || can.editExpiry ? (
      <div className="flex flex-wrap gap-2">
        {can.confirmValid && (
          <Button size="sm" onClick={onConfirm} data-testid="document-confirm-valid">
            <ShieldCheck aria-hidden />
            {t('document.confirmValid')}
          </Button>
        )}
        {can.newVersion && (
          <Button size="sm" variant="outline" asChild>
            <Link
              to="/requests/new/$typeCode"
              params={{ typeCode: 'docreg.document' }}
              search={{ values: newVersionValues(doc) }}
              data-testid="document-new-version"
            >
              <FilePlus2 aria-hidden />
              {t('document.newVersion')}
            </Link>
          </Button>
        )}
        {can.withdraw && (
          <Button size="sm" variant="outline" asChild>
            <Link
              to="/requests/new/$typeCode"
              params={{ typeCode: 'docreg.withdrawal' }}
              search={{ values: withdrawalValues(doc.id) }}
              data-testid="document-withdraw"
            >
              <FileX2 aria-hidden />
              {t('document.withdraw')}
            </Link>
          </Button>
        )}
        {can.editExpiry && (
          <Button size="sm" variant="ghost" onClick={onEditExpiry} data-testid="document-edit-expiry">
            <Pencil aria-hidden />
            {t('document.editExpiry')}
          </Button>
        )}
      </div>
    ) : undefined;
  return (
    <div data-testid="document-validity">
      <StatusBanner tone={tone} title={title} description={description} actions={actions} />
    </div>
  );
}

function AckLine({ doc }: { doc: DocumentDetail }) {
  const { t } = useTranslation('docreg');
  if (!doc.acknowledgements.required && doc.acknowledgements.count === 0) return null;
  return (
    <p className="flex flex-wrap items-center gap-2 text-sm" data-testid="document-acks">
      {doc.acknowledgements.required && <StatusChip tone="info">{t('document.ackRequired')}</StatusChip>}
      <span className="text-muted-foreground">
        {t('document.ackCount', { count: doc.acknowledgements.count })}
      </span>
      {doc.acknowledgements.mine && (
        <StatusChip tone="success" className="gap-1">
          <CheckCheck className="size-3" aria-hidden />
          {t('document.acknowledged')}
        </StatusChip>
      )}
    </p>
  );
}

function Overview({ doc, name }: { doc: DocumentDetail; name: string }) {
  const { t, i18n } = useTranslation('docreg');
  const { t: tc } = useTranslation();
  const date = useFormatDate();
  const levelLabel = useLookupLabel('docreg.level');
  const typeLabel = useLookupLabel('docreg.type');
  const aiSummary = useDocRegAi('docreg-summary');
  const toast = useToast();
  const [preview, setPreview] = useState(false);
  const [summary, setSummary] = useState<SummaryDraft>();
  const [busy, setBusy] = useState(false);
  const current = doc.versions.find((v) => v.id === doc.currentVersionId);
  const stored = current?.summaryAr || current?.summaryEn ? current : undefined;

  const rows: [string, React.ReactNode][] = [
    [t('fields.documentNo'), <bdi key="no">{doc.documentNo ?? '—'}</bdi>],
    [
      t('fields.nameAr'),
      <span key="ar" dir="rtl" lang="ar">
        {doc.nameAr || '—'}
      </span>,
    ],
    [
      t('fields.nameEn'),
      <span key="en" dir="ltr" lang="en">
        {doc.nameEn || '—'}
      </span>,
    ],
    [t('fields.level'), levelLabel(doc.level)],
    [t('fields.type'), typeLabel(doc.docType)],
    [t('fields.sensitivity'), <SensitivityChip key="s" code={doc.sensitivity} />],
    [
      t('fields.ownerUnit'),
      doc.ownerUnit ? pickText(i18n.language, doc.ownerUnit.nameAr, doc.ownerUnit.nameEn) : '—',
    ],
    [t('fields.owner'), doc.owner ? pickText(i18n.language, doc.owner.nameAr, doc.owner.nameEn) : '—'],
    [t('fields.version'), <bdi key="v">{doc.currentVersion ?? '—'}</bdi>],
    [t('fields.effectiveDate'), date(doc.effectiveDate)],
    [t('fields.expiryDate'), doc.expiryDate ? date(doc.expiryDate) : t('common.untilSuperseded')],
    [
      t('fields.validity'),
      doc.validityMonths ? t('common.months', { count: doc.validityMonths }) : t('common.untilSuperseded'),
    ],
    [t('fields.shareToAll'), doc.shareToAll ? t('common.yes') : t('common.no')],
  ];

  return (
    <div className="flex flex-col gap-4">
      <section className="grid gap-4 rounded-lg border bg-card p-5 lg:grid-cols-[2fr_3fr]">
        <dl className="grid grid-cols-[minmax(7rem,auto)_1fr] gap-x-4 gap-y-2 text-sm">
          {rows.map(([label, value]) => (
            <div key={label} className="contents">
              <dt className="text-muted-foreground">{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
        <div className="flex flex-col gap-2">
          <h2 className="font-semibold">{t('fields.description')}</h2>
          <p dir="auto" className="whitespace-pre-line text-sm">
            {doc.description || '—'}
          </p>
        </div>
      </section>

      {(aiSummary || stored) && (
        <section className="flex flex-col gap-3">
          {aiSummary && !summary && (
            <Button
              variant="outline"
              className="w-fit"
              loading={busy}
              data-testid="document-ai-summary"
              onClick={() => {
                setBusy(true);
                docregApi
                  .summary(doc.id)
                  .then(setSummary)
                  .catch((e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) }))
                  .finally(() => setBusy(false));
              }}
            >
              <Sparkles aria-hidden />
              {t('ai.summary')}
            </Button>
          )}
          {summary ? (
            <AiDraftPanel
              title={t('ai.summaryTitle')}
              ar={summary.summaryAr}
              en={summary.summaryEn}
              testId="document-summary"
            />
          ) : (
            stored && (
              <AiDraftPanel
                title={t('ai.summaryTitle')}
                ar={stored.summaryAr ?? ''}
                en={stored.summaryEn ?? ''}
              />
            )
          )}
        </section>
      )}

      <section
        className="flex flex-col gap-3 rounded-lg border bg-card p-5"
        aria-label={t('document.viewer')}
      >
        <header className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">
            {t('document.viewer')}
            {doc.currentVersion && (
              <span className="ms-2 text-sm font-normal text-muted-foreground">
                <bdi>{doc.currentVersion}</bdi>
              </span>
            )}
          </h2>
          {doc.can.download && (
            <div className="flex flex-wrap gap-2">
              {!preview && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPreview(true)}
                  data-testid="document-preview"
                >
                  <Eye aria-hidden />
                  {t('document.showPreview')}
                </Button>
              )}
              <Button variant="ghost" size="sm" asChild>
                <a href={downloadUrl(doc.id)}>
                  <Download aria-hidden />
                  {t('document.download')}
                </a>
              </Button>
            </div>
          )}
        </header>
        {!doc.can.download ? (
          <p className="text-sm text-muted-foreground">{t('document.noFile')}</p>
        ) : preview ? (
          <StampedPdfViewer documentId={doc.id} name={name} />
        ) : (
          <p className="text-sm text-muted-foreground">{t('document.downloadHint')}</p>
        )}
      </section>
    </div>
  );
}

function Versions({ doc }: { doc: DocumentDetail }) {
  const { t, i18n } = useTranslation('docreg');
  const { k } = useDocT();
  const [selected, setSelected] = useState<string[]>([]);
  const toggle = (id: string) =>
    setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s.slice(-1), id]));
  if (doc.versions.length === 0) return <EmptyState title={t('versions.empty')} />;
  // Versions arrive newest first: the older selected one is "from".
  const order = (id: string) => doc.versions.findIndex((v) => v.id === id);
  const [from, to] = [...selected].sort((a, b) => order(b) - order(a));

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        {selected.length === 2 && from && to ? (
          <Button asChild size="sm">
            <Link
              to="/documents/$documentId/compare"
              params={{ documentId: doc.id }}
              search={{ from, to }}
              data-testid="compare-link"
            >
              <GitCompare aria-hidden />
              {t('versions.compare')}
            </Link>
          </Button>
        ) : (
          doc.versions.length > 1 && (
            <p className="text-sm text-muted-foreground">{t('versions.compareHint')}</p>
          )
        )}
      </div>
      <Table data-testid="document-versions">
        <TableHead>
          <TableRow>
            <TableHeaderCell>
              <span className="sr-only">{t('versions.compare')}</span>
            </TableHeaderCell>
            <TableHeaderCell>{t('versions.label')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
            <TableHeaderCell>{t('versions.changeSummary')}</TableHeaderCell>
            <TableHeaderCell>{t('versions.requestedBy')}</TableHeaderCell>
            <TableHeaderCell>{t('fields.publishedAt')}</TableHeaderCell>
            <TableHeaderCell>{t('versions.request')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{t('document.download')}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {doc.versions.map((v) => {
            const label = v.label ?? '—';
            const published = v.publishedAt != null && v.finalFileId != null;
            return (
              <TableRow key={v.id} data-testid={`version-row-${v.id}`}>
                <TableCell>
                  {v.hasText && (
                    <Checkbox
                      label={<span className="sr-only">{t('versions.select', { label })}</span>}
                      checked={selected.includes(v.id)}
                      onChange={() => toggle(v.id)}
                    />
                  )}
                </TableCell>
                <TableCell className="font-medium tabular-nums">
                  <bdi>{label}</bdi>
                </TableCell>
                <TableCell>
                  <StatusChip tone={versionTone[v.status] ?? 'neutral'}>
                    {k(`versionStatus.${v.status}`, { defaultValue: v.status })}
                  </StatusChip>
                </TableCell>
                <TableCell className="max-w-xs">
                  <span dir="auto" className="whitespace-pre-line">
                    {v.changeSummary || '—'}
                  </span>
                </TableCell>
                <TableCell>
                  {v.requestedBy ? pickText(i18n.language, v.requestedBy.nameAr, v.requestedBy.nameEn) : '—'}
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  {v.publishedAt ? formatDateTime(v.publishedAt, i18n.language) : '—'}
                </TableCell>
                <TableCell>
                  <Link
                    to="/requests/$requestId"
                    params={{ requestId: v.requestId }}
                    search={{ tab: 'overview' }}
                    className="text-primary hover:underline"
                  >
                    <bdi>{v.requestNo ?? t('versions.request')}</bdi>
                  </Link>
                </TableCell>
                <TableCell>
                  {published && doc.can.download && (
                    <Button variant="ghost" size="icon" asChild>
                      <a
                        href={downloadUrl(doc.id, v.id)}
                        aria-label={t('versions.downloadVersion', { label })}
                      >
                        <Download aria-hidden />
                      </a>
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}

const kindIcon = {
  reminder: BellRing,
  confirmed: CalendarCheck,
  expired: AlarmClock,
  'expiry-changed': CalendarClock,
} as const;

function ValidityTimeline({ doc }: { doc: DocumentDetail }) {
  const { t, i18n } = useTranslation('docreg');
  const { k } = useDocT();
  const date = useFormatDate();
  if (doc.validity.length === 0) return <EmptyState icon={History} title={t('validity.noEvents')} />;
  return (
    <ol
      className="flex flex-col gap-3"
      aria-label={t('validity.events')}
      data-testid="document-validity-events"
    >
      {doc.validity.map((event, index) => {
        const Icon = event.kind in kindIcon ? kindIcon[event.kind as keyof typeof kindIcon] : History;
        return (
          <li key={`${event.at}-${index}`} className="flex gap-3 rounded-lg border bg-card p-4 text-sm">
            <Icon
              className={
                event.kind === 'expired'
                  ? 'size-5 shrink-0 text-danger'
                  : 'size-5 shrink-0 text-muted-foreground'
              }
              aria-hidden
            />
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">
                  {k(`validityKind.${event.kind}`, { defaultValue: event.kind })}
                </span>
                {event.days != null && (
                  <span className="text-muted-foreground">
                    {t('validity.reminderDays', { count: event.days })}
                  </span>
                )}
                <time className="ms-auto text-muted-foreground" dateTime={event.at}>
                  {formatDateTime(event.at, i18n.language)}
                </time>
              </div>
              {(event.oldExpiry || event.newExpiry) && (
                <p className="text-muted-foreground">
                  {t('validity.change', {
                    from: event.oldExpiry ? date(event.oldExpiry) : t('common.untilSuperseded'),
                    to: event.newExpiry ? date(event.newExpiry) : t('common.untilSuperseded'),
                  })}
                </p>
              )}
              {event.comment && (
                <p dir="auto" className="whitespace-pre-line">
                  {event.comment}
                </p>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

function ConfirmDialog({ doc, onClose }: { doc: DocumentDetail; onClose: () => void }) {
  const { t } = useTranslation('docreg');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const confirm = useDocRegMutation(docregApi.confirmValid);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('document.confirmTitle')}
        description={t('document.confirmDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={confirm.isPending}
              data-testid="document-confirm-submit"
              onClick={() =>
                confirm.mutate(
                  { id: doc.id, comment: comment.trim() || null },
                  {
                    onSuccess: () => {
                      toast({ tone: 'success', title: t('document.confirmed') });
                      onClose();
                    },
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {t('document.confirmValid')}
            </Button>
          </>
        }
      >
        <FormField label={t('document.comment')}>
          <Textarea value={comment} onChange={(e) => setComment(e.target.value)} maxLength={1000} rows={3} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

function ExpiryDialog({ doc, onClose }: { doc: DocumentDetail; onClose: () => void }) {
  const { t } = useTranslation('docreg');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useDocRegMutation(docregApi.setExpiry);
  const [expiryDate, setExpiryDate] = useState<string | undefined>(doc.expiryDate ?? undefined);
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string>();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('document.expiryTitle')}
        description={t('document.expiryDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    id: doc.id,
                    expiryDate: expiryDate ?? null,
                    comment: comment.trim() || null,
                    version: doc.version,
                  },
                  {
                    onSuccess: () => {
                      toast({ tone: 'success', title: t('document.expirySaved') });
                      onClose();
                    },
                    onError: (e) => {
                      if (isConflict(e)) {
                        toast({ tone: 'error', title: t('document.conflict') });
                        onClose();
                      } else setError(errorMessage(tc, e));
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <FormField label={t('fields.expiryDate')} error={error}>
          <DatePicker value={expiryDate} onChange={setExpiryDate} />
        </FormField>
        {expiryDate && (
          <Button variant="link" className="w-fit px-0" onClick={() => setExpiryDate(undefined)}>
            {t('validity.noExpiry')}
          </Button>
        )}
        <FormField label={t('document.comment')}>
          <Textarea value={comment} onChange={(e) => setComment(e.target.value)} maxLength={1000} rows={3} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}
