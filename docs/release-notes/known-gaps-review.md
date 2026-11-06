# Known gaps of Phases 0–17 — review for release 1.0 (Phase 18)

Every bullet of the "Known gaps" sections of `docs/phase-summaries/phase-0.md` … `phase-17.md`, with its status at release 1.0.

**Status:** **Closed (P*n*)** — closed in a later phase (evidence cited) · **Closed now** — closed in Phase 18 · **Phase 18 stream** — part of
another Phase 18 work item (installer, wizard, health page, security, accessibility) · **Open** — stays open; the reason and the impact go to the
release notes ("Known limitations") · **By design** — the behaviour is intended (recorded in an ADR); listed so administrators know it.

## Phase 0
| Gap | Status | Evidence / reason |
|---|---|---|
| No sign-in yet; placeholder access | Closed (P1, P2) | Identity (ADR 0009–0014), access service and negative tests (`AuthorizationMatrixTests`) |
| Module switch from configuration; no Settings centre, lookups, numbering, translations; sidebar not filtered | Closed (P2) | Settings → Systems / Lists / Numbering / Translations; sidebar from accessible systems |
| Only `created_*` / `updated_*` stamped; audit trail, files, notifications, Quartz, outbox | Closed (P3) | `Platform.Audit` hash chain (ADR 0018), Files, Notifications, Scheduling |
| Arabic search normalisation not in the sample | Closed (P1+) | `Kernel/Text/SearchText.cs`; generated `*_search` columns with indexes (e.g. `core.employee.name_search`) |
| Idempotency-key header | Closed (P4) | `Workflow/Application/IdempotencyMiddleware.cs`, `Http/Access/IdempotencyEndpointExtensions.cs` |
| Data Protection keys not persisted | Closed (P1) | ADR 0012, `core.data_protection_keys` |
| Forwarded headers; backup / upgrade scripts; automated WCAG audit | Closed (P5) / Phase 18 stream | `Host/Infrastructure/ForwardedHeadersSetup.cs`; scripts and axe audit are Phase 18 items 3 and 8 |
| GitHub Actions not run yet | Closed | runs on every pull request since Phase 1 (`.github/workflows/ci.yml`) |

## Phase 1
| Gap | Status | Evidence / reason |
|---|---|---|
| Roles, scopes, permission catalogue; admin flag | Closed (P2) | ADR 0016 (`DropSystemAdminFlag` migration) |
| Auth events only in `identity.auth_event` | Closed (P3) | moved to the audit trail (`MoveAuthEventsToAudit`, ADR 0018) |
| Photos in `core.employee_photo`; e-mails without outbox | Closed (P3) | photos through the Files service (`EmployeePhotos.Purpose`); notifications outbox with retry |
| Grade is a free code | Closed (P2) | validated against the grade list (`org.errors.unknownGrade`) |
| Vendor principals reserved only; OAuth 2.1 for MCP | Closed (P11, P5) | ADR 0110; OpenIddict (ADR 0032) |

## Phase 2
| Gap | Status | Evidence / reason |
|---|---|---|
| Permission / role / settings changes not in the audit trail | Closed (P3) | audit interceptor + explicit events |
| Bulk transfer does not re-route pending approvals | Closed (P4) | `IRoleTransferHook` implemented by Workflow |
| Logos in `settings.branding_asset` | Closed (P3) | logos are Files-service files (`SettingsAdministration` branding assets) |
| Excel import of list values | Open | the list editor covers every list; bulk import of list values was never needed by a module (Q-P7). Impact: long lists are typed in the editor |
| Translation overrides reach other sessions within 5 minutes | By design | cache refresh interval (P2 design note); the editing session sees them at once |
| Module thresholds registered by modules | Closed (P8–P17) | each module's Settings area |

## Phase 3
| Gap | Status | Evidence / reason |
|---|---|---|
| Audit retention purge not offered (default "keep forever") | Open (by design for 1.0) | ADR 0018: the runtime role cannot delete audit rows; an owner-run archive procedure is a later option. Impact: the audit schema grows; size it in the hardware sizing |
| S3 provider untested; ClamAV only against a fake clamd | **Closed now** | `Files/RealStorageAndScanTests` (real S3 server — SeaweedFS by default, MinIO via `STRATEGY_TEST_S3_IMAGE` — and the real ClamAV daemon with EICAR). It found a defect: the AWS SDK's default `aws-chunked` upload was stored with its chunk framing by SeaweedFS; the S3 store now sends a plain signed payload |
| Approval / workflow notifications, integration events | Closed (P4) | Workflow notifications through the outbox |
| Directory sync reports only | By design | applying HR changes is the customer connector's job (P3 design note) |
| On-behalf-of filled with delegation | Closed (P4) | delegation actions record `on_behalf_of` |

