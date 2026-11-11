# 0171 — Settings → Appearance and server-side theme / mode resolution

- **Status:** Accepted by the owner (11 Nov 2026), Phase 20
- **Context:** SPEC-ADDENDUM §C1: the administrator enables themes, sets the default theme and mode; "Allow users to choose their theme"
  is **off by default**; when on, users pick among the enabled themes (and mode). Before Phase 20 every user could already choose light /
  dark / system (Phase 0); the addendum does not say whether that choice may be taken away.

## Decision
- Typed settings area `appearance` (`AppearanceSettings`, one versioned JSON document like Branding, audited as a settings change):
  `enabledThemes` (default all three), `defaultTheme` (default `classic`), `defaultMode` (default `system`), `allowUserTheme` (default
  **false**), `allowUserMode` (default **true** — today's behaviour kept; an organisation can turn it off), `landingRules`, `apps`.
  `GET /api/v1/settings/appearance` for any signed-in user (the shell needs the app overrides and landing rules — nothing sensitive),
  `PUT` with `settings.general.manage` (the same permission as Branding; no new permission), 409 on a stale version.
- **Resolution in one place, on the server**: `AppearanceResolver.Resolve(settings, user.UiTheme, user.Theme)` —
  theme = the user's choice only while choosing is on and that theme is enabled, else the default; mode = the user's choice only while
  mode choice is on, else the default mode. `GET /api/v1/identity/me` returns the effective `appearance.theme` / `appearance.mode`; the
  SPA applies them and never re-implements the rule.
- `PUT /api/v1/identity/me/preferences` gains `uiTheme`, `landingApp`, `favouriteApps` (new nullable columns on `identity.users`,
  migration `Appearance`). A theme choice is refused (422 `appearance.errors.themeChoiceOff` / `validation.unknownCode`) while choosing is
  off or the theme is not enabled; a mode choice is refused while `allowUserMode` is off. `""` clears a choice (always allowed). A stored
  choice survives an administrator turning choosing off and applies again when it is turned back on.
- Signed out (sign-in pages, setup wizard): `GET /api/v1/settings/public` carries `defaultTheme` / `defaultMode`; the browser's last
  effective values (localStorage) avoid a flash of another theme on reload; the server's answer always wins once signed in.

## Consequences
- Default installations and upgrades stay on Classic with choosing off: nothing changes for anyone until an administrator decides.
- Wasel vendors keep their own portal (unchanged); the appearance settings do not apply to it.
