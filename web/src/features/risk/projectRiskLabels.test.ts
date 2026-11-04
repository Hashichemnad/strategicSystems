import { describe, expect, it } from 'vitest';
import { projectRiskCategoryLabel } from './projectRiskLabels';

describe('projectRiskCategoryLabel', () => {
  const vendor = { categoryCode: 'vendor', categoryLabelAr: 'المورّد', categoryLabelEn: 'Vendor' };

  it('shows the label in the user language, not the raw code', () => {
    expect(projectRiskCategoryLabel('ar', vendor)).toBe('المورّد');
    expect(projectRiskCategoryLabel('en', vendor)).toBe('Vendor');
  });

  it('falls back to the other language, then to the code', () => {
    expect(projectRiskCategoryLabel('en', { ...vendor, categoryLabelEn: null })).toBe('المورّد');
    expect(projectRiskCategoryLabel('ar', { ...vendor, categoryLabelAr: null, categoryLabelEn: null })).toBe(
      'vendor',
    );
  });

  it('shows a dash when the risk has no category', () => {
    expect(
      projectRiskCategoryLabel('en', { categoryCode: null, categoryLabelAr: null, categoryLabelEn: null }),
    ).toBe('—');
  });
});
