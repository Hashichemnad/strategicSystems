import { DirectionProvider } from '@radix-ui/react-direction';
import { render, type RenderOptions } from '@testing-library/react';
import type { ReactElement, ReactNode } from 'react';
import i18n from '@/i18n';
import { ToastProvider } from '@/ui';

function Providers({ children }: { children: ReactNode }) {
  return (
    <DirectionProvider dir={i18n.dir()}>
      <ToastProvider>{children}</ToastProvider>
    </DirectionProvider>
  );
}

/** Renders with the app's providers in the given language (default English). */
export async function renderWithProviders(
  ui: ReactElement,
  { language = 'en', ...options }: RenderOptions & { language?: 'ar' | 'en' } = {},
) {
  await i18n.changeLanguage(language);
  return render(ui, { wrapper: Providers, ...options });
}
