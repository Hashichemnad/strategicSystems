import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatNumber } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
  type Tone,
} from '@/ui';

type DemoStatus = components['schemas']['DemoPackStatusDto'];

const tones: Record<string, Tone> = {
  loaded: 'success',
  removed: 'neutral',
  failed: 'danger',
  skipped: 'neutral',
};

const statusKeys = {
  loaded: 'demoPack.statuses.loaded',
  removed: 'demoPack.statuses.removed',
  failed: 'demoPack.statuses.failed',
  skipped: 'demoPack.statuses.skipped',
} as const;

const contributorKeys = {
  platform: 'demoPack.contributors.platform',
  tasks: 'demoPack.contributors.tasks',
  teams: 'demoPack.contributors.teams',
  kpi: 'demoPack.contributors.kpi',
  risk: 'demoPack.contributors.risk',
  portfolio: 'demoPack.contributors.portfolio',
  requests: 'demoPack.contributors.requests',
  cap: 'demoPack.contributors.cap',
  auditmgmt: 'demoPack.contributors.auditmgmt',
} as const;

const has = <T extends object>(map: T, key: string): key is Extract<keyof T, string> =>
  Object.hasOwn(map, key);

/** Settings → Demo data (ADR 0153): load the optional demo pack for presentations, see what each system holds, remove it again. */
export function DemoPackPage() {
  const { t, i18n } = useTranslation('settings');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [password, setPassword] = useState('');
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [last, setLast] = useState<DemoStatus | null>(null);

  const status = useQuery({
    queryKey: ['setup', 'demo-pack'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/setup/demo-pack/status', { signal })),
  });
  const done = (result: DemoStatus, key: 'loaded' | 'removed') => {
    setLast(result);
    setPassword('');
    queryClient.setQueryData(['setup', 'demo-pack'], result);
    toast({ title: key === 'loaded' ? t('demoPack.toast.loaded') : t('demoPack.toast.removed') });
  };
  const load = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/v1/setup/demo-pack', { body: { password } })),
    onSuccess: (result) => done(result, 'loaded'),
  });
  const remove = useMutation({
    mutationFn: async () => unwrap(await api.DELETE('/api/v1/setup/demo-pack')),
    onSuccess: (result) => {
      setConfirmRemove(false);
      done(result, 'removed');
    },
  });

  const shown = last ?? status.data;
  const failed = shown?.contributors.some((c) => c.status === 'failed') ?? false;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.demoPack.title')} description={t('pages.demoPack.description')} />
      <StatusBanner tone="warning" title={t('demoPack.warningTitle')} description={t('demoPack.warning')} />
      {status.isError && <StatusBanner tone="danger" title={errorMessage(tc, status.error)} />}
      {(load.isError || remove.isError) && (
        <StatusBanner tone="danger" title={errorMessage(tc, load.error ?? remove.error)} />
      )}
      {failed && <StatusBanner tone="danger" title={t('demoPack.partial')} />}

      {status.isPending ? (
        <Skeleton className="h-32" />
      ) : shown?.loaded ? (
        <section aria-labelledby="demo-loaded" className="flex flex-col gap-3">
          <h2 id="demo-loaded" className="text-base font-semibold">
            {t('demoPack.loadedTitle', { version: shown.version })}
          </h2>
          <p className="text-sm text-muted-foreground">{t('demoPack.signIn')}</p>
          <div>
            <Button variant="danger" onClick={() => setConfirmRemove(true)} disabled={remove.isPending}>
              {t('demoPack.remove')}
            </Button>
          </div>
        </section>
      ) : (
        <form
          className="flex max-w-md flex-col gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            load.mutate();
          }}
        >
          <FormField label={t('demoPack.password')} hint={t('demoPack.passwordHint')} required>
            <Input
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </FormField>
          <div>
            <Button type="submit" disabled={password.length === 0 || load.isPending}>
              {load.isPending ? t('demoPack.loading') : t('demoPack.load')}
            </Button>
          </div>
        </form>
      )}

      {shown && shown.contributors.length > 0 && (
        <Table aria-label={t('demoPack.systems')}>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('demoPack.system')}</TableHeaderCell>
              <TableHeaderCell>{t('demoPack.status')}</TableHeaderCell>
              <TableHeaderCell className="text-end">{t('demoPack.records')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {shown.contributors.map((c) => (
              <TableRow key={c.code}>
                <TableCell>{has(contributorKeys, c.code) ? t(contributorKeys[c.code]) : c.code}</TableCell>
                <TableCell>
                  <StatusChip tone={tones[c.status] ?? 'neutral'}>
                    {has(statusKeys, c.status) ? t(statusKeys[c.status]) : c.status}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">{formatNumber(c.records, i18n.language)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <ConfirmDialog
        open={confirmRemove}
        onOpenChange={setConfirmRemove}
        title={t('demoPack.confirmTitle')}
        description={t('demoPack.confirm')}
        confirmLabel={t('demoPack.remove')}
        destructive
        loading={remove.isPending}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}
