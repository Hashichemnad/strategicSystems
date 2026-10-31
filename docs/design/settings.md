# Design note — Settings centre (`Strategy.Platform.Settings`)

**Purpose.** SPEC §0.2 calls for configurable lists, rules and thresholds, localisation, branding and a Settings centre. CLAUDE.md §2 rule 2 says nothing organisation-specific goes in code. This service is the configuration backbone every module reads:
- typed settings;
- lookups (picklists);
- numbering formats;
- feature flags;
- branding;
- regional settings;
- translation overrides.

Module enable / disable lives in the systems registry of `Platform.Access` (see `access.md`) because access checks need it. The Settings UI shows both together.

## Data model (schema `settings`)
| Table | Key columns | Notes |
|---|---|---|
| `setting` | `key` (PK, e.g. `regional`, `branding`), `value` (jsonb), `xmin` | One JSON document per area, deserialised into a typed record with defaults (`RegionalSettings`, `BrandingSettings`). Modules add their own areas later (rules and thresholds) |
| `lookup_type` | `code` (unique, e.g. `employee.grade`), `system_code`, `name_ar`, `name_en`, `parent_type_code`, `builtin` | Declared in code by the owning component/module and seeded when missing; admins may add custom types |
| `lookup_value` | `type_code`, `code` (unique per type), `label_ar`, `label_en`, `sort`, `active`, `parent_value_id`, `metadata` (jsonb) | Never hard-deleted; deactivated. Business logic checks codes only |
| `numbering_format` | `key` (unique, e.g. `sample.item`), `pattern`, `name_ar`, `name_en`, `builtin` | Declared in code with a default pattern; pattern editable |
| `numbering_counter` | `format_key`, `partition`, `last_value` | Counter per format and per rendered prefix |
| `feature_flag` | `code` (unique), `system_code`, `enabled`, `name_ar`, `name_en` | Declared in code with a default; value editable |
| `branding_asset` | `kind` (`logo_light` / `logo_dark`), `content_type`, `content`, `updated_at` | PNG / JPEG / WebP by magic number, ≤ 512 KB. Interim until Files (Phase 3), as for employee photos (ADR 0017) |
| `translation_override` | `language`, `namespace`, `key`, `value` | Merged over the bundled AR/EN resources at runtime |

## Typed settings
- `RegionalSettings`:
  - `TimeZone` (IANA, default `Asia/Dubai`);
  - `HijriDisplay` (default **false**, SPEC §0);
  - `ArabicIndicDigits` (default false);
  - `CurrencyCode` (default `AED`);
  - `DateFormat` (`dd/MM/yyyy`).
- `BrandingSettings`:
  - `OrganisationNameAr/En`;
  - `PrimaryColor` (hex, validated);
  - `EmailHeaderAr/En`, `EmailFooterAr/En` (plain text).

  The identity emails already use the header and footer; the Phase 3 notification templates will use them too.

`ISettingsStore.GetAsync<T>()` reads through a memory cache, invalidated on save. `SaveAsync<T>(value, version)` validates the value, checks concurrency and returns 409 on conflict.

## Lookups
- **Contract:** `ILookupService.GetValuesAsync(type, includeInactive)` and `IsActiveCodeAsync(type, code)`.
- **Declared types:** components declare lookup types with `services.AddLookupType(new LookupTypeDefinition("employee.grade", "org", "الدرجة الوظيفية", "Grade", seedValues))`.
- **Employee grade (ADR 0014 item closed):**
  - Org validates that a new or changed grade is an active code of `employee.grade` (`org.employee.unknownGrade`).
  - Existing free-text grades are added as lookup values once at start-up, with the code as the label.
  - The employee form uses a dropdown.
- **Parent value:** dependent dropdowns (e.g. a sub-category under a category) must point to a value of the type's `parent_type_code`.
- **Excel import:** the import template per type is part of Q-P7 and arrives with the first module that needs bulk lists. Phase 2 provides the editor.

