import * as TabsPrimitive from '@radix-ui/react-tabs';
import { useAppearance } from '@/app/theme';
import { cn } from '@/lib/cn';

export const Tabs = TabsPrimitive.Root;

/** Tab strip per theme (docs/design/themes.md §4): Classic underline, Enterprise boxed tabs with a top accent bar, Modern pills. */
const listClasses = {
  classic: 'flex gap-1 overflow-x-auto border-b',
  enterprise: 'flex gap-0.5 overflow-x-auto border-b',
  modern: 'flex w-fit max-w-full gap-1 overflow-x-auto rounded-[var(--radius)] bg-muted p-1',
} as const;

const triggerClasses = {
  classic:
    '-mb-px border-b-2 border-transparent px-4 py-2 text-sm font-medium whitespace-nowrap text-muted-foreground hover:text-foreground data-[state=active]:border-primary data-[state=active]:text-foreground',
  enterprise:
    '-mb-px rounded-t-[var(--radius)] border border-t-[3px] border-transparent px-4 py-1.5 text-sm font-medium whitespace-nowrap text-muted-foreground hover:text-foreground data-[state=active]:border-x-border data-[state=active]:border-t-app-accent data-[state=active]:bg-card data-[state=active]:text-foreground',
  modern:
    'rounded-[calc(var(--radius)-4px)] px-3 py-1.5 text-sm font-medium whitespace-nowrap text-muted-foreground hover:text-foreground data-[state=active]:bg-card data-[state=active]:text-foreground data-[state=active]:shadow-[var(--shadow-card)]',
} as const;

export function TabsList({ className, ...props }: TabsPrimitive.TabsListProps) {
  const { uiTheme } = useAppearance();
  return <TabsPrimitive.List className={cn(listClasses[uiTheme], className)} {...props} />;
}

interface TabsTriggerProps extends TabsPrimitive.TabsTriggerProps {
  /**
   * The tabs filter one shared list instead of switching panels (no TabsContent): no `aria-controls` pointing to a panel that
   * does not exist (WCAG 4.1.2, axe aria-valid-attr-value).
   */
  filter?: boolean;
}

export function TabsTrigger({ className, filter, ...props }: TabsTriggerProps) {
  const { uiTheme } = useAppearance();
  return (
    <TabsPrimitive.Trigger
      className={cn(triggerClasses[uiTheme], className)}
      {...props}
      {...(filter ? { 'aria-controls': undefined } : {})}
    />
  );
}

export function TabsContent({ className, ...props }: TabsPrimitive.TabsContentProps) {
  return <TabsPrimitive.Content className={cn('pt-4', className)} {...props} />;
}
