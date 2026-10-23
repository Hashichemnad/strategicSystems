import { useTranslation } from 'react-i18next';
import type { components } from '@/api/schema';

type Policy = components['schemas']['PasswordPolicyDto'];

/** The password rules of this installation (Administration → Security), shown before the user types. */
export function PasswordPolicyHint({ policy }: { policy: Policy }) {
  const { t } = useTranslation('auth');
  const rules = [
    t('policy.minLength', { count: policy.minLength }),
    policy.requireUpper && t('policy.upper'),
    policy.requireLower && t('policy.lower'),
    policy.requireDigit && t('policy.digit'),
    policy.requireSymbol && t('policy.symbol'),
    policy.history > 0 && t('policy.history', { count: policy.history }),
  ].filter(Boolean);
  return (
    <div className="rounded-md bg-muted/60 p-3 text-xs text-muted-foreground">
      <p className="mb-1 font-medium text-foreground">{t('policy.title')}</p>
      <ul className="list-disc ps-5">
        {rules.map((rule) => (
          <li key={String(rule)}>{rule}</li>
        ))}
      </ul>
    </div>
  );
}
