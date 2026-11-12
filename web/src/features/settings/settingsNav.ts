import {
  ClipboardList,
  FileSearch,
  Bell,
  BriefcaseBusiness,
  Bot,
  Cable,
  DatabaseZap,
  Building,
  CalendarClock,
  CalendarOff,
  ClipboardCheck,
  FileSpreadsheet,
  FileStack,
  Network,
  Flag,
  FormInput,
  Gauge,
  Hash,
  HeartPulse,
  KeyRound,
  Languages,
  Layers,
  Library,
  Lightbulb,
  LayoutDashboard,
  ChartColumn,
  ListChecks,
  ListTodo,
  Rocket,
  Mail,
  MapPinned,
  Palette,
  Paperclip,
  Presentation,
  Repeat,
  Scale,
  UserRoundX,
  UsersRound,
  ShieldAlert,
  ShieldCheck,
  ShieldHalf,
  SquareStack,
  SwatchBook,
  Timer,
  UserCheck,
  UserCog,
  Users,
  type LucideIcon,
} from 'lucide-react';
import { membersPermissions } from '@/features/access/members';

/** Systems with a Responsibilities page (ADR 0168): `<system>.responsibilities.manage` opens it for that system only. */
export const responsibilitySystems = [
  'kpi',
  'risk',
  'portfolio',
  'cap',
  'auditmgmt',
  'bench',
  'teams',
  'tasks',
] as const;

export const responsibilityPermissions = responsibilitySystems.map(
  (system) => `${system}.responsibilities.manage`,
);

export type SettingsPageKey =
  | 'setup'
  | 'teams'
  | 'portfolio'
  | 'kpi'
  | 'risk'
  | 'requestTypes'
  | 'docreg'
  | 'ip'
  | 'cap'
  | 'bench'
  | 'auditmgmt'
  | 'regional'
  | 'dashboards'
  | 'reporting'
  | 'reportTypes'
  | 'dossierTemplates'
  | 'branding'
  | 'appearance'
  | 'systems'
  | 'features'
  | 'lookups'
  | 'numbering'
  | 'translations'
  | 'notifications'
  | 'files'
  | 'jobs'
  | 'health'
  | 'demoPack'
  | 'approvals'
  | 'ai'
  | 'mcp'
  | 'sourceSystems'
  | 'integrationClients'
  | 'webhookDeliveries'
  | 'developerPortal'
  | 'tasks'
  | 'tasksReassign'
  | 'businessHours'
  | 'holidays'
  | 'members'
  | 'roles'
  | 'assignments'
  | 'systemAccess'
  | 'transfer'
  | 'review'
  | 'responsibilities'
  | 'users'
  | 'providers'
  | 'security'
  | 'email'
  | 'levels'
  | 'import';

export type SettingsPath =
  | '/setup/configure'
  | '/settings/regional'
  | '/settings/integrations/source-systems'
  | '/settings/integrations/clients'
  | '/settings/integrations/deliveries'
  | '/settings/integrations/developer'
  | '/settings/dashboards'
  | '/settings/reporting'
  | '/settings/report-types'
  | '/settings/dossier-templates'
  | '/settings/branding'
  | '/settings/appearance'
  | '/settings/systems'
  | '/settings/features'
  | '/settings/lookups'
  | '/settings/numbering'
  | '/settings/translations'
  | '/settings/notifications'
  | '/settings/files'
  | '/settings/jobs'
  | '/settings/health'
  | '/settings/demo-data'
  | '/settings/approvals'
  | '/settings/ai'
  | '/settings/mcp'
  | '/settings/tasks'
  | '/settings/teams'
  | '/settings/portfolio'
  | '/settings/kpi'
  | '/settings/risk'
  | '/settings/request-types'
  | '/settings/docreg'
  | '/settings/ip'
  | '/settings/cap'
  | '/settings/bench'
  | '/settings/audit-management'
  | '/settings/tasks/reassign'
  | '/settings/business-hours'
  | '/settings/holidays'
  | '/settings/members'
  | '/settings/access/roles'
  | '/settings/access/assignments'
  | '/settings/access/system-access'
  | '/settings/access/transfer'
  | '/settings/access/review'
  | '/settings/responsibilities'
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
  /** Active only on this exact path (a sibling page lives below it). */
  exact?: boolean;
}

export interface SettingsGroup {
  key: 'general' | 'data' | 'platform' | 'integrations' | 'access' | 'identity' | 'organisation';
  pages: readonly SettingsPageLink[];
}

