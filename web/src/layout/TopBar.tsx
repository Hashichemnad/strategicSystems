import { Languages, Menu, Monitor, Moon, Sun, UserRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useTheme, type ThemePreference } from '@/app/theme';
import { isLanguage } from '@/i18n';
import { writePreference } from '@/lib/preferences';
import {
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/ui';

export function TopBar({ onOpenMenu }: { onOpenMenu: () => void }) {
  const { t } = useTranslation('shell');
  return (
    <header className="sticky top-0 z-20 flex h-16 items-center gap-2 border-b bg-background/90 px-4 backdrop-blur md:px-8">
      <Button
        variant="ghost"
        size="icon"
        className="md:hidden"
        onClick={onOpenMenu}
        aria-label={t('openMenu')}
      >
        <Menu aria-hidden />
      </Button>
      <div className="flex-1" />
      <LanguageToggle />
      <ThemeMenu />
      <ProfileMenu />
    </header>
  );
}

/** One click flips the language and the whole layout direction; nothing is reloaded. */
export function LanguageToggle() {
  const { t, i18n } = useTranslation('shell');
  const next = i18n.language === 'ar' ? 'en' : 'ar';
  return (
    <Button
      variant="ghost"
      onClick={() => {
        if (!isLanguage(next)) return;
        writePreference('language', next);
        void i18n.changeLanguage(next);
      }}
      aria-label={`${t('language.label')}: ${t('language.switchTo')}`}
      data-testid="language-toggle"
    >
      <Languages aria-hidden />
      <span lang={next}>{t('language.switchTo')}</span>
    </Button>
  );
}

const themeIcons = { light: Sun, dark: Moon, system: Monitor } as const;

function ThemeMenu() {
  const { t } = useTranslation('shell');
  const { theme, setTheme } = useTheme();
  const Icon = themeIcons[theme];
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t('theme.label')}>
          <Icon aria-hidden />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t('theme.label')}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={theme} onValueChange={(value) => setTheme(value as ThemePreference)}>
          {(['light', 'dark', 'system'] as const).map((option) => (
            <DropdownMenuRadioItem key={option} value={option}>
              {t(`theme.${option}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/** Placeholder until sign-in exists (Phase 1). */
function ProfileMenu() {
  const { t } = useTranslation('shell');
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="rounded-full" aria-label={t('profile.menu')}>
          <span className="flex size-8 items-center justify-center rounded-full bg-muted">
            <UserRound className="size-4" aria-hidden />
          </span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t('profile.guest')}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled>{t('profile.signInLater')}</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
