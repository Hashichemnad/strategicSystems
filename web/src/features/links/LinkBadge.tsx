import { Link } from '@tanstack/react-router';
import { Link2, Lock } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { components } from '@/api/schema';
import { pickText } from '@/lib/format';
import { StatusChip, type Tone } from '@/ui';

export type LinkView = components['schemas']['LinkView'];

const tones = new Set<string>(['neutral', 'info', 'success', 'warning', 'danger']);

/**
 * A linked record as the links registry resolved it (ADR 0034): system and type, number, title and the live status badge, with a link —
 * or a neutral "restricted" line when the viewer may not open it (nothing about the record is shown).
 */
export function LinkBadge({ view }: { view: LinkView }) {
  const { t, i18n } = useTranslation('links');
  const typeName = pickText(i18n.language, view.typeNameAr, view.typeNameEn);
  if (view.visibility !== 'visible') {
    return (
      <span className="inline-flex items-center gap-2 text-sm text-muted-foreground" data-testid="link-badge">
        <Lock className="size-4" aria-hidden />
        {typeName && <span>{typeName}</span>}
        <span>{t(view.visibility === 'restricted' ? 'restricted' : 'unavailable')}</span>
      </span>
    );
  }

  const title = pickText(i18n.language, view.titleAr, view.titleEn);
  const status = pickText(i18n.language, view.statusLabelAr, view.statusLabelEn) || view.statusCode;
  const tone = (view.tone && tones.has(view.tone) ? view.tone : 'neutral') as Tone;
  return (
    <span className="inline-flex flex-wrap items-center gap-2 text-sm" data-testid="link-badge">
      <Link2 className="size-4 text-muted-foreground" aria-hidden />
      <span className="text-muted-foreground">{typeName}</span>
      <Link to={view.link as '/'} className="font-medium text-primary hover:underline">
        {[view.number, title].filter(Boolean).join(' · ')}
      </Link>
      {status && <StatusChip tone={tone}>{status}</StatusChip>}
    </span>
  );
}
