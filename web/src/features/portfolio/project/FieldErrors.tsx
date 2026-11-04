import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { StatusBanner } from '@/ui';

type ProblemErrors = { field: string; messageKey: string; params?: Record<string, unknown> | null }[];

/** A failed command with every field / gate message the server returned (keys translated in AR / EN). */
export function FieldErrors({ error }: { error: unknown }) {
  const { t: tc } = useTranslation();
  const problem = (error as { problem?: { errors?: ProblemErrors } }).problem;
  if (!problem?.errors?.length) return <StatusBanner tone="danger" title={errorMessage(tc, error)} />;
  return (
    <StatusBanner
      tone="danger"
      title={errorMessage(tc, error)}
      description={
        <ul className="list-disc ps-5" data-testid="field-errors">
          {problem.errors.map((e, i) => (
            <li key={i}>{translateMessageKey(tc, e.messageKey, e.params)}</li>
          ))}
        </ul>
      }
    />
  );
}
