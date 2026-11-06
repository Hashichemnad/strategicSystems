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
import { Link } from '@tanstack/react-router';
import { ArrowDown, ArrowUp, Eye, EyeOff, GripVertical, LayoutGrid, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { Button, Skeleton, StatusChip, type Tone } from '@/ui';
import { useMyWorkCards, useSaveMyWorkLayout, type MyWorkCardData } from './api';

/** Moves the item at <paramref name="from"/> to <paramref name="to"/> (pure; unit-tested). */
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return [...items];
  const copy = [...items];
  const [moved] = copy.splice(from, 1);
  copy.splice(to, 0, moved as T);
  return copy;
}

function toneOf(tone: string): Tone {
  return (['neutral', 'info', 'success', 'warning', 'danger'] as const).includes(tone as Tone)
    ? (tone as Tone)
    : 'neutral';
}

function Slot({ id, children }: { id: string; children: React.ReactNode }) {
  const { setNodeRef, isOver } = useDroppable({ id });
  return (
    <div ref={setNodeRef} className={cn('rounded-lg', isOver && 'ring-2 ring-primary')}>
      {children}
    </div>
  );
}

function CardView({
  card,
  editing,
  index,
  count,
  onMove,
  onToggle,
}: {
  card: MyWorkCardData;
  editing: boolean;
  index: number;
  count: number;
  onMove: (from: number, to: number) => void;
  onToggle: () => void;
}) {
  const { t, i18n } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: card.code,
    disabled: !editing,
  });
  const title = translateMessageKey(tc, `reporting.${card.titleKey}`) || card.code;
  const style = transform ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)` } : undefined;
  return (
    <section
      ref={setNodeRef}
      style={style}
      aria-label={title}
      data-testid={`my-work-card-${card.code}`}
      className={cn(
        'flex h-full flex-col gap-3 rounded-lg border bg-card p-4',
        card.hidden && 'opacity-60',
        isDragging && 'z-10 shadow-lg',
      )}
    >
      <header className="flex items-center justify-between gap-2">
        <h2 className="font-semibold">{card.state === 'unavailable' ? t('myWork.unavailable') : title}</h2>
        {editing ? (
          <div className="flex items-center gap-1">
            <button
              type="button"
              className="rounded p-1 text-muted-foreground hover:bg-muted"
              aria-label={t('myWork.drag', { card: title })}
              {...listeners}
              {...attributes}
            >
              <GripVertical className="size-4" aria-hidden />
            </button>
            <Button
              size="sm"
              variant="ghost"
              aria-label={t('myWork.moveUp', { card: title })}
              disabled={index === 0}
              onClick={() => onMove(index, index - 1)}
            >
              <ArrowUp className="size-4" aria-hidden />
            </Button>
            <Button
              size="sm"
              variant="ghost"
              aria-label={t('myWork.moveDown', { card: title })}
              disabled={index === count - 1}
              onClick={() => onMove(index, index + 1)}
            >
              <ArrowDown className="size-4" aria-hidden />
            </Button>
            <Button
              size="sm"
              variant="ghost"
              aria-pressed={!card.hidden}
              aria-label={card.hidden ? t('myWork.show', { card: title }) : t('myWork.hide', { card: title })}
              onClick={onToggle}
            >
              {card.hidden ? (
                <EyeOff className="size-4" aria-hidden />
              ) : (
                <Eye className="size-4" aria-hidden />
              )}
            </Button>
          </div>
        ) : (
          <Link to={card.link as '/'} className="text-sm text-primary hover:underline">
            {t('myWork.open')}
          </Link>
        )}
      </header>
      {card.counts.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {card.counts.map((count) => (
            <StatusChip key={count.key} tone={count.value > 0 ? toneOf(count.tone) : 'neutral'}>
              {translateMessageKey(tc, `reporting.myWork.counts.${count.key}`) || count.key}:{' '}
              <span className="tabular-nums">{formatNumber(count.value, i18n.language)}</span>
            </StatusChip>
          ))}
        </div>
      )}
      {card.items.length > 0 ? (
        <ul className="flex flex-col divide-y">
          {card.items.map((item) => (
            <li key={`${item.resourceType}:${item.id}`} className="flex flex-col gap-0.5 py-2">
              <Link
                to={item.link as '/'}
                className="truncate text-sm font-medium text-primary hover:underline"
              >
                {[item.number, pickText(i18n.language, item.titleAr, item.titleEn)]
                  .filter(Boolean)
                  .join(' · ')}
              </Link>
              <span className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                {translateMessageKey(tc, item.messageKey)}
                {item.dueDate && <span>{formatDate(item.dueDate, i18n.language)}</span>}
                {item.overdue && (
                  <StatusChip tone="danger">
                    <TriangleAlert className="size-3" aria-hidden /> {t('myWork.overdue')}
                  </StatusChip>
                )}
              </span>
            </li>
          ))}
        </ul>
      ) : (
        card.state !== 'unavailable' && <p className="text-sm text-muted-foreground">{t('myWork.nothing')}</p>
      )}
    </section>
  );
}

/** My Work cards from every system the user may open (SPEC §0.4 level 1, ADR 0149): reorder by drag or keyboard, hide / show, saved per user. */
export function MyWorkCards() {
  const { t } = useTranslation('reporting');
  const cards = useMyWorkCards();
  const save = useSaveMyWorkLayout();
  const [editing, setEditing] = useState(false);
  const [order, setOrder] = useState<MyWorkCardData[]>([]);
  useEffect(() => {
    if (cards.data) setOrder(cards.data.cards);
  }, [cards.data]);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  );
  const visible = useMemo(() => (editing ? order : order.filter((card) => !card.hidden)), [editing, order]);

  const persist = (next: MyWorkCardData[]) => {
    setOrder(next);
    save.mutate(next.map((card) => ({ code: card.code, hidden: card.hidden })));
  };
  const move = (from: number, to: number) => persist(moveItem(order, from, to));
  const onDragEnd = (event: DragEndEvent) => {
    if (!event.over) return;
    const from = order.findIndex((card) => card.code === event.active.id);
    const to = order.findIndex((card) => `slot-${card.code}` === event.over?.id);
    if (from >= 0 && to >= 0) move(from, to);
  };

  if (cards.isPending) return <Skeleton className="h-40 rounded-lg" />;
  if (!cards.data || order.length === 0) return null;
  return (
    <section className="flex flex-col gap-3" aria-label={t('myWork.title')} data-testid="my-work-cards">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-lg font-semibold">{t('myWork.title')}</h2>
        <Button
          size="sm"
          variant="outline"
          onClick={() => setEditing((value) => !value)}
          data-testid="my-work-customise"
        >
          <LayoutGrid className="size-4" aria-hidden />
          {editing ? t('myWork.done') : t('myWork.customise')}
        </Button>
      </div>
      {editing && <p className="text-sm text-muted-foreground">{t('myWork.customiseHint')}</p>}
      <DndContext sensors={sensors} onDragEnd={onDragEnd}>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {visible.map((card) => {
            const index = order.findIndex((c) => c.code === card.code);
            return (
              <Slot key={card.code} id={`slot-${card.code}`}>
                <CardView
                  card={card}
                  editing={editing}
                  index={index}
                  count={order.length}
                  onMove={move}
                  onToggle={() =>
                    persist(order.map((c) => (c.code === card.code ? { ...c, hidden: !c.hidden } : c)))
                  }
                />
              </Slot>
            );
          })}
        </div>
      </DndContext>
    </section>
  );
}
