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
  ShieldCheck,
  Users,
  UsersRound,
  type LucideIcon,
} from 'lucide-react';

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
    | '/admin/email';
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
    | 'nav.email';
  icon: LucideIcon;
  /** Interim (ADR 0009): administration entries for system administrators only. Phase 2 filters by permissions. */
  adminOnly?: boolean;
}

export interface NavSection {
  key: 'main' | 'organisation' | 'administration' | 'development';
  labelKey:
    | 'nav.sections.main'
    | 'nav.sections.organisation'
    | 'nav.sections.administration'
    | 'nav.sections.development';
  items: readonly NavItem[];
}

/**
 * Sidebar sections. The server enforces every permission; hiding entries is only a convenience. From Phase 2 the list
 * is filtered by system access and the Settings module switches.
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
      { to: '/org/units', labelKey: 'nav.units', icon: Building2 },
      { to: '/org/chart', labelKey: 'nav.chart', icon: Network },
      { to: '/org/employees', labelKey: 'nav.employees', icon: UsersRound },
      { to: '/org/levels', labelKey: 'nav.levels', icon: Layers, adminOnly: true },
      { to: '/org/import', labelKey: 'nav.import', icon: FileSpreadsheet, adminOnly: true },
    ],
  },
  {
    key: 'administration',
    labelKey: 'nav.sections.administration',
    items: [
      { to: '/admin/users', labelKey: 'nav.users', icon: Users, adminOnly: true },
      { to: '/admin/providers', labelKey: 'nav.providers', icon: KeyRound, adminOnly: true },
      { to: '/admin/security', labelKey: 'nav.security', icon: ShieldCheck, adminOnly: true },
      { to: '/admin/email', labelKey: 'nav.email', icon: Mail, adminOnly: true },
    ],
  },
  {
    key: 'development',
    labelKey: 'nav.sections.development',
    items: [
      { to: '/sample', labelKey: 'nav.sample', icon: Boxes },
      { to: '/ui-kit', labelKey: 'nav.uiKit', icon: Palette },
    ],
  },
];

export function visibleNavigation(isAdmin: boolean): NavSection[] {
  return navigation
    .map((section) => ({ ...section, items: section.items.filter((item) => isAdmin || !item.adminOnly) }))
    .filter((section) => section.items.length > 0);
}
