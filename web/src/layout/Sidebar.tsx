import { Link } from '@tanstack/react-router';
import { Target, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { Button } from '@/ui';
import { useAccess } from '@/features/access/session';
import { useBranding } from '@/features/settings/publicSettings';
import { visibleNavigation } from './navigation';

interface SidebarProps {
  open: boolean;
  onClose: () => void;
}

export function Sidebar({ open, onClose }: SidebarProps) {
  const { t } = useTranslation('shell');
  const { t: tc } = useTranslation();
  const access = useAccess();
  const branding = useBranding();

  return (
    <>
      {open && <div className="fixed inset-0 z-30 bg-black/40 md:hidden" onClick={onClose} aria-hidden />}
      <aside
        className={cn(
          'fixed inset-y-0 start-0 z-40 flex w-72 flex-col border-e bg-sidebar transition-transform md:sticky md:top-0 md:h-dvh md:w-64',
          // Closed on small screens: slid off-canvas and invisible, so its links leave the tab order.
          !open && 'max-md:invisible max-md:ltr:-translate-x-full max-md:rtl:translate-x-full',
        )}
      >
        <div className="flex h-16 items-center justify-between gap-2 border-b px-5">
          <Link to="/" className="flex items-center gap-2 font-semibold">
            {branding.logoUrl ? (
              <img src={branding.logoUrl} alt="" className="size-8 rounded-lg object-contain" />
            ) : (
              <span className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
                <Target className="size-4" aria-hidden />
              </span>
            )}
            <span className="truncate">{branding.name ?? tc('appName')}</span>
          </Link>
          <Button
            variant="ghost"
            size="icon"
            className="size-8 md:hidden"
            onClick={onClose}
            aria-label={t('closeMenu')}
          >
            <X aria-hidden />
          </Button>
        </div>
        <nav aria-label={t('navigation')} className="flex flex-1 flex-col gap-4 overflow-y-auto p-3">
          {visibleNavigation(access).map((section) => (
            <div key={section.key} className="flex flex-col gap-1">
              {section.key !== 'main' && (
                <p className="px-3 pb-1 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                  {t(section.labelKey)}
                </p>
              )}
              {section.items.map(({ to, labelKey, icon: Icon }) => (
                <Link
                  key={to}
                  to={to}
                  activeOptions={{ exact: to === '/', includeSearch: false }}
                  className="flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium text-muted-foreground hover:bg-muted hover:text-foreground"
                  activeProps={{
                    className: 'bg-accent text-accent-foreground hover:bg-accent',
                    'aria-current': 'page',
                  }}
                >
                  <Icon className="size-4" aria-hidden />
                  {t(labelKey)}
                </Link>
              ))}
            </div>
          ))}
        </nav>
      </aside>
    </>
  );
}
