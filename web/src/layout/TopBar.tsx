import { Link, useNavigate } from '@tanstack/react-router';
import { Languages, LogOut, Menu, Monitor, Moon, Sun, UserRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { api } from '@/api/client';
import { useTheme, type ThemePreference } from '@/app/theme';
import { useMe, useSignOut } from '@/features/auth/session';
import { isLanguage } from '@/i18n';
import { pickText } from '@/lib/format';
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
      <LanguageToggle saveToProfile />
      <ThemeMenu />
      <ProfileMenu />
    </header>
  );
}

/**
 * One click flips the language and the whole layout direction; nothing is reloaded.
 * Signed in, the choice is also saved on the profile so it follows the user to other devices.
 */
export function LanguageToggle({ saveToProfile = false }: { saveToProfile?: boolean }) {
  const { t, i18n } = useTranslation('shell');
  const next = i18n.language === 'ar' ? 'en' : 'ar';
  return (
    <Button
      variant="ghost"
      onClick={() => {
        if (!isLanguage(next)) return;
        writePreference('language', next);
        void i18n.changeLanguage(next);
        if (saveToProfile)
          void api.PUT('/api/v1/identity/me/preferences', { body: { language: next, theme: null } });
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
        <DropdownMenuRadioGroup
          value={theme}
          onValueChange={(value) => {
            setTheme(value as ThemePreference);
            void api.PUT('/api/v1/identity/me/preferences', { body: { language: null, theme: value } });
          }}
        >
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

/** The signed-in user: profile and sign out. */
function ProfileMenu() {
  const { t, i18n } = useTranslation('shell');
  const me = useMe();
  const signOut = useSignOut();
  const navigate = useNavigate();
  const name = me?.employee
    ? pickText(i18n.language, me.employee.nameAr, me.employee.nameEn)
    : (me?.userName ?? '');
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="rounded-full"
          aria-label={t('profile.menu')}
          data-testid="profile-menu"
        >
          <span className="flex size-8 items-center justify-center rounded-full bg-muted">
            <UserRound className="size-4" aria-hidden />
          </span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="flex flex-col">
          <span>{name}</span>
          <span className="text-xs font-normal text-muted-foreground" dir="ltr">
            {me?.userName}
          </span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/profile" search={{ tab: 'preferences' }}>
            <UserRound aria-hidden />
            {t('profile.myProfile')}
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem
          onSelect={() => {
            void signOut().then(() => navigate({ to: '/login', search: {} }));
          }}
        >
          <LogOut aria-hidden />
          {t('profile.signOut')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
