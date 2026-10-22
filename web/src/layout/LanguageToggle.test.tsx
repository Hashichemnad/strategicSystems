import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { LanguageToggle } from './TopBar';

describe('LanguageToggle', () => {
  it('flips language and document direction instantly and remembers the choice', async () => {
    await renderWithProviders(<LanguageToggle />, { language: 'en' });
    expect(document.documentElement).toHaveAttribute('dir', 'ltr');

    await userEvent.click(screen.getByTestId('language-toggle'));
    expect(document.documentElement).toHaveAttribute('dir', 'rtl');
    expect(document.documentElement).toHaveAttribute('lang', 'ar');
    expect(localStorage.getItem('strategy.language')).toBe('ar');
    expect(screen.getByTestId('language-toggle')).toHaveTextContent('English');

    await userEvent.click(screen.getByTestId('language-toggle'));
    expect(document.documentElement).toHaveAttribute('dir', 'ltr');
  });
});
