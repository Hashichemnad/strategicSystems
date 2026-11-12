import { getRouteApi, Link } from '@tanstack/react-router';
import { FileDown, ScrollText, Search, ShieldCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { formatDateTime } from '@/lib/format';
import {
  buttonVariants,
  EmptyState,
  Input,
  Label,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { exportUrl, useAuditEntityTypes, useAuditSearch, type AuditSearch } from './api';
import { actionLabel, actionTone } from './HistoryTab';

const route = getRouteApi('/app/audit');
const PAGE_SIZE = 50;
const ALL = '__all';
const systems = [
  'access',
  'audit',
  'files',
  'identity',
  'notifications',
  'org',
  'sample',
  'scheduling',
  'settings',
] as const;
const channels = ['ui', 'api', 'mcp', 'ai', 'job', 'import', 'wasel', 'sync', 'system'] as const;
const actions = ['create', 'update', 'delete', 'auth', 'file', 'export', 'audit.verify', 'job'] as const;

/** Global Audit Log (SPEC §0.2): search across all systems by user, system, object, record, dates and action; export. */
export function AuditLogPage() {
  const { t, i18n } = useTranslation('audit');
  const { t: tc } = useTranslation();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const canExport = useCan('audit.log.export');
  const canVerify = useCan('audit.chain.verify');
  const [text, setText] = useState(search.text ?? '');
  const entityTypes = useAuditEntityTypes();

  const query: AuditSearch = {
    page: search.page,
    pageSize: PAGE_SIZE,
    ...(search.text ? { text: search.text } : {}),
    ...(search.system ? { system: search.system } : {}),
    ...(search.entityType ? { entityType: search.entityType } : {}),
    ...(search.recordId ? { recordId: search.recordId } : {}),
    ...(search.action ? { action: search.action } : {}),
    ...(search.channel ? { channel: search.channel } : {}),
    ...(search.from ? { from: new Date(search.from).toISOString() } : {}),
    ...(search.to ? { to: new Date(`${search.to}T23:59:59`).toISOString() } : {}),
  };
  const result = useAuditSearch(query);

  useEffect(() => {
    if (text === (search.text ?? '')) return;
    const timer = window.setTimeout(
      () => void navigate({ search: (prev) => ({ ...prev, text: text || undefined, page: 1 }) }),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [text, search.text, navigate]);

  const set = (key: 'system' | 'entityType' | 'action' | 'channel' | 'from' | 'to', value: string) =>
    void navigate({
      search: (prev) => ({ ...prev, [key]: value === ALL || value === '' ? undefined : value, page: 1 }),
    });

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('log.title')}
        description={t('log.description')}
        actions={
          <div className="flex flex-wrap gap-2">
            {canVerify && (
              <Link to="/audit/verification" className={buttonVariants({ variant: 'outline' })}>
                <ShieldCheck aria-hidden />
                {t('verification.title')}
              </Link>
            )}
            {canExport && (
              <>
                <a
                  className={buttonVariants({ variant: 'outline' })}
                  href={exportUrl(query, 'xlsx', i18n.language)}
                  download
                >
                  <FileDown aria-hidden />
                  {t('log.exportExcel')}
                </a>
                <a
                  className={buttonVariants({ variant: 'outline' })}
                  href={exportUrl(query, 'pdf', i18n.language)}
                  download
                >
                  <FileDown aria-hidden />
                  {t('log.exportPdf')}
                </a>
              </>
            )}
          </div>
        }
      />

      <div className="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="relative flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor="audit-text">{t('log.search')}</Label>
          <Search
            className="pointer-events-none absolute start-3 bottom-2.5 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            id="audit-text"
            type="search"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('log.searchPlaceholder')}
            className="ps-9"
          />
        </div>
        <Filter
          id="audit-system"
          label={t('log.system')}
          value={search.system}
          onChange={(v) => set('system', v)}
          options={systems.map((s) => ({ value: s, label: t(`systems.${s}`) }))}
          all={t('log.all')}
        />
        <Filter
          id="audit-entity"
          label={t('log.object')}
          value={search.entityType}
          onChange={(v) => set('entityType', v)}
          options={(entityTypes.data ?? []).map((e) => ({ value: e, label: e }))}
          all={t('log.all')}
        />
        <Filter
          id="audit-action"
          label={t('log.action')}
          value={search.action}
          onChange={(v) => set('action', v)}
          options={actions.map((a) => ({
            value: a,
            label: t(`actionGroups.${a.replace('.', '_')}` as 'actionGroups.create'),
          }))}
          all={t('log.all')}
        />
        <Filter
          id="audit-channel"
          label={t('log.channel')}
          value={search.channel}
          onChange={(v) => set('channel', v)}
          options={channels.map((c) => ({ value: c, label: t(`channels.${c}`) }))}
          all={t('log.all')}
        />
        <div className="flex flex-col gap-1">
          <Label htmlFor="audit-from">{t('log.from')}</Label>
          <Input
            id="audit-from"
            type="date"
            value={search.from ?? ''}
            onChange={(e) => set('from', e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="audit-to">{t('log.to')}</Label>
          <Input
            id="audit-to"
            type="date"
            value={search.to ?? ''}
            onChange={(e) => set('to', e.target.value)}
          />
        </div>
      </div>

      {result.isPending ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : result.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, result.error)} />
      ) : result.data.items.length === 0 ? (
        <EmptyState icon={ScrollText} title={t('log.empty')} />
      ) : (
        <>
          <div>
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('log.time')}</TableHeaderCell>
                  <TableHeaderCell>{t('log.user')}</TableHeaderCell>
                  <TableHeaderCell>{t('log.action')}</TableHeaderCell>
                  <TableHeaderCell>{t('log.object')}</TableHeaderCell>
                  <TableHeaderCell>{t('log.change')}</TableHeaderCell>
                  <TableHeaderCell>{t('log.channel')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {result.data.items.map((entry) => (
                  <TableRow key={entry.id}>
                    <TableCell className="whitespace-nowrap">
                      {formatDateTime(entry.occurredAt, i18n.language)}
                    </TableCell>
                    <TableCell>{entry.userName ?? t('history.system')}</TableCell>
                    <TableCell>
                      <StatusChip tone={actionTone(entry)}>{actionLabel(t, entry)}</StatusChip>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-col">
                        <span>{entry.entityType ?? '—'}</span>
                        <span className="font-mono text-xs text-muted-foreground" dir="ltr">
                          {entry.recordId}
                        </span>
                      </div>
                    </TableCell>
                    <TableCell className="max-w-md">
                      {entry.field ? (
                        <span dir="auto" className="break-all">
                          <span className="font-medium">{entry.field}: </span>
                          <span className="text-danger line-through">{entry.oldValue ?? '∅'}</span> →{' '}
                          <span className="text-success">{entry.newValue ?? '∅'}</span>
                        </span>
                      ) : (
                        <span className="line-clamp-2 text-muted-foreground" dir="auto">
                          {summary(
                            entry.action === 'delete' ? entry.oldValue : (entry.newValue ?? entry.data),
                          ) ??
                            entry.reason ??
                            ''}
                        </span>
                      )}
                    </TableCell>
                    <TableCell>
                      {t(`channels.${entry.channel as 'ui'}`, { defaultValue: entry.channel })}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <Pagination
            page={search.page}
            totalPages={Math.max(1, Math.ceil(result.data.totalCount / PAGE_SIZE))}
            totalCount={result.data.totalCount}
            onPageChange={(page) => void navigate({ search: (prev) => ({ ...prev, page }) })}
          />
        </>
      )}
    </div>
  );
}

function Filter(props: {
  id: string;
  label: string;
  value: string | undefined;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  all: string;
}) {
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={props.id}>{props.label}</Label>
      <Select
        id={props.id}
        value={props.value ?? ALL}
        onValueChange={props.onChange}
        options={[{ value: ALL, label: props.all }, ...props.options]}
      />
    </div>
  );
}

/** A create / delete snapshot or event data as short "field: value" text. */
function summary(json: string | null): string | null {
  if (!json) return null;
  try {
    const values = Object.entries(JSON.parse(json) as Record<string, unknown>)
      .filter(([, value]) => value !== null && value !== '' && typeof value !== 'object')
      .map(([key, value]) => `${key}: ${String(value)}`);
    const text = values.join(' · ');
    return text.length > 160 ? `${text.slice(0, 160)}…` : text;
  } catch {
    return json.slice(0, 160);
  }
}
