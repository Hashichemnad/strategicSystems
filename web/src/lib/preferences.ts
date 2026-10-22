/**
 * Per-browser UI preferences (language, theme). Storage can be unavailable (private mode, blocked site data),
 * so every access is guarded and the app works without it. Phase 1 persists these on the user profile.
 */
const PREFIX = 'strategy.';

export function readPreference(key: string): string | null {
  try {
    return window.localStorage.getItem(PREFIX + key);
  } catch {
    return null;
  }
}

export function writePreference(key: string, value: string): void {
  try {
    window.localStorage.setItem(PREFIX + key, value);
  } catch {
    // Preference simply isn't remembered.
  }
}
