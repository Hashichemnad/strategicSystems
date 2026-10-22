# 0008 — Test runner and front-end tool versions

- **Status:** Accepted (Phase 0)

## Decisions
- **xunit v3 on Microsoft.Testing.Platform.** .NET 10's `dotnet test` requires opting in for MTP-based frameworks
  (`global.json` → `"test": { "runner": "Microsoft.Testing.Platform" }`). Test projects are executables.
  The command is `dotnet test --solution Strategy.slnx` (or `--project <path>`). The VSTest packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) are not used.
- **Front-end tool majors.** Newer major versions exist, but Phase 0 pins the latest release of the major line each
  library's ecosystem supports today: TypeScript 5.9 (typescript-eslint supports < 6.1), ESLint 9, Vite 7, React 19,
  react-day-picker 9, i18next 25. **Vitest 4.1.12+** is required: Vitest ≤ 4.1.10 carries critical advisories (tinypool, @vitest/mocker).
  Upgrade majors deliberately, one at a time, with the quality gates green.
- **npm 11 for dependency changes.** npm 10.9 (bundled with Node 22) crashes resolving Vitest 4's optional peer set
  (`Cannot read properties of null (reading 'edgesOut')`). Use `npx npm@11 install <pkg>` to change dependencies.
  `npm ci` with the committed lockfile works on npm 10 and npm 11.
- **Playwright 1.56.1**, pinned to match the Chromium build pre-installed in the cloud development environment (revision 1194).
  CI installs that browser with `npx playwright install --with-deps chromium`.
