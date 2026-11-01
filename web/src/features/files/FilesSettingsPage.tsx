import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { useServerErrors } from '@/features/settings/common';
import { Button, FormField, Input, PageHeader, Skeleton, Switch, useToast } from '@/ui';

type FilesSettings = components['schemas']['FilesSettings'];

/** Settings → Files: global upload limit and the optional ClamAV virus scan (SPEC §0.2). */
export function FilesSettingsPage() {
  const { t } = useTranslation('files');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const current = useQuery({
    queryKey: ['files', 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/files/settings', { signal })),
  });
  const [draft, setDraft] = useState<FilesSettings>();
  useEffect(() => setDraft(current.data?.value ?? undefined), [current.data]);
  const save = useMutation({
    mutationFn: async (value: FilesSettings) =>
      unwrap(
        await api.PUT('/api/v1/files/settings', { body: { value, version: current.data?.version ?? 0 } }),
      ),
    onSuccess: (saved) => {
      errors.clear();
      queryClient.setQueryData(['files', 'settings'], saved);
      toast({ title: t('settings.saved') });
    },
    onError: errors.fail,
  });
  if (!draft) return <Skeleton className="h-96" />;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <form
        className="flex max-w-2xl flex-col gap-5 rounded-lg border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(draft);
        }}
      >
        <FormField
          label={t('settings.maxUpload')}
          hint={t('settings.maxUploadHint')}
          error={errors.errorFor('maxUploadMegabytes')}
        >
          <Input
            type="number"
            min={1}
            max={500}
            value={draft.maxUploadMegabytes}
            onChange={(e) => setDraft({ ...draft, maxUploadMegabytes: Number(e.target.value) })}
          />
        </FormField>
        <Switch
          checked={draft.scanEnabled}
          onCheckedChange={(scanEnabled) =>
            setDraft({ ...draft, scanEnabled, scanRequired: scanEnabled && draft.scanRequired })
          }
          label={t('settings.scanEnabled')}
          description={t('settings.scanEnabledHint')}
        />
        <Switch
          checked={draft.scanRequired}
          disabled={!draft.scanEnabled}
          onCheckedChange={(scanRequired) => setDraft({ ...draft, scanRequired })}
          label={t('settings.scanRequired')}
          description={t('settings.scanRequiredHint')}
        />
        <div className="grid gap-4 sm:grid-cols-[1fr_8rem]">
          <FormField label={t('settings.clamAvHost')} error={errors.errorFor('clamAvHost')}>
            <Input
              dir="ltr"
              value={draft.clamAvHost}
              disabled={!draft.scanEnabled}
              onChange={(e) => setDraft({ ...draft, clamAvHost: e.target.value })}
            />
          </FormField>
          <FormField label={t('settings.clamAvPort')} error={errors.errorFor('clamAvPort')}>
            <Input
              type="number"
              dir="ltr"
              value={draft.clamAvPort}
              disabled={!draft.scanEnabled}
              onChange={(e) => setDraft({ ...draft, clamAvPort: Number(e.target.value) })}
            />
          </FormField>
        </div>
        <div>
          <Button type="submit" loading={save.isPending}>
            {tc('actions.save')}
          </Button>
        </div>
      </form>
    </div>
  );
}
