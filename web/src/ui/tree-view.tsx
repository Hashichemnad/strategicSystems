import { ChevronRight } from 'lucide-react';
import { useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { buildTree, type FlatNode, type TreeNode } from './tree';

interface TreeViewProps<T extends FlatNode> {
  items: readonly T[];
  label: string;
  selectedId?: string | undefined;
  onSelect: (item: T) => void;
  renderLabel: (item: T) => ReactNode;
  /** Ids expanded initially (default: the first two levels). */
  defaultExpanded?: readonly string[];
}

/**
 * Accessible tree (WAI-ARIA tree pattern): Up / Down move, the "inward" arrow expands and the "outward" arrow
 * collapses, so ArrowLeft expands in Arabic and ArrowRight in English; Home / End jump; Enter selects.
 */
export function TreeView<T extends FlatNode>({
  items,
  label,
  selectedId,
  onSelect,
  renderLabel,
  defaultExpanded,
}: TreeViewProps<T>) {
  const { i18n } = useTranslation();
  const rtl = i18n.dir() === 'rtl';
  const roots = useMemo(() => buildTree(items), [items]);
  const [expanded, setExpanded] = useState<Set<string>>(
    () => new Set(defaultExpanded ?? items.filter((item) => !item.parentId).map((item) => item.id)),
  );
  const [focusId, setFocusId] = useState<string | undefined>(selectedId);
  const refs = useRef(new Map<string, HTMLDivElement>());

  const visible: TreeNode<T>[] = [];
  const walk = (nodes: TreeNode<T>[]) => {
    for (const node of nodes) {
      visible.push(node);
      if (expanded.has(node.item.id)) walk(node.children);
    }
  };
  walk(roots);
  const activeId = focusId && visible.some((n) => n.item.id === focusId) ? focusId : visible[0]?.item.id;

  const toggle = (id: string, open?: boolean) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (open ?? !next.has(id)) next.add(id);
      else next.delete(id);
      return next;
    });

  const focus = (id: string | undefined) => {
    if (!id) return;
    setFocusId(id);
    refs.current.get(id)?.focus();
  };

  function onKeyDown(event: KeyboardEvent, node: TreeNode<T>) {
    const index = visible.findIndex((n) => n.item.id === node.item.id);
    const inward = rtl ? 'ArrowLeft' : 'ArrowRight';
    const outward = rtl ? 'ArrowRight' : 'ArrowLeft';
    switch (event.key) {
      case 'ArrowDown':
        focus(visible[index + 1]?.item.id);
        break;
      case 'ArrowUp':
        focus(visible[index - 1]?.item.id);
        break;
      case 'Home':
        focus(visible[0]?.item.id);
        break;
      case 'End':
        focus(visible.at(-1)?.item.id);
        break;
      case inward:
        if (node.children.length && !expanded.has(node.item.id)) toggle(node.item.id, true);
        else focus(node.children[0]?.item.id);
        break;
      case outward:
        if (expanded.has(node.item.id) && node.children.length) toggle(node.item.id, false);
        else focus(node.item.parentId ?? undefined);
        break;
      case 'Enter':
      case ' ':
        onSelect(node.item);
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  const renderNodes = (nodes: TreeNode<T>[]): ReactNode =>
    nodes.map((node) => {
      const open = expanded.has(node.item.id);
      const hasChildren = node.children.length > 0;
      return (
        <div key={node.item.id} role="none">
          <div
            ref={(element) => {
              if (element) refs.current.set(node.item.id, element);
              else refs.current.delete(node.item.id);
            }}
            role="treeitem"
            aria-level={node.depth + 1}
            aria-expanded={hasChildren ? open : undefined}
            aria-selected={node.item.id === selectedId}
            tabIndex={node.item.id === activeId ? 0 : -1}
            onKeyDown={(event) => onKeyDown(event, node)}
            onClick={() => {
              setFocusId(node.item.id);
              onSelect(node.item);
            }}
            style={{ paddingInlineStart: `${node.depth * 1.25 + 0.25}rem` }}
            className={cn(
              'flex cursor-pointer items-center gap-1 rounded-md py-1.5 pe-2 text-sm outline-none hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring',
              node.item.id === selectedId && 'bg-accent text-accent-foreground hover:bg-accent',
            )}
          >
            <span
              aria-hidden
              className={cn('flex size-5 items-center justify-center', !hasChildren && 'invisible')}
              onClick={(event) => {
                event.stopPropagation();
                toggle(node.item.id);
              }}
            >
              <ChevronRight
                className={cn(
                  'size-4 transition-transform rtl:rotate-180',
                  open && 'rotate-90 rtl:rotate-90',
                )}
              />
            </span>
            {renderLabel(node.item)}
          </div>
          {hasChildren && open && <div role="group">{renderNodes(node.children)}</div>}
        </div>
      );
    });

  return (
    <div role="tree" aria-label={label} className="flex flex-col gap-0.5">
      {renderNodes(roots)}
    </div>
  );
}