## Phase 4
| Gap | Status | Evidence / reason |
|---|---|---|
| Team roles resolve once Teams registers `ITeamRoleResolver` | Closed (P7) | `Teams/Application/TeamIntegration.cs` |
| Open tasks not re-routed by bulk transfers | By design | ADR 0040: tasks belong to people; *Reassign open tasks of an employee* |
| My Work cards per module | Closed (P17) | `IMyWorkCardSource` in every module (ADR 0149) |
| Preview resolves record roles against no record | By design | a preview has no record; real submissions resolve them |

## Phase 5
| Gap | Status | Evidence / reason |
|---|---|---|
| Modules add their own AI forms, sources and MCP tools | Closed (P6–P17) | each module's `Ai/` and `Mcp/` folders |
| Live check with Claude Desktop / claude.ai / Claude Code over HTTPS | Open (owner acceptance) | needs the customer's HTTPS installation and real accounts; procedure in `docs/mcp-guide.md` |
| No embeddings; names in free text not masked | Open | similarity uses full-text search; masking covers the record's people. Impact: free-text names can reach the AI provider when masking is on — documented in the administrator guide (use a self-hosted model where this matters) |
| `libjbig0` (GPL-2+) licence point of ADR 0033 | Open (owner decision) | OCR packages in the image; build with `INSTALL_OCR=false` to leave them out until the owner decides |
| Setup wizard AI step; health page AI check | Phase 18 stream | items 2 and 3 |

## Phase 6
| Gap | Status | Evidence / reason |
|---|---|---|
| Bulk transfer does not move tasks; no "take over as creator" | By design / Open | ADR 0040. Impact: tasks a leaver *created* keep the leaver as creator / approver until reassigned by hand |
| Modules register linkable types | Closed (P7–P17) | `Links` registrations per module |
| Personal leave calendars not in working-time arithmetic | Open | ADR 0027 / 0037: deadlines use the unit's business hours and holidays only |

## Phase 7
| Gap | Status | Evidence / reason |
|---|---|---|
| `LinkedRecordClosed` from Portfolio | Closed (P9) | `ProjectIntegration` publishes it on closure approval |
| "Close the team when the record closes without evaluation" | Open | teams are closed after evaluation (project teams) or by hand |
| Reward budget informative | By design | Q-T decisions, Phase 7 design note |

## Phase 8 / 9
| Gap | Status | Evidence / reason |
|---|---|---|
| Lifecycle, `LinkedRecordClosed`, project teams, dashboards, exports | Closed (P9, P10, P17) | |
| Strategic objectives from the KPI catalogue | Closed (P9) | ADR 0075 |
| AI assists, Gantt, agile, baselines / EVM, vendor updates, shared risk scoring | Closed (P10, P11, P13) | ADRs 0090–0101, 0110–0117 |
| Costs for team members not configurable | Closed (P10) | `TeamMembersSeeCosts` (ADR 0098) |

## Phase 10
| Gap | Status | Evidence / reason |
|---|---|---|
| Capacity view and quarterly portfolio review | Closed (P11) | ADR 0116 |
| Gantt export uses the system Arabic font | Closed (P11) | product fonts embedded in the export (ADR 0117, `ui/gantt/exportGantt.ts`) |
| With swimlanes on, a board column appears once per lane | Open (UI) | cosmetic; the board works. Impact: repeated column headers with swimlanes |
| Request page Benefits tab has no deep link | Open (UI) | minor navigation limitation |
| `portfolio.sprintReviewed` not sent | Closed (P11) | sent on approve / reject / return of the sprint review (`ProjectIntegration`, ADR 0117) |

## Phase 11
| Gap | Status | Evidence / reason |
|---|---|---|
| UAE Pass for vendors not built | **Closed now** | ADR 0154; `WaselTests.Uae_pass_signs_in_only_an_active_vendor_account_…`; E2E `wasel-uaepass.spec.ts` (screenshots `docs/screenshots/phase-18/*-wasel-uaepass-*`) |
| Vendor comments without attachments / @mentions | Open | vendors upload deliverables separately. Impact: no files on comments |
| Capacity view ignores tasks and sprint stories | Open | allocations come from Teams and the charter; documented in the user guide |

## Phase 12
| Gap | Status | Evidence / reason |
|---|---|---|
| Portfolio / Risk / CAP integrations proven with doubles | Closed (P9, P13, P15) | real subscribers (`KpiResultApproved` → Risk, `KpiTargetMissed` → CAP) |
| Benchmark results on KPI cards | Closed (P15) | `IKpiBenchmarkWriter` (ADR 0128) |
| Employee-level KPIs | By design | out of scope (SPEC §2.7) |
| KPI My Work card from lists | Closed (P17) | `Kpi/Reporting/KpiMyWorkCards.cs` |

