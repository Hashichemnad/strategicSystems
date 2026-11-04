import { useQueryClient } from '@tanstack/react-query';
import { Link, Outlet, useLocation, useNavigate } from '@tanstack/react-router';
import { Handshake, LogOut } from 'lucide-react';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { useBranding } from '@/features/settings/publicSettings';
import { LanguageToggle } from '@/layout/TopBar';
import { Button } from '@/ui';
import { resetWaselAntiforgery, useWaselMe, waselApi } from './waselApi';

const PUBLIC_PAGES = ['/wasel/login', '/wasel/set-password'];

/**
 * Vendor-branded bilingual layout of the Wasel portal (SPEC §1.18): installation branding, the portal name, language toggle and
 * sign-out. No internal menus. Pages other than sign-in need a vendor session (GET /api/wasel/v1/me), else /wasel/login.
 */
export function WaselLayout() {
  const { t } = useTranslation('wasel');
  const branding = useBranding();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const isPublic = PUBLIC_PAGES.some((page) => location.pathname.startsWith(page));
  const me = useWaselMe(!isPublic);

  useEffect(() => {
    if (!isPublic && me.error instanceof ApiError && me.error.status === 401) {
      void navigate({ to: '/wasel/login', search: { redirect: location.pathname } });
    }
  }, [isPublic, me.error, navigate, location.pathname]);

  async function signOut() {
    await waselApi.POST('/api/wasel/v1/auth/logout');
    resetWaselAntiforgery();
    queryClient.removeQueries({ queryKey: ['wasel'] });
    await navigate({ to: '/wasel/login', search: { redirect: undefined } });
  }

  return (
    <div className="flex min-h-dvh flex-col bg-muted/40" data-testid="wasel-layout">
      <header className="flex h-16 items-center justify-between gap-3 border-b bg-card px-4 md:px-8">
        <Link to="/wasel" className="flex min-w-0 items-center gap-3 font-semibold">
          {branding.logoUrl ? (
            <img src={branding.logoUrl} alt="" className="size-8 rounded-lg object-contain" />
          ) : (
            <span className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
              <Handshake className="size-4" aria-hidden />
            </span>
          )}
          <span className="flex min-w-0 flex-col leading-tight">
            <span className="truncate" data-testid="wasel-title">
              {t('portal.title')}
            </span>
            {branding.name && <span className="truncate text-xs text-muted-foreground">{branding.name}</span>}
          </span>
        </Link>
        <div className="flex items-center gap-2">
          {!isPublic && me.data && (
            <span
              className="hidden text-sm text-muted-foreground sm:inline"
              data-testid="wasel-user"
              dir="ltr"
            >
              {me.data.email}
            </span>
          )}
          <LanguageToggle />
          {!isPublic && me.data && (
            <Button variant="ghost" onClick={() => void signOut()} data-testid="wasel-sign-out">
              <LogOut aria-hidden />
              <span className="hidden sm:inline">{t('portal.signOut')}</span>
            </Button>
          )}
        </div>
      </header>
      <main id="main" className="flex flex-1 justify-center px-4 pt-6 pb-12">
        {isPublic ? (
          <div className="w-full max-w-md self-start rounded-xl border bg-card p-6 shadow-sm md:mt-10 md:p-8">
            <Outlet />
          </div>
        ) : me.data ? (
          <div className="w-full max-w-6xl">
            <Outlet />
          </div>
        ) : null}
      </main>
    </div>
  );
}
