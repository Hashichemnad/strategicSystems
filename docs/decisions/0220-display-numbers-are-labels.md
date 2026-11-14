# 0220 — Display numbers of synced records are labels; real database errors in exceptions

- **Status:** Proposed (release 1.0.2) — awaiting owner acceptance
- **Context:** Sandbox testing found two problems.
  - Salesforce `Name` auto-numbers are not unique in the sense this system assumed. Work-plan rows, for example, are numbered per project
    (`1`, `1.1`). A synced number can also equal a number created here. Unique indexes on `number` / `code` / `document_no` made such records
    fail with a generic "write failed".
  - The exception stored no reason, and the database's message reached only the server log.

## Decision
- **Identity** of a synced record is its internal GUID plus the Salesforce Id (external reference). **Display numbers are labels.** The
  unique indexes on display numbers become non-unique indexes in every module. Link indexes such as `RequestId` stay unique.
- **No repeat of a synced label:** a sync-target field declared with `.DisplayNumber(code, property, numberingFormat)` calls
  `INumberingService.ObserveAsync`. When the synced value has the shape of that format's pattern, the counter of its partition is raised to
  at least its sequence, in the write's transaction. Numbers created here continue after the synced ones. A value of another shape changes
  nothing. `NumberingPattern.Parse` reads a number back against its pattern; it is table-tested.
- **Look-ups by number** (search, MCP, imports) tolerate several records with the same number. Imports refuse an ambiguous match.
- **Write failures** keep the database's reason as exception parameters (`sqlState`, `constraint`, `table`, `column`, `message`). The
  PostgreSQL detail line is never stored, because it can quote row values. The reason is shown on the Exceptions tab. Exceptions other than
  database errors raised by writers or hooks fail the record, not the run, so the run always finishes with a status.

## Consequences
Two records can show the same number. The record header, the Salesforce Id badge and the search results tell them apart.
