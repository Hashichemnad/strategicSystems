/** A flat node with a parent reference, as the API returns trees. */
export interface FlatNode {
  id: string;
  parentId?: string | null;
}

export interface TreeNode<T extends FlatNode> {
  item: T;
  children: TreeNode<T>[];
  depth: number;
}

/** Builds a forest from a flat list. Orphans (parent missing) become roots; input order is kept among siblings. */
export function buildTree<T extends FlatNode>(items: readonly T[]): TreeNode<T>[] {
  const nodes = new Map<string, TreeNode<T>>();
  for (const item of items) nodes.set(item.id, { item, children: [], depth: 0 });
  const roots: TreeNode<T>[] = [];
  for (const item of items) {
    const node = nodes.get(item.id);
    if (!node) continue;
    const parent = item.parentId ? nodes.get(item.parentId) : undefined;
    if (parent) parent.children.push(node);
    else roots.push(node);
  }
  const setDepth = (list: TreeNode<T>[], depth: number) => {
    for (const node of list) {
      node.depth = depth;
      setDepth(node.children, depth + 1);
    }
  };
  setDepth(roots, 0);
  return roots;
}