/** Settings centre navigation (docs/design/settings.md): one admin area, filtered by the user's permissions. */
export const settingsGroups: readonly SettingsGroup[] = [
  {
    key: 'general',
    pages: [
      { key: 'setup', to: '/setup/configure', icon: Rocket, permissions: ['settings.setup.manage'] },
      {
        key: 'regional',
        to: '/settings/regional',
        icon: MapPinned,
        permissions: ['settings.general.manage'],
      },
      { key: 'branding', to: '/settings/branding', icon: Palette, permissions: ['settings.general.manage'] },
      {
        key: 'appearance',
        to: '/settings/appearance',
        icon: SwatchBook,
        permissions: ['settings.general.manage'],
      },
      {
        key: 'dashboards',
        to: '/settings/dashboards',
        icon: LayoutDashboard,
        permissions: ['reporting.dashboard.manage', 'reporting.dashboard.build'],
      },
      {
        key: 'reporting',
        to: '/settings/reporting',
        icon: ChartColumn,
        permissions: ['reporting.settings.manage', 'reporting.aggregate.refresh'],
      },
      {
        key: 'reportTypes',
        to: '/settings/report-types',
        icon: Network,
        permissions: ['reporting.report-type.manage'],
      },
      {
        key: 'dossierTemplates',
        to: '/settings/dossier-templates',
        icon: FileStack,
        permissions: ['reporting.dossier-template.manage'],
      },
      { key: 'systems', to: '/settings/systems', icon: SquareStack, permissions: ['access.system.read'] },
      { key: 'features', to: '/settings/features', icon: Flag, permissions: ['settings.feature.manage'] },
      {
        key: 'businessHours',
        to: '/settings/business-hours',
        icon: CalendarClock,
        permissions: ['calendar.business-hours.read'],
      },
      {
        key: 'holidays',
        to: '/settings/holidays',
        icon: CalendarOff,
        permissions: ['calendar.business-hours.read'],
      },
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
    key: 'platform',
    pages: [
      {
        key: 'notifications',
        to: '/settings/notifications',
        icon: Bell,
        permissions: ['settings.notification.manage'],
      },
      { key: 'files', to: '/settings/files', icon: Paperclip, permissions: ['settings.file.manage'] },
      { key: 'jobs', to: '/settings/jobs', icon: Timer, permissions: ['settings.job.view'] },
      { key: 'health', to: '/settings/health', icon: HeartPulse, permissions: ['settings.health.view'] },
      {
        key: 'demoPack',
        to: '/settings/demo-data',
        icon: Presentation,
        permissions: ['settings.setup.manage'],
      },
      {
        key: 'approvals',
        to: '/settings/approvals',
        icon: ClipboardCheck,
        permissions: ['workflow.process.read'],
      },
      {
        key: 'tasks',
        to: '/settings/tasks',
        icon: ListTodo,
        permissions: ['tasks.settings.manage'],
        exact: true,
      },
      {
        key: 'tasksReassign',
        to: '/settings/tasks/reassign',
        icon: UserRoundX,
        permissions: ['tasks.reassignment.manage'],
      },
      { key: 'teams', to: '/settings/teams', icon: UsersRound, permissions: ['teams.settings.manage'] },
      {
        key: 'portfolio',
        to: '/settings/portfolio',
        icon: BriefcaseBusiness,
        permissions: ['portfolio.settings.manage'],
      },
      {
        key: 'kpi',
        to: '/settings/kpi',
        icon: Gauge,
        permissions: ['kpi.settings.manage', 'kpi.strategy.manage', 'kpi.plan.manage'],
      },
      { key: 'risk', to: '/settings/risk', icon: ShieldAlert, permissions: ['risk.framework.manage'] },
      {
        key: 'requestTypes',
        to: '/settings/request-types',
        icon: FormInput,
        permissions: ['requests.type.manage'],
      },
      { key: 'docreg', to: '/settings/docreg', icon: Library, permissions: ['docreg.settings.manage'] },
      { key: 'ip', to: '/settings/ip', icon: Lightbulb, permissions: ['ip.settings.manage'] },
      { key: 'cap', to: '/settings/cap', icon: ClipboardList, permissions: ['cap.settings.manage'] },
      { key: 'bench', to: '/settings/bench', icon: Scale, permissions: ['bench.settings.manage'] },
      {
        key: 'auditmgmt',
        to: '/settings/audit-management',
        icon: FileSearch,
        permissions: ['auditmgmt.settings.manage'],
      },
    ],
  },
  {
    key: 'integrations',
    pages: [
      { key: 'ai', to: '/settings/ai', icon: Bot, permissions: ['ai.settings.manage'] },
      { key: 'mcp', to: '/settings/mcp', icon: Cable, permissions: ['mcp.settings.manage'] },
      {
        key: 'sourceSystems',
        to: '/settings/integrations/source-systems',
        icon: DatabaseZap,
        permissions: ['integration.source-system.manage'],
      },
      {
        key: 'integrationClients',
        to: '/settings/integrations/clients',
        icon: KeyRound,
        permissions: ['integration.client.manage'],
      },
      {
        key: 'webhookDeliveries',
        to: '/settings/integrations/deliveries',
        icon: Repeat,
        permissions: ['integration.webhook.manage'],
      },
      {
        key: 'developerPortal',
        to: '/settings/integrations/developer',
        icon: FileSpreadsheet,
        permissions: ['integration.portal.view'],
      },
    ],
  },
  {
    key: 'access',
    pages: [
      { key: 'members', to: '/settings/members', icon: UsersRound, permissions: membersPermissions },
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
      {
        key: 'responsibilities',
        to: '/settings/responsibilities',
        icon: UserCog,
        permissions: responsibilityPermissions,
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
