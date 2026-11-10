import { Link } from '@tanstack/react-router';
import { ShieldHalf, UsersRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useCan } from './session';
import { memberManagePermission } from './members';

/**
 * The link bar at the top of a system's settings page (SPEC-ADDENDUM §D1): its Members page and the permission matrix ("Advanced
 * permissions"). Each link shows only with the permission its page needs; the server checks every call again.
 */
export function SystemSettingsLinks({ system }: { system: string }) {
  const { t } = useTranslation('access');
  const canMembers = useCan('access.assignment.manage', memberManagePermission(system));
  const canMatrix = useCan('access.role.read');
  if (!canMembers && !canMatrix) return null;
  return (
    <nav
      aria-label={t('members.linksLabel')}
      className="flex flex-wrap gap-2"
      data-testid="system-settings-links"
    >
      {canMembers && (
        <Link
          to="/settings/members/$system"
          params={{ system }}
          className="inline-flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm hover:bg-muted"
        >
          <UsersRound className="size-4" aria-hidden />
          {t('members.title')}
        </Link>
      )}
      {canMatrix && (
        <Link
          to="/settings/access/roles"
          search={{ system }}
          className="inline-flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm hover:bg-muted"
        >
          <ShieldHalf className="size-4" aria-hidden />
          {t('members.advanced')}
        </Link>
      )}
    </nav>
  );
}
