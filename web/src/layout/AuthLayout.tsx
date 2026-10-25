import { Outlet } from '@tanstack/react-router';
import { Target } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { LanguageToggle } from './TopBar';

/** Minimal bilingual layout for sign-in pages (no navigation, language toggle always available). */
export function AuthLayout() {
  const { t } = useTranslation();
  return (
    <div className="flex min-h-dvh flex-col bg-muted/40">
      <header className="flex h-16 items-center justify-between px-4 md:px-8">
        <span className="flex items-center gap-2 font-semibold">
          <span className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <Target className="size-4" aria-hidden />
          </span>
          {t('appName')}
        </span>
        <LanguageToggle />
      </header>
      <main id="main" className="flex flex-1 items-start justify-center px-4 pt-6 pb-12 md:pt-16">
        <div className="w-full max-w-md rounded-xl border bg-card p-6 shadow-sm md:p-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
