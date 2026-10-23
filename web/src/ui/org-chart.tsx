import { ChevronDown } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { buildTree, type FlatNode, type TreeNode } from './tree';
import './org-chart.css';

interface OrgChartProps<T extends FlatNode> {
  items: readonly T[];
  label: string;
  renderNode: (item: T) => ReactNode;
  /** Levels shown before nodes start collapsed (default 3). */
  initialDepth?: number;
}

/**
 * Top-down organisation chart drawn with CSS connectors (logical properties, so it mirrors in Arabic).
 * Each box with children has a collapse / expand button. The chart scrolls inside its own frame on small screens.
 */
export function OrgChart<T extends FlatNode>({
  items,
  label,
  renderNode,
  initialDepth = 3,
}: OrgChartProps<T>) {
  const { t } = useTranslation();
  const roots = useMemo(() => buildTree(items), [items]);
  const [collapsed, setCollapsed] = useState<Set<string>>(() => new Set());
  const [expandedDeep, setExpandedDeep] = useState<Set<string>>(() => new Set());

  const isOpen = (node: TreeNode<T>) =>
    !collapsed.has(node.item.id) && (node.depth < initialDepth - 1 || expandedDeep.has(node.item.id));

  const toggle = (node: TreeNode<T>) => {
    const open = isOpen(node);
    setCollapsed((current) => {
      const next = new Set(current);
      if (open) next.add(node.item.id);
      else next.delete(node.item.id);
      return next;
    });
    setExpandedDeep((current) => {
      const next = new Set(current);
      if (!open) next.add(node.item.id);
      return next;
    });
  };

  const render = (nodes: TreeNode<T>[]): ReactNode => (
    <ul className="org-chart-level">
      {nodes.map((node) => {
        const open = isOpen(node);
        return (
          <li key={node.item.id} className="org-chart-node">
            <div className="org-chart-box">
              {renderNode(node.item)}
              {node.children.length > 0 && (
                <button
                  type="button"
                  className="mt-1 inline-flex items-center gap-1 rounded px-1 text-xs text-muted-foreground hover:bg-muted"
                  aria-expanded={open}
                  onClick={() => toggle(node)}
                >
                  <ChevronDown
                    aria-hidden
                    className={cn('size-3 transition-transform', !open && '-rotate-90 rtl:rotate-90')}
                  />
                  {open ? t('tree.collapse') : t('tree.expand', { count: node.children.length })}
                </button>
              )}
            </div>
            {open && node.children.length > 0 && render(node.children)}
          </li>
        );
      })}
    </ul>
  );

  return (
    <div role="figure" aria-label={label} className="org-chart overflow-x-auto rounded-lg border bg-card p-4">
      {render(roots)}
    </div>
  );
}
