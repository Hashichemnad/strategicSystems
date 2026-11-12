import { useState } from 'react';
import { useCan } from '@/features/access/session';
import { EmptyState, PageHeader, Skeleton, StatusChip, Tabs, TabsContent, TabsList, TabsTrigger } from '@/ui';
import { SYNC_FAILOVER, SYNC_MANAGE, useSalesforceConnection } from './api';
import { ConnectionTab, ScheduleTab } from './ConnectionTab';
import { ExceptionsTab } from './ExceptionsTab';
import { FailoverTab } from './FailoverTab';
import { MappingsTab } from './MappingsTab';
import { RunsTab } from './RunsTab';
import { modeTone, useSyncT } from './shared';
import { StatusTab } from './StatusTab';

const TABS = ['status', 'connection', 'schedule', 'mappings', 'exceptions', 'runs', 'failover'] as const;
type Tab = (typeof TABS)[number];

/**
 * Settings → Integrations → Salesforce sync (docs/design/salesforce-sync.md §13): one-way standby copy of Salesforce. The page
 * hides actions the user may not take; the server checks every call again (CLAUDE.md §7).
 */
export function SalesforceSyncPage() {
  const { t, tx } = useSyncT();
  const connection = useSalesforceConnection();
  const canManage = useCan(SYNC_MANAGE);
  const canFailover = useCan(SYNC_FAILOVER);
  const [tab, setTab] = useState<Tab>('status');
  if (connection.isPending) return <Skeleton className="h-64 rounded-lg" />;
  const c = connection.data;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('sync.title')}
        description={t('sync.description')}
        meta={
          c && (
            <StatusChip tone={modeTone[c.mode] ?? 'neutral'}>
              <span data-testid="sync-mode">{tx(`sync.modes.${c.mode}`, { defaultValue: c.mode })}</span>
            </StatusChip>
          )
        }
      />
      {!c ? (
        <EmptyState title={t('sync.noConnection')} description={t('sync.noConnectionHint')} />
      ) : (
        <Tabs value={tab} onValueChange={(value) => setTab(value as Tab)}>
          <TabsList>
            {TABS.filter((name) => name !== 'failover' || canFailover).map((name) => (
              <TabsTrigger key={name} value={name}>
                {tx(`sync.tabs.${name}`)}
              </TabsTrigger>
            ))}
          </TabsList>
          <TabsContent value="status">
            <StatusTab connectionId={c.id} canManage={canManage} canFailover={canFailover} onOpen={setTab} />
          </TabsContent>
          <TabsContent value="connection">
            <ConnectionTab connection={c} canManage={canManage} />
          </TabsContent>
          <TabsContent value="schedule">
            <ScheduleTab connection={c} canManage={canManage} />
          </TabsContent>
          <TabsContent value="mappings">
            <MappingsTab connection={c} canManage={canManage} />
          </TabsContent>
          <TabsContent value="exceptions">
            <ExceptionsTab connectionId={c.id} canManage={canManage} />
          </TabsContent>
          <TabsContent value="runs">
            <RunsTab connectionId={c.id} />
          </TabsContent>
          {canFailover && (
            <TabsContent value="failover">
              <FailoverTab connectionId={c.id} />
            </TabsContent>
          )}
        </Tabs>
      )}
    </div>
  );
}
