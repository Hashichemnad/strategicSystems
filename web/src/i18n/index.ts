import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import arAccess from './ar/access.json';
import enAccess from './en/access.json';
import arAdmin from './ar/admin.json';
import enAdmin from './en/admin.json';
import arAuth from './ar/auth.json';
import enAuth from './en/auth.json';
import arIdentity from './ar/identity.json';
import enIdentity from './en/identity.json';
import arImport from './ar/import.json';
import enImport from './en/import.json';
import arOrg from './ar/org.json';
import enOrg from './en/org.json';
import arProfile from './ar/profile.json';
import enProfile from './en/profile.json';
import arCommon from './ar/common.json';
import arErrors from './ar/errors.json';
import arSample from './ar/sample.json';
import arSettings from './ar/settings.json';
import arShell from './ar/shell.json';
import arUiKit from './ar/uiKit.json';
import arValidation from './ar/validation.json';
import enCommon from './en/common.json';
import enErrors from './en/errors.json';
import enSample from './en/sample.json';
import enSettings from './en/settings.json';
import enShell from './en/shell.json';
import enUiKit from './en/uiKit.json';
import enValidation from './en/validation.json';
import arAudit from './ar/audit.json';
import enAudit from './en/audit.json';
import arFiles from './ar/files.json';
import enFiles from './en/files.json';
import arNotifications from './ar/notifications.json';
import enNotifications from './en/notifications.json';
import arJobs from './ar/jobs.json';
import enJobs from './en/jobs.json';
import arReporting from './ar/reporting.json';
import enReporting from './en/reporting.json';
import arWorkflow from './ar/workflow.json';
import enWorkflow from './en/workflow.json';
import arExpressions from './ar/expressions.json';
import arAi from './ar/ai.json';
import arMcp from './ar/mcp.json';
import arCalendar from './ar/calendar.json';
import enCalendar from './en/calendar.json';
import enExpressions from './en/expressions.json';
import enAi from './en/ai.json';
import enMcp from './en/mcp.json';
import arTasks from './ar/tasks.json';
import arTeams from './ar/teams.json';
import enTasks from './en/tasks.json';
import enTeams from './en/teams.json';
import arPortfolio from './ar/portfolio.json';
import enPortfolio from './en/portfolio.json';
import arLinks from './ar/links.json';
import enLinks from './en/links.json';
import arKpi from './ar/kpi.json';
import enKpi from './en/kpi.json';
import arRisk from './ar/risk.json';
import enRisk from './en/risk.json';
import arRequests from './ar/requests.json';
import enRequests from './en/requests.json';
import arDocreg from './ar/docreg.json';
import enDocreg from './en/docreg.json';
import arIp from './ar/ip.json';
import enIp from './en/ip.json';
import arAgile from './ar/agile.json';
import enAgile from './en/agile.json';
import arPmStandards from './ar/pmStandards.json';
import enPmStandards from './en/pmStandards.json';
import arPortfolioViews from './ar/portfolioViews.json';
import enPortfolioViews from './en/portfolioViews.json';
import { readPreference } from '@/lib/preferences';

export const languages = ['ar', 'en'] as const;
export type Language = (typeof languages)[number];

export const resources = {
  ar: {
    common: arCommon,
    shell: arShell,
    errors: arErrors,
    validation: arValidation,
    sample: arSample,
    uiKit: arUiKit,
    auth: arAuth,
    profile: arProfile,
    org: arOrg,
    import: arImport,
    identity: arIdentity,
    admin: arAdmin,
    settings: arSettings,
    access: arAccess,
    audit: arAudit,
    files: arFiles,
    notifications: arNotifications,
    jobs: arJobs,
    reporting: arReporting,
    workflow: arWorkflow,
    expressions: arExpressions,
    ai: arAi,
    mcp: arMcp,
    calendar: arCalendar,
    tasks: arTasks,
    teams: arTeams,
    portfolio: arPortfolio,
    agile: arAgile,
    pmStandards: arPmStandards,
    portfolioViews: arPortfolioViews,
    links: arLinks,
    kpi: arKpi,
    risk: arRisk,
    requests: arRequests,
    docreg: arDocreg,
    ip: arIp,
  },
  en: {
    common: enCommon,
    shell: enShell,
    errors: enErrors,
    validation: enValidation,
    sample: enSample,
    uiKit: enUiKit,
    auth: enAuth,
    profile: enProfile,
    org: enOrg,
    import: enImport,
    identity: enIdentity,
    admin: enAdmin,
    settings: enSettings,
    access: enAccess,
    audit: enAudit,
    files: enFiles,
    notifications: enNotifications,
    jobs: enJobs,
    reporting: enReporting,
    workflow: enWorkflow,
    expressions: enExpressions,
    ai: enAi,
    mcp: enMcp,
    calendar: enCalendar,
    tasks: enTasks,
    teams: enTeams,
    portfolio: enPortfolio,
    agile: enAgile,
    pmStandards: enPmStandards,
    portfolioViews: enPortfolioViews,
    links: enLinks,
    kpi: enKpi,
    risk: enRisk,
    requests: enRequests,
    docreg: enDocreg,
    ip: enIp,
  },
} as const;

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (languages as readonly string[]).includes(value);
}

/** Stored choice, else the browser's language, else Arabic. Signed in, the profile's language wins (AppShell). */
export function initialLanguage(): Language {
  const stored = readPreference('language');
  if (isLanguage(stored)) return stored;
  return navigator.language.toLowerCase().startsWith('en') ? 'en' : 'ar';
}

/** Mirrors the active language onto <html lang dir> so the whole layout flips instantly. */
export function applyDocumentLanguage(language: string): void {
  document.documentElement.lang = language;
  document.documentElement.dir = i18n.dir(language);
}

void i18n.use(initReactI18next).init({
  // A copy: runtime translation overrides are merged into i18next's store, and `resources` must stay the bundled text.
  resources: structuredClone(resources),
  lng: initialLanguage(),
  fallbackLng: { ar: ['en'], default: ['en'] },
  supportedLngs: languages,
  defaultNS: 'common',
  ns: Object.keys(resources.en),
  interpolation: { escapeValue: false },
  returnNull: false,
  // Translation overrides from Settings are added at runtime: re-render when resources change.
  react: { bindI18nStore: 'added' },
});

applyDocumentLanguage(i18n.language);
i18n.on('languageChanged', applyDocumentLanguage);

export default i18n;
