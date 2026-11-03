import { RefreshCw, Sparkles } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import { Button, StatusBanner } from '@/ui';
import { AiBadge } from './AiDraft';
import { useAiFeature, useApproverBrief } from './api';

/**
 * Approver brief on the shared approval panel (SPEC §0.5): an AI paragraph and points, plus the changed fields computed from the record's
 * history. Generated on request, never stored, clearly marked as AI-generated; the decision stays with the approver.
 */
export function ApproverBrief({ resourceType, resourceId }: { resourceType: string; resourceId: string }) {
  const { t, i18n } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const enabled = useAiFeature('brief');
  const brief = useApproverBrief();
  if (!enabled) return null;
  const generate = () => brief.mutate({ resourceType, resourceId });
  const data = brief.data;
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border border-info/40 bg-card p-4"
      data-testid="approver-brief"
    >
      <div className="flex flex-wrap items-center gap-2">
        <Sparkles className="size-4 text-info" aria-hidden />
        <h3 className="font-semibold">{t('brief.title')}</h3>
        {data && <AiBadge label={t('generated')} />}
        <Button
          size="sm"
          variant={data ? 'ghost' : 'outline'}
          className="ms-auto"
          loading={brief.isPending}
          onClick={generate}
        >
          {data ? <RefreshCw aria-hidden /> : <Sparkles aria-hidden />}
          {data ? t('brief.regenerate') : t('brief.generate')}
        </Button>
      </div>
      {brief.isError && <StatusBanner tone="danger" title={errorMessage(tc, brief.error)} />}
      {data && (
        <div className="flex flex-col gap-3 text-sm">
          <p className="leading-relaxed" dir="auto">
            {data.summary}
          </p>
          {data.changes.length > 0 && (
            <div>
              <h4 className="font-medium">{t('brief.changes')}</h4>
              <ul className="ms-5 list-disc">
                {data.changes.map((change) => (
                  <li key={change} dir="auto">
                    {change}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {data.attentionPoints.length > 0 && (
            <div>
              <h4 className="font-medium">{t('brief.attention')}</h4>
              <ul className="ms-5 list-disc">
                {data.attentionPoints.map((point) => (
                  <li key={point} dir="auto">
                    {point}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <div>
            <h4 className="font-medium">{t('brief.changedFields')}</h4>
            {data.changedFields.length === 0 ? (
              <p className="text-muted-foreground">{t('brief.noChanges')}</p>
            ) : (
              <table className="mt-1 w-full text-start text-xs">
                <thead>
                  <tr className="text-muted-foreground">
                    <th className="py-1 text-start font-medium">{t('brief.field')}</th>
                    <th className="py-1 text-start font-medium">{t('brief.before')}</th>
                    <th className="py-1 text-start font-medium">{t('brief.after')}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.changedFields.map((change) => (
                    <tr key={change.field} className="border-t">
                      <td className="py-1 pe-2">
                        {i18n.language === 'ar' ? change.labelAr : change.labelEn}
                      </td>
                      <td className="py-1 pe-2 text-muted-foreground line-through" dir="auto">
                        {change.before ?? '—'}
                      </td>
                      <td className="py-1" dir="auto">
                        {change.after ?? '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
          <p className="text-xs text-muted-foreground">
            {t('brief.disclaimer')} · <bdi dir="ltr">{data.model}</bdi> ·{' '}
            {formatDateTime(data.generatedAt, i18n.language)}
          </p>
        </div>
      )}
    </section>
  );
}
