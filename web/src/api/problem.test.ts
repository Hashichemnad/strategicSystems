import { describe, expect, it, vi } from 'vitest';
import i18n from '@/i18n';
import { ApiError } from './client';
import { applyFieldErrors, errorMessage, translateFieldMessage, translateMessageKey } from './problem';

describe('server message keys', () => {
  it('translates namespaced keys with params in both languages', async () => {
    expect(translateMessageKey(i18n.t, 'validation.maximumLength', { maxLength: 200 })).toBe(
      'Use at most 200 characters.',
    );
    await i18n.changeLanguage('ar');
    expect(translateMessageKey(i18n.t, 'sample.errors.inactive')).toBe(
      'لا يمكن تعديل البنود غير النشطة. فعّل البند أولًا.',
    );
  });

  it('falls back to a generic message for an unknown key', () => {
    expect(translateMessageKey(i18n.t, 'nope.unknownKey')).toContain('unexpected error');
  });

  it('turns API errors into sentences with the support reference', () => {
    const error = new ApiError(500, {
      status: 500,
      code: 'unexpected',
      messageKey: 'errors.unexpected',
      correlationId: 'abc123',
    });
    expect(errorMessage(i18n.t, error)).toContain('abc123');
    expect(errorMessage(i18n.t, new TypeError('Failed to fetch'))).toBe(i18n.t('errors:network'));
  });

  it('puts field errors on known form fields only', () => {
    const setError = vi.fn();
    const error = new ApiError(400, {
      status: 400,
      code: 'validation.failed',
      messageKey: 'errors.validationFailed',
      errors: [
        {
          field: 'nameEn',
          code: 'MaximumLengthValidator',
          messageKey: 'validation.maximumLength',
          params: { maxLength: 200 },
        },
        { field: 'secret', code: 'x', messageKey: 'validation.invalid' },
      ],
    });

    expect(applyFieldErrors(error, setError, ['nameEn'])).toBe(true);
    expect(setError).toHaveBeenCalledOnce();
    const message = (setError.mock.calls[0]?.[1] as { message: string }).message;
    expect(translateFieldMessage(i18n.t, message)).toBe('Use at most 200 characters.');
  });
});
