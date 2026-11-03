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
| Quartz.Extensions.Hosting | Apache-2.0 | scheduled jobs (Quartz.NET with the PostgreSQL job store; the embedded table script is Quartz's own, Apache-2.0) |
| Quartz.Serialization.SystemTextJson | Apache-2.0 | Quartz job data serialisation |
| AWSSDK.S3 | Apache-2.0 | optional S3-compatible file storage (MinIO, AWS S3) |
| Microsoft.AspNetCore.OpenApi | MIT | OpenAPI document |
| Microsoft.Extensions.ApiDescription.Server | MIT | build-time OpenAPI generation |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | MIT | readiness checks |
| Microsoft.Extensions.AI | MIT | AI abstraction (`IChatClient`), function invocation for "Ask the system" (Phase 5) |
| Microsoft.Extensions.AI.Abstractions | MIT | AI contracts (`IChatClient`, messages, tools) |
| Microsoft.Extensions.AI.OpenAI | MIT | `IChatClient` adapter for OpenAI, Azure OpenAI and OpenAI-compatible servers |
| OpenAI | MIT | OpenAI .NET SDK (OpenAI, Azure OpenAI v1 endpoint, Ollama / vLLM) |
| Anthropic | Apache-2.0 | official Anthropic C# SDK with its `IChatClient` adapter (Claude) |
| ModelContextProtocol.AspNetCore | Apache-2.0 | official MCP C# SDK, Streamable HTTP server at `/mcp` |
| OpenIddict.AspNetCore | Apache-2.0 | built-in OAuth 2.1 authorization server and token validation for MCP clients |
| OpenIddict.EntityFrameworkCore | Apache-2.0 | OAuth clients, authorizations and tokens in PostgreSQL (schema `mcp`) |
| DocumentFormat.OpenXml | MIT | text extraction from Word and PowerPoint documents (document-to-form) |
| PDFtoImage | MIT | renders PDF pages without a text layer to images for OCR (ADR 0033); brings bblanchon.PDFium.* (Apache-2.0 binaries of PDFium, BSD-3-Clause; bundled third-party notices in `src/Host/Strategy.Host/licenses/pdfium/`, shipped in the app's `licenses/` folder) and SkiaSharp + native assets (MIT; notices in `licenses/skiasharp/`) |
| xunit.v3 | Apache-2.0 | tests |
| Shouldly | BSD-3-Clause | test assertions |
| NSubstitute | BSD-3-Clause | test doubles |
| Testcontainers.PostgreSql | MIT | integration tests on real PostgreSQL |
| Testcontainers | MIT | integration tests against Keycloak (OIDC, OAuth, SAML) and OpenLDAP containers, Mailpit and Gotenberg |
| PdfPig | Apache-2.0 | PDF text extraction for document-to-form (Phase 5); in tests, reads rendered PDFs (fonts, Arabic text, layout snapshot) |
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
| @dnd-kit/core (+ @dnd-kit/accessibility, @dnd-kit/utilities) | MIT | Kanban drag and drop (Phase 6) |
| react-hook-form, @hookform/resolvers | MIT | forms |
| zod | MIT | schemas (forms, search params) |
| react-day-picker | MIT | date picker |
| date-fns | MIT | calendar locales for the date picker |
| openapi-fetch | MIT | typed HTTP client |
| @fontsource/ibm-plex-sans-arabic, @fontsource/ibm-plex-sans | OFL-1.1 (fonts) | self-hosted fonts (offline installs); the Arabic and Latin WOFF2 files of IBM Plex Sans Arabic are also embedded in `Strategy.Platform.Reporting` for PDFs |

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

## Operating-system packages in the product image (`deploy/Dockerfile`)

| Package | Licence | Used for |
|---|---|---|
| libldap | OpenLDAP Public License | LDAP / Active Directory sign-in |

### Text recognition (OCR) — installed with `INSTALL_OCR=true` (default), left out with `INSTALL_OCR=false` ([ADR 0033](decisions/0033-ocr-for-scanned-documents.md))

`apt-get install tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng` adds the packages below (libldap2 and the SASL libraries are installed for LDAP sign-in anyway; Ubuntu 24.04 base of
`mcr.microsoft.com/dotnet/aspnet:10.0`; versions as of this build — `/app/licenses/ocr/packages.txt` in the image is authoritative). They are
used only by the separate `tesseract` program; nothing is linked into the product's own code. Each package's Debian copyright file ships in the
image at `/app/licenses/ocr/<package>.copyright` (the image build fails if one is missing). **LGPL** libraries (accepted by the owner, ADR 0033)
and the one **GPL** library are in bold.

| Package | Version | Licence (summary of the Debian copyright file) |
|---|---|---|
| fontconfig | 2.15.0-1.1ubuntu2 | HPND-style (MIT-like) |
| fontconfig-config | 2.15.0-1.1ubuntu2 | HPND-style (MIT-like) |
| fonts-dejavu-core | 2.37-8 | Bitstream Vera / DejaVu font licence (permissive; GPL-2+ only for build scripts) |
| fonts-dejavu-mono | 2.37-8 | Bitstream Vera / DejaVu font licence (permissive; GPL-2+ only for build scripts) |
| libarchive13t64 | 3.7.2-2ubuntu0.9 | BSD-2-Clause (+ Apache-2.0, public-domain parts) |
| libbrotli1 | 1.1.0-2ubuntu0.1 | MIT |
| libbsd0 | 0.12.1-1build1.1 | BSD-2/3-Clause, ISC |
| libcairo2 | 1.18.0-3build1 | **LGPL-2.1** or MPL-1.1 |
| libcurl4t64 | 8.5.0-2ubuntu10.16 | curl (MIT-style); GPL only for build scripts |
| libdatrie1 | 0.2.13-3build1 | **LGPL-2.1+** |
| libdeflate0 | 1.19-1build1.1 | MIT |
| libexpat1 | 2.6.1-2ubuntu0.6 | MIT |
| libfontconfig1 | 2.15.0-1.1ubuntu2 | HPND-style (MIT-like) |
| libfreetype6 | 2.13.2+dfsg-1ubuntu0.2 | FreeType Licence (FTL) or GPL-2 — used under FTL |
| libfribidi0 | 1.0.13-3build1 | **LGPL-2.1+** |
| libgif7 | 5.2.2-1ubuntu1.2 | MIT |
| libglib2.0-0t64 | 2.80.0-6ubuntu3.10 | **LGPL-2.1+** |
| libgomp1 | 14.2.0-4ubuntu2~24.04.1 | GPL-3+ with the GCC Runtime Library Exception |
| libgraphite2-3 | 1.3.14-2ubuntu0.24.04.1 | **LGPL-2.1+** or MPL-1.1 (or GPL-2+) |
| libgssapi-krb5-2 | 1.20.1-6ubuntu2.10 | MIT (Kerberos) |
| libharfbuzz0b | 8.3.0-2build2 | MIT ("Old MIT"); GPL only for build scripts |
| libjbig0 | 2.1-6.1ubuntu2 | **GPL-2+** (JBIG-KIT) — see ADR 0033, owner decision |
| libjpeg-turbo8 | 2.1.5-2ubuntu2 | IJG, BSD-3-Clause, Zlib |
| libjpeg8 | 8c-2ubuntu11 | IJG (Debian transitional package; the code is libjpeg-turbo) |
| libk5crypto3 | 1.20.1-6ubuntu2.10 | MIT (Kerberos) |
| libkeyutils1 | 1.6.3-3build1 | **LGPL-2.1+** (library) |
| libkrb5-3 | 1.20.1-6ubuntu2.10 | MIT (Kerberos) |
| libkrb5support0 | 1.20.1-6ubuntu2.10 | MIT (Kerberos) |
| libldap2 | 2.6.10+dfsg-0ubuntu0.24.04.1 | OpenLDAP Public License |
| liblept5 | 1.82.0-3build4 | BSD-2-Clause (Leptonica) |
| liblerc4 | 4.0.0+ds-4ubuntu2 | Apache-2.0 |
| libnghttp2-14 | 1.59.0-1ubuntu0.4 | MIT |
| libopenjp2-7 | 2.5.0-2ubuntu0.5 | BSD-2-Clause |
| libpango-1.0-0 | 1.52.1+ds-1build1 | **LGPL-2.1+** |
| libpangocairo-1.0-0 | 1.52.1+ds-1build1 | **LGPL-2.1+** |
| libpangoft2-1.0-0 | 1.52.1+ds-1build1 | **LGPL-2.1+** |
| libpixman-1-0 | 0.42.2-1build1 | MIT |
| libpng16-16t64 | 1.6.43-5ubuntu0.7 | libpng licence |
| libpsl5t64 | 0.21.2-1.1build1 | MIT |
| librtmp1 | 2.4+20151223.gitfa8646d.1-2build7 | **LGPL-2.1+** (library) |
| libsasl2-2 | 2.1.28+dfsg1-5ubuntu3.1 | BSD (Cyrus SASL) |
| libsasl2-modules-db | 2.1.28+dfsg1-5ubuntu3.1 | BSD (Cyrus SASL) |
| libsharpyuv0 | 1.3.2-0.4build3 | BSD-3-Clause |
| libssh-4 | 0.10.6-2ubuntu0.5 | **LGPL-2.1+** |
| libtesseract5 | 5.3.4-1build5 | Apache-2.0 |
| libthai-data | 0.1.29-2build1 | **LGPL-2.1+** |
| libthai0 | 0.1.29-2build1 | **LGPL-2.1+** |
| libtiff6 | 4.5.1+git230720-4ubuntu2.5 | libtiff licence (BSD-like) |
| libwebp7 | 1.3.2-0.4build3 | BSD-3-Clause |
| libwebpmux3 | 1.3.2-0.4build3 | BSD-3-Clause |
| libx11-6 | 2:1.8.7-1ubuntu0.1 | MIT / X11 |
| libx11-data | 2:1.8.7-1ubuntu0.1 | MIT / X11 |
| libxau6 | 1:1.0.9-1build6 | MIT / X11 |
| libxcb-render0 | 1.15-1ubuntu2 | MIT / X11 |
| libxcb-shm0 | 1.15-1ubuntu2 | MIT / X11 |
| libxcb1 | 1.15-1ubuntu2 | MIT / X11 |
| libxdmcp6 | 1:1.1.3-0ubuntu6 | MIT / X11 |
| libxext6 | 2:1.3.4-1build2 | MIT / X11 |
| libxml2 | 2.9.14+dfsg-1.3ubuntu3.10 | MIT |
| libxrender1 | 1:0.9.10-1.1build1 | MIT / X11 |
| tesseract-ocr | 5.3.4-1build5 | Apache-2.0 |
| tesseract-ocr-ara | 1:4.1.0-2 | Apache-2.0 |
| tesseract-ocr-eng | 1:4.1.0-2 | Apache-2.0 |
| tesseract-ocr-osd | 1:4.1.0-2 | Apache-2.0 |

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
