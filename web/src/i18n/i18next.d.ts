import 'i18next';
import type { resources } from './index';

// Typed translation keys: a missing or misspelled key is a compile error.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'common';
    resources: (typeof resources)['en'];
  }
}
