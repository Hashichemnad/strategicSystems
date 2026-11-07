import { Link } from '@tanstack/react-router';
import { UserCog } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { responsibilitySystems } from '@/features/settings/settingsNav';
import { useAccess } from './session';

/**
 * The entry to a system's Responsibilities page from that system's settings page (ADR 0168). Shown only to holders of
 * `<system>.responsibilities.manage`; the server checks every call again.
 */
export function ResponsibilitiesLink({ system }: { system: (typeof responsibilitySystems)[number] }) {
  const { t } = useTranslation('responsibilities');
  const access = useAccess();
  if (!access?.permissions.includes(`${system}.responsibilities.manage`)) return null;
  return (
    <Link
      to="/settings/responsibilities"
      search={{ system }}
      className="inline-flex items-center gap-1.5 self-end text-sm font-medium text-primary hover:underline"
      data-testid={`responsibilities-link-${system}`}
    >
      <UserCog className="size-4" aria-hidden />
      {t('link')}
    </Link>
  );
}
