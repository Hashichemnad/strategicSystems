# Third-party licences

The product is sold, so every dependency must use an allowed licence (CLAUDE.md §4.3):
MIT, Apache-2.0, BSD, ISC, MPL-2.0, Unlicense, OFL (fonts), plus the OSI-approved PostgreSQL licence (Npgsql).
This file lists **direct** dependencies. Transitive dependencies are checked automatically:

- .NET: `tests/Strategy.ArchitectureTests` reads every `project.assets.json` and the nuspec licence of every resolved package, and fails on a banned package or a licence outside the allow-list.
- npm: `web/scripts/check-licenses.mjs` (run by `npm run lint:licenses` in CI) does the same over `web/package-lock.json`.

Update this file in the same commit that adds a dependency.

## .NET (NuGet) — `Directory.Packages.props`

| Package | Licence | Used for |
|---|---|---|
| Microsoft.EntityFrameworkCore.Relational | MIT | ORM |
| Microsoft.EntityFrameworkCore.Design | MIT | migrations tooling (build-time only) |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL | EF Core provider for PostgreSQL |
| Npgsql.OpenTelemetry | PostgreSQL | database tracing |
| EFCore.NamingConventions | Apache-2.0 | snake_case naming |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | kernel DI contracts |
| Microsoft.Extensions.Logging.Abstractions | MIT | kernel logging contracts |
| FluentValidation | Apache-2.0 | command / query validation |
| Serilog.AspNetCore | Apache-2.0 | structured logging |
| Serilog.Sinks.Console | Apache-2.0 | console log sink |
| Serilog.Settings.Configuration | Apache-2.0 | logging configuration |
| OpenTelemetry.Extensions.Hosting | Apache-2.0 | traces / metrics |
| OpenTelemetry.Instrumentation.AspNetCore | Apache-2.0 | HTTP server instrumentation |
| OpenTelemetry.Instrumentation.Http | Apache-2.0 | HTTP client instrumentation |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | Apache-2.0 | OTLP export (optional) |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | MIT | local accounts, lockout, MFA tokens |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | MIT | OpenID Connect sign-in (Entra ID, ADFS, UAE Pass) |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | MIT | Data Protection key ring in PostgreSQL |
| Sustainsys.Saml2.AspNetCore2 | MIT | SAML 2.0 sign-in |
| System.DirectoryServices.Protocols | MIT | LDAP / Active Directory bind (needs `libldap` on Linux) |
| MailKit | MIT | SMTP email |
| System.Security.Cryptography.Pkcs | MIT | pinned patched version of a transitive dependency (MimeKit) |
| Microsoft.IdentityModel.Tokens.Saml | MIT | current release in place of Sustainsys.Saml2's 5.2.4 minimum, whose netstandard1.x chain (NETStandard.Library 1.6.1, Microsoft.NETCore.Platforms 1.1.0) carries the non-OSI .NET Library licence |
| System.Configuration.ConfigurationManager | MIT | current release in place of Sustainsys.Saml2's 4.4.1 minimum (no SPDX metadata) |
| ClosedXML | MIT | Excel import templates and reports |
| Microsoft.AspNetCore.OpenApi | MIT | OpenAPI document |
| Microsoft.Extensions.ApiDescription.Server | MIT | build-time OpenAPI generation |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | MIT | readiness checks |
| xunit.v3 | Apache-2.0 | tests |
| Shouldly | BSD-3-Clause | test assertions |
| NSubstitute | BSD-3-Clause | test doubles |
| Testcontainers.PostgreSql | MIT | integration tests on real PostgreSQL |
| Testcontainers | MIT | integration tests against Keycloak (OIDC, OAuth, SAML) and OpenLDAP containers |
| Microsoft.AspNetCore.Mvc.Testing | MIT | in-memory host for API tests |
| NetArchTest.Rules | MIT ¹ | architecture tests |

¹ The NetArchTest.Rules package has no licence metadata in its nuspec; MIT verified from
<https://github.com/BenMorris/NetArchTest/blob/master/LICENSE> (listed in `PackagePolicy.ManuallyVerified`).

Tools: `dotnet-ef` (MIT), local tool in `dotnet-tools.json`.

## npm — `web/package.json`

### Runtime (shipped in the SPA bundle)

| Package | Licence | Used for |
|---|---|---|
| react, react-dom | MIT | UI |
| @tanstack/react-query | MIT | server state |
| @tanstack/react-router | MIT | routing, typed search params |
| i18next, react-i18next | MIT | translations |
| @radix-ui/react-direction, -dialog, -dropdown-menu, -label, -popover, -select, -slot, -tabs, -toast | MIT | accessible primitives |
| class-variance-authority | Apache-2.0 | component variants |
| clsx, tailwind-merge | MIT | class names |
| lucide-react | ISC | icons |
| react-hook-form, @hookform/resolvers | MIT | forms |
| zod | MIT | schemas (forms, search params) |
| react-day-picker | MIT | date picker |
| date-fns | MIT | calendar locales for the date picker |
| openapi-fetch | MIT | typed HTTP client |
| @fontsource/ibm-plex-sans-arabic, @fontsource/ibm-plex-sans | OFL-1.1 (fonts) | self-hosted fonts (offline installs) |

### Development and build only (not shipped)

| Package | Licence |
|---|---|
| vite, @vitejs/plugin-react, @tailwindcss/vite, tailwindcss | MIT |
| typescript | Apache-2.0 |
| eslint, @eslint/js, typescript-eslint, eslint-plugin-react-hooks, eslint-plugin-jsx-a11y, globals | MIT |
| vitest, jsdom, @testing-library/react, @testing-library/user-event, @testing-library/jest-dom | MIT |
| @playwright/test | Apache-2.0 |
| openapi-typescript | MIT |
| prettier | MIT |
| @types/react, @types/react-dom, @types/node | MIT |

Transitive dev-only packages under BlueOak-1.0.0, Python-2.0, CC-BY-4.0 and CC0-1.0 are accepted for tooling only,
as accepted by the owner in [ADR 0007](decisions/0007-licence-notes.md).

## Container images (docker-compose, not bundled into the product image)

| Image | Licence | Profile |
|---|---|---|
| postgres | PostgreSQL | default |
| gotenberg/gotenberg | MIT | default |
| axllent/mailpit | MIT | `dev` only |
| getmeili/meilisearch | MIT | `search` (optional) |
| clamav/clamav | GPL-2.0 — separate optional service, see [ADR 0007](decisions/0007-licence-notes.md) | `antivirus` (optional) |
| ollama/ollama | MIT | `ai` (optional) |
| mcr.microsoft.com/dotnet/aspnet (base of the product image) | MIT | — |
