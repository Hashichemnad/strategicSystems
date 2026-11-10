# 0170 — Themes are design-token sets plus layout slots of one component library

- **Status:** Proposed (Phase 20) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §C1 asks for three themes (Classic, Enterprise, Modern) × light / dark × RTL / LTR that differ in navigation,
  density, record-page layout, list style, card style and typography — "one component library; themes switch tokens and layout slots,
  never duplicate screens". About 300 screens exist (Phases 0–19); duplicating any of them per theme would triple the maintenance and the
  test surface.

## Decision
- `<html data-theme="classic|enterprise|modern">` (+ the existing `dark` class) selects a **token set** in `web/src/styles/index.css`.
  `:root` / `.dark` stay the Phase 0–19 values byte for byte (Classic). New tokens: `--shadow-card`, `--control-h`, `--control-h-sm`,
  `--header-cell-h`, `--cell-py`, `--header`, `--app-accent`, `--font-heading`; Classic's values equal the sizes the components had, so
  Classic renders unchanged. Enterprise shrinks the root font size to 93.75 % (compact density respecting the browser's font setting),
  square corners, light elevation; Modern uses warmer surfaces, larger radius, soft elevation, Noto Kufi Arabic headings in Arabic.
- **Layout slots**: only the shell (`AppShell` → Classic shell or `WorkspaceShell`) and six shared components read the theme through
  `useAppearance()`: `RecordLayout` (stack / highlights + path + two columns + related lists / card layout), `SplitView` + `useListView`
  (table / list + preview), `StageStepper` (circles / path bar / progress track), `TabsList` / `TabsTrigger` (underline / boxed / pills) and
  `PageHeader` (title size). Every other screen follows the tokens only.
- Organisation branding (`--brand-primary`) overrides the primary colour in every theme; per-app accents (`--app-accent`) apply in the
  workspace themes.
- Fonts: IBM Plex Sans / IBM Plex Sans Arabic everywhere (already self-hosted); Noto Kufi Arabic (OFL-1.1, `@fontsource/noto-kufi-arabic`,
  Arabic subset weight 600) for Modern headings. No vendor fonts, icons or names: the Enterprise theme imitates working patterns only.
- Motion: one `motion-panel` entrance (160 ms) in the workspace themes; none under `prefers-reduced-motion` (transitions also cut to 0.01 ms
  there, workspace themes only, so Classic is unchanged).

## Consequences
- A page adopts the richer layouts by using `RecordLayout` / `SplitView`; in Phase 20 the project record page and the project list do.
  All other record pages already get the theme's header, path bar, tabs, cards and density through the shared components; moving them to
  `RecordLayout` is mechanical (listed as a known gap).
- Accessibility is checked per theme (axe in all three themes, both modes); token values were chosen for AA contrast.