## Numbering (`INumberingService`)
- **Pattern tokens:**

  | Token | Meaning |
  |---|---|
  | `{YYYY}`, `{YY}`, `{MM}` | date parts |
  | `{UNIT}` | the code passed by the caller |
  | `{SEQ:n}` | sequence padded to n digits |
  | other text | taken literally |

  `{SEQ}` is required and must be last among the tokens. Characters are limited to `A–Z a–z 0–9 - _ / .`.
- **Partition:** the rendered prefix (the pattern without the sequence). Numbers therefore restart per year, unit, etc., exactly as the pattern implies. Changing the padding keeps the counter. Changing the prefix starts a new sequence, which cannot collide with older numbers.
- **`NextAsync(DbContext caller, key, context)`** runs `INSERT … ON CONFLICT … DO UPDATE … RETURNING` **on the caller's connection and transaction**. The number is generated in the same transaction as the record (CLAUDE.md §6). The counter is infrastructure, not a business table.
- **Uniqueness:** the owning module keeps a unique index on its number column.
- **Preview:** `POST /settings/numbering/{key}/preview` renders a pattern for today without consuming a number.
- **First consumer:** Sample items (`SMP-{YYYY}-{SEQ:4}`). Editing the pattern in the UI changes the next item's number without a redeploy.

## Feature flags (`IFeatureFlags`)
- **Contract:** `IsEnabledAsync(code)`, cached.
- **Phase 2 flag:** `identity.access-tokens` (default on). When off, users cannot create personal access tokens and the profile hides the tab. Later modules add their switchable features (e.g. PMS EVM, AI features).

## Translation overrides
- **Read:** `GET /settings/translations` (public: the sign-in page is translated too) returns `{ ar: { ns: { key: value } }, en: … }` and a version.
- **Edit:** `PUT /settings/translations` (`settings.translation.manage`) sets or clears one key in one language.
- **Web:** loads the overrides at start-up and after every change with `i18n.addResourceBundle(lang, ns, overrides, deep, overwrite)`. A change shows at once in the editing session, in both languages. Other sessions pick it up on their next load or within 5 minutes.
- **Editor:** lists every bundled key (namespace, key, AR default, EN default), filtered by search, with the override fields.

## Public client configuration
`GET /settings/public` (public) returns the branding names and colour, whether logos exist, the regional settings and the overrides version. The SPA uses it for the sign-in page, number and date formatting, and the shell logo. Logos are served by `GET /settings/branding/logo/{kind}` (public, cached).

## Endpoints (`/api/v1/settings`) and permissions
| Endpoint | Permission |
|---|---|
| `GET /public`, `GET /translations`, `GET /branding/logo/{kind}` | public |
| `GET/PUT /regional`, `GET/PUT /branding`, `PUT/DELETE /branding/logo/{kind}` | `settings.general.manage` |
| `GET /lookups/types`, `GET /lookups/{type}/values` | signed in (dropdowns everywhere) |
| `POST /lookups/types`, `POST /lookups/{type}/values`, `PUT /lookups/values/{id}` | `settings.lookup.manage` |
| `GET /numbering`, `PUT /numbering/{key}`, `POST /numbering/{key}/preview` | `settings.numbering.manage` |
| `GET /features`, `PUT /features/{code}` | `settings.feature.manage` |
| `PUT /translations` | `settings.translation.manage` |

The seeded role `settings.administrator` holds all of these (`requires_mfa`).

## Settings centre UI (`/settings`)
A hub page with cards. A secondary navigation is shown on every settings page and filtered by permission.

| Group | Pages |
|---|---|
| General | Regional · Branding · Systems (enable, owning unit) · Features |
| Data | Lists (lookups) · Numbering · Translations |
| Access | Roles & permissions (matrix per system) · Assignments · System access · Bulk transfer · Access review |
| Users & sign-in | Users · Identity providers · Security · Email server (the Phase 1 pages, now listed here) |
| Organisation | Levels · Import (the Phase 1 pages) |
