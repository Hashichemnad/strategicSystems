import { useTranslation } from 'react-i18next';

/** "Skip to content": the first Tab stop of every shell (WCAG 2.4.1). */
export function SkipLink() {
  const { t } = useTranslation('shell');
  return (
    <a
      href="#main"
      className="sr-only focus:not-sr-only focus:fixed focus:start-2 focus:top-2 focus:z-[70] focus:rounded-md focus:bg-card focus:px-3 focus:py-2 focus:shadow"
    >
      {t('skipToContent')}
    </a>
  );
}
