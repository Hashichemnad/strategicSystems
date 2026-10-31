import {
  Boxes,
  Building2,
  FileSpreadsheet,
  KeyRound,
  Layers,
  LayoutDashboard,
  Mail,
  Network,
  Palette,
  Settings2,
  ShieldCheck,
  Users,
  UsersRound,
  type LucideIcon,
} from 'lucide-react';
import type { MyAccess } from '@/features/access/session';

export interface NavItem {
  to:
    | '/'
    | '/sample'
    | '/ui-kit'
    | '/org/units'
    | '/org/chart'
    | '/org/employees'
    | '/org/levels'
    | '/org/import'
    | '/admin/users'
    | '/admin/providers'
    | '/admin/security'
    | '/admin/email'
    | '/settings';
  labelKey:
    | 'nav.home'
    | 'nav.sample'
    | 'nav.uiKit'
    | 'nav.units'
    | 'nav.chart'
    | 'nav.employees'
    | 'nav.levels'
    | 'nav.import'
    | 'nav.users'
    | 'nav.providers'
    | 'nav.security'
    | 'nav.email'
    | 'nav.settings';
  icon: LucideIcon;
  /** Shown when the user holds at least one of these permissions (any scope). */
  permissions?: readonly string[];
  /** Shown when the user may open this system (enabled in Settings and system access granted). */
  system?: string;
}

export interface NavSection {
  key: 'main' | 'organisation' | 'systems' | 'administration' | 'development';
  labelKey:
    | 'nav.sections.main'
    | 'nav.sections.organisation'
    | 'nav.sections.systems'
    | 'nav.sections.administration'
    | 'nav.sections.development';
  items: readonly NavItem[];
}

/** Every permission that opens a page of the Settings centre. */
export const settingsPermissions = [
  'settings.general.manage',
  'settings.lookup.manage',
  'settings.numbering.manage',
  'settings.feature.manage',
  'settings.translation.manage',
  'access.system.read',
  'access.role.read',
  'access.assignment.read',
  'access.review.export',
  'identity.user.manage',
  'identity.provider.manage',
  'identity.security.manage',
  'identity.smtp.manage',
  'org.unit.manage',
  'org.import.manage',
] as const;

/**
 * Sidebar sections. Entries are filtered by real access (`/api/v1/access/me`): systems the user can open and
 * permissions they hold. The server enforces every permission again; hiding entries is only a convenience.
 */
export const navigation: readonly NavSection[] = [
  {
    key: 'main',
    labelKey: 'nav.sections.main',
    items: [{ to: '/', labelKey: 'nav.home', icon: LayoutDashboard }],
  },
  {
    key: 'organisation',
    labelKey: 'nav.sections.organisation',
    items: [
      { to: '/org/units', labelKey: 'nav.units', icon: Building2, permissions: ['org.unit.read'] },
      { to: '/org/chart', labelKey: 'nav.chart', icon: Network, permissions: ['org.unit.read'] },
      {
        to: '/org/employees',
        labelKey: 'nav.employees',
        icon: UsersRound,
        permissions: ['org.employee.read'],
      },
      { to: '/org/levels', labelKey: 'nav.levels', icon: Layers, permissions: ['org.unit.manage'] },
      {
        to: '/org/import',
        labelKey: 'nav.import',
        icon: FileSpreadsheet,
        permissions: ['org.import.manage'],
      },
    ],
  },
  {
    key: 'systems',
    labelKey: 'nav.sections.systems',
    items: [{ to: '/sample', labelKey: 'nav.sample', icon: Boxes, system: 'sample' }],
  },
  {
    key: 'administration',
    labelKey: 'nav.sections.administration',
    items: [
      { to: '/settings', labelKey: 'nav.settings', icon: Settings2, permissions: settingsPermissions },
      { to: '/admin/users', labelKey: 'nav.users', icon: Users, permissions: ['identity.user.manage'] },
      {
        to: '/admin/providers',
        labelKey: 'nav.providers',
        icon: KeyRound,
        permissions: ['identity.provider.manage'],
      },
      {
        to: '/admin/security',
        labelKey: 'nav.security',
        icon: ShieldCheck,
        permissions: ['identity.security.manage'],
      },
      { to: '/admin/email', labelKey: 'nav.email', icon: Mail, permissions: ['identity.smtp.manage'] },
    ],
  },
  {
    key: 'development',
    labelKey: 'nav.sections.development',
    items: [{ to: '/ui-kit', labelKey: 'nav.uiKit', icon: Palette }],
  },
];

export function isVisible(
  item: Pick<NavItem, 'permissions' | 'system'>,
  access: MyAccess | undefined,
): boolean {
  if (item.system && !access?.systems.includes(item.system)) return false;
  if (item.permissions && !item.permissions.some((p) => access?.permissions.includes(p))) return false;
  return true;
}

export function visibleNavigation(access: MyAccess | undefined): NavSection[] {
  return navigation
    .map((section) => ({ ...section, items: section.items.filter((item) => isVisible(item, access)) }))
    .filter((section) => section.items.length > 0);
}
