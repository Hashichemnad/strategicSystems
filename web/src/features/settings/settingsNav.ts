import {
  Building,
  FileSpreadsheet,
  Flag,
  Hash,
  KeyRound,
  Languages,
  Layers,
  ListChecks,
  Mail,
  MapPinned,
  Palette,
  Repeat,
  ShieldCheck,
  ShieldHalf,
  SquareStack,
  UserCheck,
  Users,
  type LucideIcon,
} from 'lucide-react';

export type SettingsPageKey =
  | 'regional'
  | 'branding'
  | 'systems'
  | 'features'
  | 'lookups'
  | 'numbering'
  | 'translations'
  | 'roles'
  | 'assignments'
  | 'systemAccess'
  | 'transfer'
  | 'review'
  | 'users'
  | 'providers'
  | 'security'
  | 'email'
  | 'levels'
  | 'import';

export type SettingsPath =
  | '/settings/regional'
  | '/settings/branding'
  | '/settings/systems'
  | '/settings/features'
  | '/settings/lookups'
  | '/settings/numbering'
  | '/settings/translations'
  | '/settings/access/roles'
  | '/settings/access/assignments'
  | '/settings/access/system-access'
  | '/settings/access/transfer'
  | '/settings/access/review'
  | '/admin/users'
  | '/admin/providers'
  | '/admin/security'
  | '/admin/email'
  | '/org/levels'
  | '/org/import';

export interface SettingsPageLink {
  key: SettingsPageKey;
  to: SettingsPath;
  icon: LucideIcon;
  /** Any one of these permissions opens the page (the server checks each call). */
  permissions: readonly string[];
}

export interface SettingsGroup {
  key: 'general' | 'data' | 'access' | 'identity' | 'organisation';
  pages: readonly SettingsPageLink[];
}

/** Settings centre navigation (docs/design/settings.md): one admin area, filtered by the user's permissions. */
export const settingsGroups: readonly SettingsGroup[] = [
  {
    key: 'general',
    pages: [
      {
        key: 'regional',
        to: '/settings/regional',
        icon: MapPinned,
        permissions: ['settings.general.manage'],
      },
      { key: 'branding', to: '/settings/branding', icon: Palette, permissions: ['settings.general.manage'] },
      { key: 'systems', to: '/settings/systems', icon: SquareStack, permissions: ['access.system.read'] },
      { key: 'features', to: '/settings/features', icon: Flag, permissions: ['settings.feature.manage'] },
    ],
  },
  {
    key: 'data',
    pages: [
      { key: 'lookups', to: '/settings/lookups', icon: ListChecks, permissions: ['settings.lookup.manage'] },
      { key: 'numbering', to: '/settings/numbering', icon: Hash, permissions: ['settings.numbering.manage'] },
      {
        key: 'translations',
        to: '/settings/translations',
        icon: Languages,
        permissions: ['settings.translation.manage'],
      },
    ],
  },
  {
    key: 'access',
    pages: [
      { key: 'roles', to: '/settings/access/roles', icon: ShieldHalf, permissions: ['access.role.read'] },
      {
        key: 'assignments',
        to: '/settings/access/assignments',
        icon: UserCheck,
        permissions: ['access.assignment.read'],
      },
      {
        key: 'systemAccess',
        to: '/settings/access/system-access',
        icon: KeyRound,
        permissions: ['access.assignment.read'],
      },
      {
        key: 'transfer',
        to: '/settings/access/transfer',
        icon: Repeat,
        permissions: ['access.assignment.manage'],
      },
      {
        key: 'review',
        to: '/settings/access/review',
        icon: FileSpreadsheet,
        permissions: ['access.review.export'],
      },
    ],
  },
  {
    key: 'identity',
    pages: [
      { key: 'users', to: '/admin/users', icon: Users, permissions: ['identity.user.manage'] },
      { key: 'providers', to: '/admin/providers', icon: KeyRound, permissions: ['identity.provider.manage'] },
      {
        key: 'security',
        to: '/admin/security',
        icon: ShieldCheck,
        permissions: ['identity.security.manage'],
      },
      { key: 'email', to: '/admin/email', icon: Mail, permissions: ['identity.smtp.manage'] },
    ],
  },
  {
    key: 'organisation',
    pages: [
      { key: 'levels', to: '/org/levels', icon: Layers, permissions: ['org.unit.manage'] },
      { key: 'import', to: '/org/import', icon: Building, permissions: ['org.import.manage'] },
    ],
  },
];