## Phase 13
| Gap | Status | Evidence / reason |
|---|---|---|
| Full E2E and Docker integration tests not run in that sandbox | Closed (P17) | 104 / 104 E2E, full integration suite (Phase 17 summary) |
| CAP / Audit consume the Risk contracts | Closed (P15, P16) | |

## Phase 14
| Gap | Status | Evidence / reason |
|---|---|---|
| Demo AI provider does not answer the Requests / DocReg / IP prompts | Open | the demo provider is a development aid; real providers answer. No impact on customers |
| Public links for Public documents are a setting only | Open | ADR 0083: no anonymous endpoint in 1.0. Impact: Public documents are shared as files, not links |
| Form builder edits only the simple stage template | By design | richer stage graphs are code (built-in types) |
| Rich text shown as plain text (no DOMPurify) | Open | the product has no rich-text editor yet; values are plain text, which is safe. DOMPurify is added when formatted HTML is rendered |
| `unitIds` submitter rule has no editor | Open | everyone / roles only in the builder. Impact: unit-restricted request types need the API |
| Benchmarking as a built-in type | Closed (P15) | ADR 0126 |

## Phase 15 / 16
| Gap | Status | Evidence / reason |
|---|---|---|
| Actions from a development plan have no action type | By design | the owner sets it before submitting (server requires it) |
| KPI picker lists the first 200 cards | Open | no search-as-you-type. Impact: installations with > 200 KPI cards pick by the API / list order |
| New visit participants' names appear after saving | Open (UI) | cosmetic |
| Follow-up calendar without drag-and-drop rescheduling | Open | dates are changed in the follow-up form |
| QAIP KPI results are drafts | By design | the KPI owner submits them (AI / integrations draft, people decide) |

## Phase 17
| Gap | Status | Evidence / reason |
|---|---|---|
| Past-period comparison of stock metrics on the live path shows "—" | Open (analysed in Phase 18) | see below |
| Document Registry sensitivity counted only where cleared everywhere | Open (by design) | an under-count, never a leak (ADR 0141) |
| Project risks not in `risk.*` metrics | By design | they are Portfolio records; the Risk dashboard shows them separately |
| KPI cards without weight count as weight 1 in `kpi.score` | By design | additive facts (ADR 0141) |
| Dashboards poll every 5 minutes; no SignalR push | Open | figures are at most ~6 minutes old on an open page |
| `CapApiTests.Due_dates_follow_the_priority_in_working_time` intermittent | **Closed now** | root cause and fix below |
| Scheduled jobs run on their schedules for the first time after the upgrade | Release notes | announce: reminders, digests and escalations of earlier phases start sending. A new test proves every job fires on its schedule (`Scheduling/JobSchedulesFireTests`) |

### CAP due-date test (closed now)
**Root cause:** the test froze the clock at 10 September 2026 *after* creating its people and role assignments "today". Role assignments are valid
from the day they are granted, so on 10 September those people had no roles. The test passed only while the access snapshot built before the
freeze stayed in the cache (the cache's 60-second age check compares with the clock, which had moved backwards). Any access change in the
process — including one in another test host, because `AccessChangeSignal` is process-wide — forced a reload on 10 September without the roles:
403 when creating a remark. Only full parallel runs had such changes, hence the intermittence. Reproduced deterministically by raising the signal
right after the freeze. **Fix:** the test world is created on the test date (`CapWorld.CreateAsync(factory, clock, validFrom)`; the sign-ins
use the real time because authenticator codes do), and the test now raises the signal after the freeze, so it no longer depends on the cache.
Product behaviour was correct and is unchanged.

### Past-period comparison of stock metrics on the live path (stays open)
Own / Team-scope viewers and record-role holders get **live** figures computed by each module on its scoped query (ADR 0141). A stock metric is
the state *now*; its value at the end of an earlier month would have to be rebuilt per record from history. Modules keep current state only; the
audit trail can rebuild one record "as of a date", but doing that for every record in a user's scope, for ~25 stock metrics across 11 modules,
on every dashboard view, is neither cheap nor bounded. The cached path has the comparison because the monthly snapshot freezes unit-level facts —
but those facts are aggregated per unit and cannot be filtered to the records one user may see, so reusing them would leak or mis-state figures.
A correct implementation would store **per-viewer** monthly snapshots of live figures (a job at month end, for the users who use the live path),
which gives comparisons only from the first snapshot onwards. **Impact:** for these viewers the "vs previous period" delta of stock metrics
shows "—"; flow metrics (by record date) and all figures of All / Unit-scope viewers have the comparison. Candidate for a later release.
