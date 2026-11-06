import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import { ArrowDown, ArrowUp, Lock, MoveRight, Plus, Settings2, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
  EmptyState,
  FormField,
  Input,
  Select,
  Skeleton,
  StatusBanner,
  useToast,
} from '@/ui';
import type { ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  agileApi,
  categories,
  swimlanes,
  useAgile,
  useAgileMutation,
  type AgileView,
  type BoardColumn,
  type Story,
  type Swimlane,
} from './agileApi';
import { PriorityChip } from './BacklogTab';
import {
  BACKLOG,
  columnCode,
  columnStories,
  defaultSprintFilter,
  groupBySwimlane,
  moveItem,
  rankAtEnd,
  storiesInScope,
  wipReached,
} from './boardLogic';

interface DropTarget {
  column: string;
}

/** Board (design §30): configurable columns with WIP limits, swimlanes, drag-and-drop plus a keyboard "Move to…" menu. */
export function BoardTab({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const view = useAgile(project.id);
  const move = useAgileMutation(project.id, agileApi.moveStory);
  const [sprintChoice, setSprintChoice] = useState<string | null>(null);
  const [laneChoice, setLaneChoice] = useState<Swimlane | null>(null);
  const [settings, setSettings] = useState(false);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor),
  );
  const lang = i18n.language;
  if (view.isPending) return <Skeleton className="h-64" />;
  if (view.error) return <FieldErrors error={view.error} />;
  const data = view.data;
  const columns = data.board.columns;
  const sprintFilter = sprintChoice ?? defaultSprintFilter(data.sprints);
  const swimlane = laneChoice ?? (data.board.swimlane as Swimlane);
  const scope = storiesInScope(data.stories, sprintFilter);
  const lanes = groupBySwimlane(scope, swimlane, data.epics, lang);
  const canConfigure = data.permissions.canPlan || data.permissions.canRunSprints;
  const label = (c: BoardColumn) => pickText(lang, c.labelAr, c.labelEn) || c.code;

  const moveTo = (story: Story, column: string) => {
    if (story.columnCode === column || !story.canMove) return;
    const rank = rankAtEnd(columnStories(scope, columns, column), story.id);
    move.mutate(
      { sid: story.id, column, rank, version: story.version },
      {
        onSuccess: () => toast({ title: t('board.moved') }),
        onError: (error) => toast({ tone: 'error', title: errorMessage(tc, error) }),
      },
    );
  };
  const onDragEnd = (event: DragEndEvent) => {
    const story = event.active.data.current as Story | undefined;
    const target = event.over?.data.current as DropTarget | undefined;
    if (story && target) moveTo(story, target.column);
  };
  const laneTitle = (key: string) => {
    if (swimlane === 'epic') {
      const epic = data.epics.find((e) => e.id === key);
      return epic ? `${epic.code} · ${pickText(lang, epic.titleAr, epic.titleEn)}` : t('swimlane.noEpic');
    }
    if (swimlane === 'assignee') {
      const person = scope.find((s) => s.assignee?.id === key)?.assignee;
      return person ? pickText(lang, person.nameAr, person.nameEn) : t('swimlane.unassigned');
    }
    return '';
  };
  const sprintOptions = [
    ...data.sprints
      .filter((s) => s.status !== 'cancelled')
      .sort((a, b) => b.number - a.number)
      .map((s) => ({
        value: s.id,
        label: `#${formatNumber(s.number, lang)} ${s.name ?? ''} · ${t(`status.sprint.${s.status as 'planned'}`)}`,
      })),
    { value: BACKLOG, label: t('board.backlog') },
  ];

  return (
    <div className="flex flex-col gap-4" data-testid="agile-board">
      <div className="flex flex-wrap items-end gap-3">
        <FormField label={t('board.sprintFilter')} className="w-64">
          <Select
            value={sprintFilter}
            onValueChange={setSprintChoice}
            options={sprintOptions}
            aria-label={t('board.sprintFilter')}
          />
        </FormField>
        <FormField label={t('swimlane.label')} className="w-48">
          <Select
            value={swimlane}
            onValueChange={(v) => setLaneChoice(v as Swimlane)}
            options={swimlanes.map((s) => ({ value: s, label: t(`swimlane.${s}`) }))}
          />
        </FormField>
        {canConfigure && (
          <Button variant="outline" size="sm" className="ms-auto" onClick={() => setSettings(true)}>
            <Settings2 aria-hidden />
            {t('board.settings')}
          </Button>
        )}
      </div>
      <p className="text-sm text-muted-foreground">{t('board.dragHint')}</p>
      {scope.length === 0 && <EmptyState title={t('board.noStories')} />}
      <DndContext sensors={sensors} onDragEnd={onDragEnd}>
        <div className="flex flex-col gap-4">
          {lanes.map((lane) => (
            <section
              key={lane.key || '-'}
              className="flex flex-col gap-2"
              aria-label={swimlane === 'none' ? undefined : laneTitle(lane.key)}
              data-testid="board-lane"
            >
              {swimlane !== 'none' && (
                <h3 className="text-sm font-semibold" dir="auto">
                  {laneTitle(lane.key)}
                </h3>
              )}
              <div className="flex gap-3 overflow-x-auto pb-2">
                {columns.map((column) => {
                  const count = columnStories(scope, columns, column.code).length;
                  return (
                    <BoardColumnView
                      key={column.code}
                      laneKey={lane.key}
                      column={column}
                      title={label(column)}
                      count={count}
                      stories={columnStories(lane.stories, columns, column.code)}
                      columns={columns}
                      columnLabel={label}
                      onMove={moveTo}
                    />
                  );
                })}
              </div>
            </section>
          ))}
        </div>
      </DndContext>
      {settings && <BoardSettingsDialog project={project} view={data} onClose={() => setSettings(false)} />}
    </div>
  );
}

