import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { ClipboardList, Plus, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import {
  Button,
  EmptyState,
  Input,
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
import { useCapOptions, useCapSources } from './api';
import { CapPageLayout, lookupTypes, useCapLabels, useCapT, useDate, useNames } from './components';
import { SourceDialog } from './forms';

const route = getRouteApi('/app/cap/sources');
const ALL = '__all';

/** Sources of remarks (SPEC §9.1): manual sources and those raised by other systems through the contract (live link). */
export function CapSourcesPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const go = useNavigate();
  const options = useCapOptions();
  const labels = useCapLabels();
  const names = useNames();
  const date = useDate();
  const [creating, setCreating] = useState(false);
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    const handle = setTimeout(() => {
      if ((search.search ?? '') !== text)
        void navigate({ search: (p) => ({ ...p, search: text || undefined, page: undefined }) });
    }, 300);
    return () => clearTimeout(handle);
  }, [text, search.search, navigate]);
  const sources = useCapSources({
    page: search.page ?? 1,
    pageSize: 25,
    ...(search.search ? { search: search.search } : {}),
    ...(search.sourceType ? { sourceType: search.sourceType } : {}),
  });

  return (
    <CapPageLayout testId="cap-sources">
      <PageHeader
        title={t('sources.title')}
        description={t('sources.description')}
        actions={
          options.data?.canManageSources && (
            <Button onClick={() => setCreating(true)} data-testid="cap-source-new">
              <Plus aria-hidden />
              {t('sources.new')}
            </Button>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-3">
        <div className="relative">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('common.search')}
            aria-label={t('common.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
        </div>
        <Select
          aria-label={t('fields.sourceType')}
          value={search.sourceType ?? ALL}
          onValueChange={(v) =>
            void navigate({
              search: (p) => ({ ...p, sourceType: v === ALL ? undefined : v, page: undefined }),
            })
          }
          options={[
            { value: ALL, label: t('common.allTypes') },
            ...labels.lookupOptions(lookupTypes.sourceType),
          ]}
        />
      </div>
      {sources.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, sources.error)} />
      ) : !sources.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : sources.data.items.length === 0 ? (
        <EmptyState icon={ClipboardList} title={t('sources.empty')} />
      ) : (
        <>
          <Table data-testid="cap-sources-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.sourceType')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.receivedOn')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
                <TableHeaderCell>{t('sources.remarks')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {sources.data.items.map((s) => (
                <TableRow key={s.id} data-testid="cap-source-row">
                  <TableCell>
                    <Link
                      to="/cap/sources/$sourceId"
                      params={{ sourceId: s.id }}
                      className="font-medium text-primary hover:underline"
                    >
                      <bdi>{s.number}</bdi>
                    </Link>
                  </TableCell>
                  <TableCell>
                    <span className="flex flex-col">
                      <span>{names.text(s.titleAr, s.titleEn)}</span>
                      {s.origin !== 'manual' && (
                        <span className="text-xs text-muted-foreground">{k(`origin.${s.origin}`)}</span>
                      )}
                    </span>
                  </TableCell>
                  <TableCell>{labels.lookup(lookupTypes.sourceType, s.sourceTypeCode)}</TableCell>
                  <TableCell>{date(s.receivedOn)}</TableCell>
                  <TableCell>{names.unit(s.unit)}</TableCell>
                  <TableCell>
                    <bdi className="tabular-nums">
                      {s.completedRemarks} / {s.remarks}
                    </bdi>
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={s.status === 'active' ? 'info' : 'success'}>
                      {k(`sourceStatus.${s.status}`)}
                    </StatusChip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pagination
            page={sources.data.page}
            totalPages={sources.data.totalPages ?? 1}
            totalCount={sources.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
      {creating && (
        <SourceDialog
          onClose={() => setCreating(false)}
          onCreated={(id) => void go({ to: '/cap/sources/$sourceId', params: { sourceId: id } })}
        />
      )}
    </CapPageLayout>
  );
}
