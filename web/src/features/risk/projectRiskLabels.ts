import { pickText } from '@/lib/format';

/** A project risk's category as the server labelled it from Portfolio's lookup: the user's language, then the other, then the code. */
export function projectRiskCategoryLabel(
  language: string,
  row: { categoryCode: string | null; categoryLabelAr: string | null; categoryLabelEn: string | null },
): string {
  if (!row.categoryCode) return '—';
  return pickText(language, row.categoryLabelAr, row.categoryLabelEn) || row.categoryCode;
}
