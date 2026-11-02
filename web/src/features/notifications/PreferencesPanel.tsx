import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  Skeleton,
  StatusBanner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { usePreferences, useSavePreferences, type PreferencesInput } from './api';

/** Profile → Notifications: e-mail / in-app per event and the daily digest. Mandatory security messages cannot be turned off. */
export function PreferencesPanel() {
  const { t, i18n } = useTranslation('notifications');
  const { t: tc } = useTranslation();
  const preferences = usePreferences();
  const save = useSavePreferences();
  const toast = useToast();
  const [draft, setDraft] = useState<PreferencesInput | null>(null);

  useEffect(() => {
    if (preferences.data)
      setDraft({
        digest: preferences.data.digest,
        email: preferences.data.email,
        inApp: preferences.data.inApp,
        events: preferences.data.events.map((e) => ({
          eventKey: e.eventKey,
          email: e.email,
          inApp: e.inApp,
        })),
      });
  }, [preferences.data]);

  if (preferences.isPending || !draft) return <Skeleton className="h-48 rounded-lg" />;
  if (preferences.isError) return <StatusBanner tone="danger" title={errorMessage(tc, preferences.error)} />;
  const setEvent = (key: string, change: Partial<{ email: boolean; inApp: boolean }>) =>
    setDraft({
      ...draft,
      events: (draft.events ?? []).map((e) => (e.eventKey === key ? { ...e, ...change } : e)),
    });

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3 rounded-lg border bg-card p-4">
        <Switch
          checked={draft.digest}
          onCheckedChange={(digest) => setDraft({ ...draft, digest })}
          label={t('preferences.digest')}
          description={t('preferences.digestHint')}
        />
      </div>
      <div className="overflow-x-auto rounded-lg border bg-card">
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('preferences.event')}</TableHeaderCell>
              <TableHeaderCell>{t('preferences.email')}</TableHeaderCell>
              <TableHeaderCell>{t('preferences.inApp')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {preferences.data.events.map((event) => {
              const value = draft.events?.find((e) => e.eventKey === event.eventKey);
              return (
                <TableRow key={event.eventKey}>
                  <TableCell>
                    {pickText(i18n.language, event.nameAr, event.nameEn)}
                    {event.mandatory && (
                      <span className="ms-2 text-xs text-muted-foreground">{t('preferences.mandatory')}</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <Switch
                      checked={event.mandatory || (value?.email ?? true)}
                      disabled={event.mandatory}
                      onCheckedChange={(email) => setEvent(event.eventKey, { email })}
                      label={t('preferences.emailFor', {
                        name: pickText(i18n.language, event.nameAr, event.nameEn),
                      })}
                      hideLabel
                    />
                  </TableCell>
                  <TableCell>
                    <Switch
                      checked={value?.inApp ?? true}
                      onCheckedChange={(inApp) => setEvent(event.eventKey, { inApp })}
                      label={t('preferences.inAppFor', {
                        name: pickText(i18n.language, event.nameAr, event.nameEn),
                      })}
                      hideLabel
                    />
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
      <div>
        <Button
          loading={save.isPending}
          onClick={() =>
            save.mutate(draft, {
              onSuccess: () => toast({ title: t('preferences.saved') }),
              onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
            })
          }
        >
          {tc('actions.save')}
        </Button>
      </div>
    </div>
  );
}