function BoardColumnView({
  laneKey,
  column,
  title,
  count,
  stories,
  columns,
  columnLabel,
  onMove,
}: {
  laneKey: string;
  column: BoardColumn;
  title: string;
  count: number;
  stories: Story[];
  columns: BoardColumn[];
  columnLabel: (c: BoardColumn) => string;
  onMove: (story: Story, column: string) => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const { setNodeRef, isOver } = useDroppable({
    id: `${laneKey}::${column.code}`,
    data: { column: column.code } satisfies DropTarget,
  });
  const full = wipReached(column, count);
  const limit = column.wipLimit;
  const lang = i18n.language;
  return (
    <section
      ref={setNodeRef}
      aria-label={title}
      className={cn(
        'flex min-w-64 flex-1 flex-col gap-2 rounded-lg border bg-muted/40 p-3',
        isOver && 'ring-2 ring-primary',
        full && 'border-danger',
      )}
      data-testid={`board-column-${column.code}`}
      data-wip-reached={full ? 'true' : undefined}
    >
      <h4 className="flex items-center justify-between gap-2 text-sm font-semibold">
        <span dir="auto">{title}</span>
        <span
          className={cn(
            'rounded-full px-2 text-xs tabular-nums',
            full ? 'bg-danger-soft text-danger' : 'bg-background',
          )}
          title={full ? t('board.wipReached') : undefined}
          aria-label={
            limit
              ? `${t('board.wip', { value: formatNumber(count, lang), limit: formatNumber(limit, lang) })}${full ? ` · ${t('board.wipReached')}` : ''}`
              : t('board.count', { value: formatNumber(count, lang) })
          }
          data-testid="board-column-count"
        >
          {limit
            ? t('board.wip', { value: formatNumber(count, lang), limit: formatNumber(limit, lang) })
            : formatNumber(count, lang)}
        </span>
      </h4>
      {stories.length === 0 ? (
        <p className="py-6 text-center text-xs text-muted-foreground">{t('board.empty')}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {stories.map((story) => (
            <StoryCard
              key={story.id}
              story={story}
              columns={columns}
              columnLabel={columnLabel}
              onMove={onMove}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

function StoryCard({
  story,
  columns,
  columnLabel,
  onMove,
}: {
  story: Story;
  columns: BoardColumn[];
  columnLabel: (c: BoardColumn) => string;
  onMove: (story: Story, column: string) => void;
}) {
  const { t, i18n } = useTranslation('agile');
  const lang = i18n.language;
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: story.id,
    data: story,
    disabled: !story.canMove,
  });
  return (
    <li
      ref={setNodeRef}
      style={transform ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)` } : undefined}
      className={cn(
        'flex flex-col gap-2 rounded-md border bg-card p-3 text-sm shadow-sm',
        isDragging && 'z-10 opacity-80 shadow-lg',
      )}
      data-testid="story-card"
      data-story={story.code}
    >
      <div
        {...listeners}
        {...attributes}
        className={cn('flex flex-col gap-1', story.canMove ? 'cursor-grab' : 'cursor-default')}
      >
        <span className="flex items-center justify-between gap-2 text-xs text-muted-foreground">
          <bdi>{story.code}</bdi>
          {story.approvedDone && (
            <Lock className="size-3.5" aria-label={t('board.locked')} data-testid="story-locked" />
          )}
        </span>
        <span className="font-medium" dir="auto">
          {story.title}
        </span>
      </div>
      <div className="flex flex-wrap items-center gap-1">
        <PriorityChip priority={story.priority} />
        <span className="rounded-full bg-muted px-2 text-xs tabular-nums">
          {t('common.pointsValue', { value: formatNumber(story.storyPoints, lang) })}
        </span>
      </div>
      <div className="flex items-center justify-between gap-2">
        <span className="truncate text-xs text-muted-foreground">
          {story.assignee
            ? pickText(lang, story.assignee.nameAr, story.assignee.nameEn)
            : t('swimlane.unassigned')}
        </span>
        {story.canMove && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button size="sm" variant="ghost" data-testid="story-move-menu">
                {/* Points along the reading direction: mirrored in Arabic. */}
                <MoveRight className="rtl:-scale-x-100" aria-hidden />
                <span className="sr-only sm:not-sr-only">{t('board.moveTo')}</span>
                {/* "Move to…" stays first in the name; the card follows for screen readers (WCAG 2.5.3). */}
                <span className="sr-only"> {story.code}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent>
              <DropdownMenuLabel>{t('board.moveTo')}</DropdownMenuLabel>
              {columns
                .filter((c) => c.code !== story.columnCode)
                .map((c) => (
                  <DropdownMenuItem
                    key={c.code}
                    onSelect={() => onMove(story, c.code)}
                    data-testid={`story-move-${c.code}`}
                  >
                    {columnLabel(c)}
                  </DropdownMenuItem>
                ))}
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </li>
  );
}

interface ColumnDraft {
  /** Existing code, or null for a column added in this dialog (code derived on save). */
  code: string | null;
  labelAr: string;
  labelEn: string;
  category: string;
  wipLimit: string;
}

function BoardSettingsDialog({
  project,
  view,
  onClose,
}: {
  project: ProjectFull;
  view: AgileView;
  onClose: () => void;
}) {
  const { t } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useAgileMutation(project.id, agileApi.setBoard);
  const [rows, setRows] = useState<ColumnDraft[]>(
    view.board.columns.map((c) => ({
      code: c.code,
      labelAr: c.labelAr ?? '',
      labelEn: c.labelEn ?? '',
      category: c.category,
      wipLimit: c.wipLimit ? String(c.wipLimit) : '',
    })),
  );
  const [lane, setLane] = useState(view.board.swimlane);
  const update = (index: number, patch: Partial<ColumnDraft>) =>
    setRows((current) => current.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  const valid =
    rows.length >= 2 && rows.some((r) => r.category === 'todo') && rows.some((r) => r.category === 'done');
  const submit = () => {
    const codes = rows.flatMap((r) => (r.code ? [r.code] : []));
    const columns: BoardColumn[] = rows.map((r) => {
      let code = r.code;
      if (!code) {
        code = columnCode(r.labelEn || r.labelAr, codes);
        codes.push(code);
      }
      const wip = Number(r.wipLimit);
      return {
        code,
        labelAr: r.labelAr.trim() || null,
        labelEn: r.labelEn.trim() || null,
        category: r.category,
        wipLimit: r.wipLimit.trim() === '' || !Number.isFinite(wip) ? null : wip,
      };
    });
    save.mutate(
      { id: project.id, board: { columns, swimlane: lane }, version: view.projectVersion },
      {
        onSuccess: () => {
          toast({ title: t('common.saved') });
          onClose();
        },
      },
    );
  };
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('board.settings')}
        description={t('board.settingsHint')}
        className="max-w-4xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!valid}
              onClick={submit}
              data-testid="board-settings-save"
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4" data-testid="board-settings-dialog">
          {save.error && <FieldErrors error={save.error} />}
          {!valid && <StatusBanner tone="warning" title={t('board.needsTodoAndDone')} />}
          <h3 className="text-sm font-semibold">{t('board.columns')}</h3>
          <ol className="flex flex-col gap-3">
            {rows.map((row, index) => (
              <li
                key={row.code ?? `new-${index}`}
                className="grid items-end gap-2 rounded-md border p-3 md:grid-cols-[1fr_1fr_10rem_7rem_auto]"
                aria-label={t('board.columnN', { n: index + 1 })}
                data-testid="board-settings-column"
              >
                <FormField label={t('board.labelAr')}>
                  <Input
                    dir="rtl"
                    value={row.labelAr}
                    onChange={(e) => update(index, { labelAr: e.target.value })}
                  />
                </FormField>
                <FormField label={t('board.labelEn')}>
                  <Input
                    dir="ltr"
                    value={row.labelEn}
                    onChange={(e) => update(index, { labelEn: e.target.value })}
                  />
                </FormField>
                <FormField label={t('board.category')}>
                  <Select
                    value={row.category}
                    onValueChange={(category) => update(index, { category })}
                    options={categories.map((c) => ({ value: c, label: t(`category.${c}`) }))}
                  />
                </FormField>
                <FormField label={t('board.wipLimit')} hint={t('board.wipLimitHint')}>
                  <Input
                    dir="ltr"
                    type="number"
                    min={1}
                    max={100}
                    value={row.wipLimit}
                    onChange={(e) => update(index, { wipLimit: e.target.value })}
                  />
                </FormField>
                <div className="flex gap-1">
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={t('board.moveUp')}
                    disabled={index === 0}
                    onClick={() => setRows((r) => moveItem(r, index, index - 1))}
                  >
                    <ArrowUp aria-hidden />
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={t('board.moveDown')}
                    disabled={index === rows.length - 1}
                    onClick={() => setRows((r) => moveItem(r, index, index + 1))}
                  >
                    <ArrowDown aria-hidden />
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={t('board.removeColumn')}
                    disabled={rows.length <= 2}
                    onClick={() => setRows((r) => r.filter((_, i) => i !== index))}
                  >
                    <Trash2 aria-hidden />
                  </Button>
                </div>
              </li>
            ))}
          </ol>
          <div className="flex flex-wrap items-end justify-between gap-3">
            <Button
              size="sm"
              variant="outline"
              disabled={rows.length >= 10}
              onClick={() =>
                setRows((r) => [
                  ...r,
                  { code: null, labelAr: '', labelEn: '', category: 'doing', wipLimit: '' },
                ])
              }
              data-testid="board-settings-add"
            >
              <Plus aria-hidden />
              {t('board.addColumn')}
            </Button>
            <FormField label={t('board.defaultSwimlane')} className="w-56">
              <Select
                value={lane}
                onValueChange={setLane}
                options={swimlanes.map((s) => ({ value: s, label: t(`swimlane.${s}`) }))}
              />
            </FormField>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
