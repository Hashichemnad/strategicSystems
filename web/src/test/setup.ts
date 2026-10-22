import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';
import i18n from '@/i18n';

// jsdom lacks these browser APIs used by Radix and the theme provider.
function polyfill(target: object, key: string, value: unknown): void {
  if (!(key in target)) Object.defineProperty(target, key, { value, writable: true, configurable: true });
}

polyfill(
  globalThis,
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  },
);
polyfill(window, 'matchMedia', (query: string) => ({
  matches: false,
  media: query,
  onchange: null,
  addEventListener: () => undefined,
  removeEventListener: () => undefined,
  addListener: () => undefined,
  removeListener: () => undefined,
  dispatchEvent: () => false,
}));
polyfill(Element.prototype, 'scrollIntoView', () => undefined);
polyfill(Element.prototype, 'hasPointerCapture', () => false);
polyfill(Element.prototype, 'releasePointerCapture', () => undefined);

beforeEach(async () => {
  await i18n.changeLanguage('en');
});

afterEach(() => {
  cleanup();
});
