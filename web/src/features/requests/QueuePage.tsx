import { Link } from '@tanstack/react-router';
import { CheckCheck, ListChecks, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
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
  requestApi,
  requestStatuses,
  useRequestMutation,
  useRequestQueue,
  type BulkResult,
  type RequestListItem,
} from './api';
import { RequestStatusChip, useDynamicT } from './components';
import { DueCell, useDebounced } from './MyRequestsPage';

const PAGE_SIZE = 50;
const ANY = '*';
const ACTIVE = 'active';

/**
 * Processor queue (docs/design/requests.md §2.7): requests at the processing stages the user works on, soonest due first, SLA
 * indicator, and bulk completion where the server says it is safe (no required completion field).
 */
export function RequestQueuePage() {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const dt = useDynamicT();
  const lang = i18n.language;
  const [status, setStatus] = useState(ACTIVE);
  const [type, setType] = useState(ANY);
  const [stage, setStage] = useState(ANY);
  const [overdue, setOverdue] = useState(false);
  const [text, setText] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkOpen, setBulkOpen] = useState(false);
  const [result, setResult] = useState<{ result: BulkResult; items: RequestListItem[] }>();
  const search = useDebounced(text);
  const queue = useRequestQueue({
    page,
    pageSize: PAGE_SIZE,
    ...(status !== ACTIVE ? { status } : {}),
    ...(type !== ANY ? { type } : {}),
    ...(stage !== ANY ? { stage } : {}),
    ...(search ? { search } : {}),
    ...(overdue ? { overdue: true } : {}),
  });
  const items = useMemo(() => queue.data?.items ?? [], [queue.data]);

  // Type and stage filters list what the queue holds (processors may not be submitters of the type).
  const [knownTypes, setKnownTypes] = useState<Map<string, string>>(new Map());
  const [knownStages, setKnownStages] = useState<Map<string, string>>(new Map());
  const typeOptions = new Map(knownTypes);
  const stageOptions = new Map(knownStages);
  let grew = false;
  for (const item of items) {
    if (!typeOptions.has(item.typeCode)) {
      typeOptions.set(item.typeCode, pickText(lang, item.typeNameAr, item.typeNameEn) || item.typeCode);
      grew = true;
    }
    if (item.stage && !stageOptions.has(item.stage)) {
      stageOptions.set(item.stage, pickText(lang, item.stageNameAr, item.stageNameEn) || item.stage);
      grew = true;
    }
  }
  if (grew) {
    setKnownTypes(typeOptions);
    setKnownStages(stageOptions);
  }

  const visibleIds = items.map((i) => i.id);
  const allChecked = visibleIds.length > 0 && visibleIds.every((id) => selected.has(id));
  const toggle = (id: string, on: boolean) =>
    setSelected((s) => {
      const next = new Set(s);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  const reset = () => {
    setPage(1);
    setSelected(new Set());
  };

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('queue.title')}
        description={t('queue.description')}
        actions={
          <Button
            disabled={selected.size === 0}
            onClick={() => setBulkOpen(true)}
            data-testid="queue-complete-selected"
          >
            <CheckCheck aria-hidden />
            {t('queue.completeSelected', { count: selected.size })}
          </Button>
        }
      />
      <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-5">
        <div className="relative lg:col-span-2">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            value={text}
            onChange={(e) => {
              setText(e.target.value);
              reset();
            }}
            placeholder={t('list.search')}
            aria-label={t('list.search')}
          />
        </div>
        <Select
          aria-label={t('list.filterType')}
          value={type}
          onValueChange={(value) => {
            setType(value);
            reset();
          }}
          options={[
            { value: ANY, label: t('list.anyType') },
            ...[...knownTypes].map(([value, label]) => ({ value, label })),
          ]}
        />
        <Select
          aria-label={t('list.filterStage')}
          value={stage}
          onValueChange={(value) => {
            setStage(value);
            reset();
          }}
          options={[
            { value: ANY, label: t('list.anyStage') },
            ...[...knownStages].map(([value, label]) => ({ value, label })),
          ]}
        />
        <Select
          aria-label={t('list.filterStatus')}
          value={status}
          onValueChange={(value) => {
            setStatus(value);
            reset();
          }}
          options={[
            { value: ACTIVE, label: t('queue.myWork') },
            ...requestStatuses
              .filter((s) => s !== 'draft')
              .map((s) => ({ value: s, label: dt(`status.${s}`, s) })),
          ]}
        />
      </div>
      <Switch
        className="max-w-xs"
        checked={overdue}
        onCheckedChange={(on) => {
          setOverdue(on);
          reset();
        }}
        label={t('queue.overdueOnly')}
      />
      {result && (
        <BulkOutcome result={result.result} items={result.items} onClose={() => setResult(undefined)} />
      )}
      {queue.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : queue.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, queue.error)} />
      ) : items.length === 0 ? (
        <EmptyState icon={ListChecks} title={t('queue.empty')} />
      ) : (
        <>
          <div className="overflow-x-auto rounded-lg border bg-card">
            <Table aria-busy={queue.isFetching} data-testid="queue-table">
              <TableHead>
                <TableRow>
                  <TableHeaderCell className="w-10">
                    <input
                      type="checkbox"
                      className="size-4 accent-primary"
                      aria-label={t('queue.selectAll')}
                      checked={allChecked}
                      onChange={(e) => setSelected(e.target.checked ? new Set(visibleIds) : new Set())}
                    />
                  </TableHeaderCell>
                  <TableHeaderCell>{t('list.number')}</TableHeaderCell>
                  <TableHeaderCell>{t('list.type')}</TableHeaderCell>
                  <TableHeaderCell>{t('list.title')}</TableHeaderCell>
                  <TableHeaderCell>{t('list.requester')}</TableHeaderCell>
                  <TableHeaderCell>{t('list.stage')}</TableHeaderCell>
                  <TableHeaderCell>{t('list.submitted')}</TableHeaderCell>
                  <TableHeaderCell>{t('list.due')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {items.map((r) => (
                  <TableRow key={r.id} data-state={selected.has(r.id) ? 'selected' : undefined}>
                    <TableCell>
                      <input
                        type="checkbox"
                        className="size-4 accent-primary"
                        aria-label={t('queue.selectRow', { number: r.number })}
                        checked={selected.has(r.id)}
                        onChange={(e) => toggle(r.id, e.target.checked)}
                        data-testid={`queue-select-${r.number}`}
                      />
                    </TableCell>
                    <TableCell>
                      <Link
                        to="/requests/$requestId"
                        params={{ requestId: r.id }}
                        search={{ tab: 'overview' }}
                        className="font-medium text-primary hover:underline"
                      >
                        <bdi>{r.number}</bdi>
                      </Link>
                    </TableCell>
                    <TableCell>{pickText(lang, r.typeNameAr, r.typeNameEn) || r.typeCode}</TableCell>
                    <TableCell>
                      <span dir="auto">{pickText(lang, r.titleAr, r.titleEn) || '—'}</span>
                    </TableCell>
                    <TableCell>{pickText(lang, r.requester.nameAr, r.requester.nameEn) || '—'}</TableCell>
                    <TableCell>
                      <span className="flex flex-col gap-1">
                        <span>{pickText(lang, r.stageNameAr, r.stageNameEn) || '—'}</span>
                        {r.status !== 'in_progress' && <RequestStatusChip status={r.status} />}
                      </span>
                    </TableCell>
                    <TableCell>{r.submittedAt ? formatDate(r.submittedAt, lang) : '—'}</TableCell>
                    <TableCell>
                      <DueCell item={r} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <Pagination
            page={page}
            totalPages={Math.max(1, Math.ceil(queue.data.totalCount / PAGE_SIZE))}
            totalCount={queue.data.totalCount}
            onPageChange={(p) => {
              setPage(p);
              setSelected(new Set());
            }}
          />
        </>
      )}
      {bulkOpen && (
        <BulkDialog
          ids={[...selected]}
          onClose={() => setBulkOpen(false)}
          onDone={(outcome) => {
            setResult({ result: outcome, items });
            setSelected(new Set(outcome.failed.map((f) => f.id)));
            setBulkOpen(false);
          }}
        />
      )}
    </div>
  );
}

function BulkDialog({
  ids,
  onClose,
  onDone,
}: {
  ids: string[];
  onClose: () => void;
  onDone: (result: BulkResult) => void;
}) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const bulk = useRequestMutation(requestApi.bulkComplete);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('queue.bulkTitle', { count: ids.length })}
        description={t('queue.bulkHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={bulk.isPending}
              data-testid="dialog-confirm"
              onClick={() =>
                bulk.mutate(
                  { ids, comment: comment.trim() || null },
                  {
                    onSuccess: (outcome) => {
                      if (outcome.failed.length === 0)
                        toast({ title: t('queue.bulkDone', { count: outcome.done.length }) });
                      onDone(outcome);
                    },
                  },
                )
              }
            >
              {t('queue.complete')}
            </Button>
          </>
        }
      >
        <FormField label={t('queue.bulkComment')}>
          <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
        </FormField>
        {bulk.isError && <StatusBanner tone="danger" title={errorMessage(tc, bulk.error)} />}
      </DialogContent>
    </Dialog>
  );
}

/** Partial failures stay listed (and selected) with the reason per request. */
function BulkOutcome({
  result,
  items,
  onClose,
}: {
  result: BulkResult;
  items: RequestListItem[];
  onClose: () => void;
}) {
  const { t } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const dt = useDynamicT();
  if (result.failed.length === 0) return null;
  const numberOf = (id: string) => items.find((i) => i.id === id)?.number ?? id;
  return (
    <StatusBanner
      tone="warning"
      title={t('queue.bulkPartial', { done: result.done.length, failed: result.failed.length })}
      description={
        <ul className="list-disc ps-5" data-testid="bulk-failures">
          {result.failed.map((f) => (
            <li key={f.id}>
              <bdi>{numberOf(f.id)}</bdi>:{' '}
              {dt(`queue.failures.${f.code.replaceAll('.', '_')}`, t('queue.failures.other'))}
            </li>
          ))}
        </ul>
      }
      actions={
        <Button size="sm" variant="outline" onClick={onClose}>
          {tc('actions.close')}
        </Button>
      }
    />
  );
}
