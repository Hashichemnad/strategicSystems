import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { lazy, Suspense, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import {
  Button,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StatusBanner,
  useToast,
  Wizard,
  type WizardStep,
} from '@/ui';
import { completeSetup, demoPackAvailable, loadDemoPack, setupStatusKey } from './api';

// The settings screens are reused as they are: each step embeds the page administrators know from Settings.
const EmailSettingsPage = lazy(() =>
  import('@/features/admin/EmailSettingsPage').then((m) => ({ default: m.EmailSettingsPage })),
);
const ProvidersPage = lazy(() =>
  import('@/features/admin/ProvidersPage').then((m) => ({ default: m.ProvidersPage })),
);
const AiSettingsPage = lazy(() =>
  import('@/features/ai/AiSettingsPage').then((m) => ({ default: m.AiSettingsPage })),
);
const BusinessHoursPage = lazy(() =>
  import('@/features/calendar/BusinessHoursPage').then((m) => ({ default: m.BusinessHoursPage })),
);
const HolidaysPage = lazy(() =>
  import('@/features/calendar/HolidaysPage').then((m) => ({ default: m.HolidaysPage })),
);
const ImportPage = lazy(() => import('@/features/org/ImportPage').then((m) => ({ default: m.ImportPage })));
const SystemsPage = lazy(() =>
  import('@/features/settings/GeneralPages').then((m) => ({ default: m.SystemsPage })),
);

type StepKey = 'email' | 'authentication' | 'ai' | 'calendar' | 'import' | 'systems' | 'demo' | 'finish';

/**
 * First-run setup, stage 2 (ADR 0150): guided configuration for the signed-in administrator. Every step is optional ("Next" skips it)
 * and can be done later in Settings; finishing marks the setup as completed so administrators are no longer sent here.
 */
export function ConfigurePage() {
  const { t } = useTranslation('setup');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const canManage = useCan('settings.setup.manage');
  const can = {
    email: useCan('identity.smtp.manage'),
    authentication: useCan('identity.provider.manage'),
    ai: useCan('ai.settings.manage'),
    calendar: useCan('calendar.business-hours.read'),
    import: useCan('org.import.manage'),
    systems: useCan('access.system.read'),
  };
  const demo = useQuery({
    queryKey: ['setup', 'demo-pack-available'],
    queryFn: ({ signal }) => demoPackAvailable(signal),
    staleTime: Infinity,
    retry: false,
  });

  if (!canManage) {
    return <EmptyState title={t('configure.forbidden')} />;
  }

  const step = (key: StepKey, content: ReactNode): WizardStep => ({
    key,
    title: t(`configure.steps.${key}`),
    content: (
      <div className="flex flex-col gap-4" data-testid={`setup-step-${key}`}>
        <StatusBanner tone="info" title={t(`configure.hints.${key}`)} />
        <Suspense fallback={<Skeleton className="h-64" />}>{content}</Suspense>
      </div>
    ),
  });

  const steps: WizardStep[] = [
    can.email && step('email', <EmailSettingsPage />),
    can.authentication && step('authentication', <ProvidersPage />),
    can.ai && step('ai', <AiSettingsPage />),
    can.calendar &&
      step(
        'calendar',
        <div className="flex flex-col gap-8">
          <BusinessHoursPage />
          <HolidaysPage />
        </div>,
      ),
    can.import && step('import', <ImportPage />),
    can.systems && step('systems', <SystemsPage />),
    demo.data === true && step('demo', <DemoStep />),
    step('finish', <p className="text-sm">{t('configure.hints.finish')}</p>),
  ].filter((s): s is WizardStep => Boolean(s));

  async function finish() {
    setBusy(true);
    try {
      await completeSetup();
      await queryClient.invalidateQueries({ queryKey: setupStatusKey });
      toast({ title: t('configure.finished') });
      await navigate({ to: '/' });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('configure.title')} description={t('configure.description')} />
      {demo.isPending ? (
        <Skeleton className="h-96" />
      ) : (
        <Wizard steps={steps} onFinish={finish} busy={busy} finishLabel={t('configure.finish')} />
      )}
    </div>
  );
}

/** Optional demo pack (only offered when this build has it). */
function DemoStep() {
  const { t } = useTranslation('setup');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  return (
    <div className="flex max-w-md flex-col gap-4">
      <FormField label={t('configure.demo.password')} hint={t('configure.demo.passwordHint')}>
        <Input
          type="password"
          dir="ltr"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
      </FormField>
      <div>
        <Button
          variant="outline"
          loading={busy}
          disabled={loaded || password.length === 0}
          onClick={() => {
            setBusy(true);
            loadDemoPack(password)
              .then(() => {
                setLoaded(true);
                toast({ title: t('configure.demo.loaded') });
              })
              .catch((failure: unknown) => toast({ tone: 'error', title: errorMessage(tc, failure) }))
              .finally(() => setBusy(false));
          }}
        >
          {t('configure.demo.load')}
        </Button>
      </div>
    </div>
  );
}
