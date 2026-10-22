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
| Microsoft.AspNetCore.OpenApi | MIT | OpenAPI document |
| Microsoft.Extensions.ApiDescription.Server | MIT | build-time OpenAPI generation |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | MIT | readiness checks |
| xunit.v3 | Apache-2.0 | tests |
| Shouldly | BSD-3-Clause | test assertions |
| NSubstitute | BSD-3-Clause | test doubles |
| Testcontainers.PostgreSql | MIT | integration tests on real PostgreSQL |
| Microsoft.AspNetCore.Mvc.Testing | MIT | in-memory host for API tests |
| NetArchTest.Rules | MIT ¹ | architecture tests |

¹ The NetArchTest.Rules package has no licence metadata in its nuspec; MIT verified from
<https://github.com/BenMorris/NetArchTest/blob/master/LICENSE> (listed in `PackagePolicy.ManuallyVerified`).

Tools: `dotnet-ef` (MIT), local tool in `dotnet-tools.json`.
