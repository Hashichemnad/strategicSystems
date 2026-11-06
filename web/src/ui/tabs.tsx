import * as TabsPrimitive from '@radix-ui/react-tabs';
import { cn } from '@/lib/cn';

export const Tabs = TabsPrimitive.Root;

export function TabsList({ className, ...props }: TabsPrimitive.TabsListProps) {
  return <TabsPrimitive.List className={cn('flex gap-1 overflow-x-auto border-b', className)} {...props} />;
}

interface TabsTriggerProps extends TabsPrimitive.TabsTriggerProps {
  /**
   * The tabs filter one shared list instead of switching panels (no TabsContent): no `aria-controls` pointing to a panel that
   * does not exist (WCAG 4.1.2, axe aria-valid-attr-value).
   */
  filter?: boolean;
}

export function TabsTrigger({ className, filter, ...props }: TabsTriggerProps) {
  return (
    <TabsPrimitive.Trigger
      className={cn(
        '-mb-px border-b-2 border-transparent px-4 py-2 text-sm font-medium whitespace-nowrap text-muted-foreground hover:text-foreground data-[state=active]:border-primary data-[state=active]:text-foreground',
        className,
      )}
      {...props}
      {...(filter ? { 'aria-controls': undefined } : {})}
    />
  );
}

export function TabsContent({ className, ...props }: TabsPrimitive.TabsContentProps) {
  return <TabsPrimitive.Content className={cn('pt-4', className)} {...props} />;
}
