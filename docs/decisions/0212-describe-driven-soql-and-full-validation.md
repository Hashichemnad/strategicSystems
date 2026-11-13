# 0212 — Queries select only fields the object has; Validate checks every queried field; picklist → number

- **Status:** Proposed (release 1.0.1) — awaiting owner acceptance
- **Context:** Against a real sandbox the generated SOQL failed with `INVALID_FIELD`: `User` has no `IsDeleted` (Salesforce users are
  deactivated, never deleted) and `Contact` has no `EmployeeNumber` / `FederationIdentifier`, which `PeopleOptions` gave every people mapping
  by default. *Validate* checked only the mapped fields, so neither was caught before a run. The fake org of the tests derived each describe
  from the mapping pack, so every queried field "existed". Separately, `PMS_Project_Risk__c.Probability_Level__c` and `Impact_Level__c` are
  picklists (`1`–`5`) mapped "direct" to whole-number fields: *Validate* refused them ("does not fit") and picklist mappings could only
  produce text.

## Decision
- **One list of queried fields** (`SyncQueries`), used by the engine and by *Validate*: for record and people mappings `Id`, `IsDeleted`
  (optional), the watermark, mapped fields, external-ID template fields and the people fields (optional at run time); fixed lists for files
  (`ContentVersion`, `ContentDocumentLink`), attachments, approvals (`ProcessInstance`, `ProcessInstanceStep`, with relationship paths) and
  field history (`…__History`).
- **Runs** describe each record / people object once per run (one API call) and select `IsDeleted` and people fields only when the describe
  has them; a configured people field the object lacks is logged as a warning and skipped (matching continues with the other keys).
- **User is never "Deleted in source"** (`SyncQueries.IsNeverDeleted`); an inactive user stays matched so history and lookups keep resolving.
- `PeopleOptions.EmployeeNoField` and `FederationIdField` have **no default**; a match key without a field is skipped. The ADoF pack sets them
  for `User` (`EmployeeNumber`, `FederationIdentifier`) and matches `Contact` by e-mail, then the custom field `Employee_Number__c`.
- **Validate checks every queried field** on its object, with relationship hops through the describes, and reports `field`, `object` and
  `usage` (system, watermark, externalId, people, files, approvals, history, mapped). Missing people fields are warnings; the others errors.
- **Picklist → number**: after the value map, the mapped value is converted to the target kind for number, ratio, whole number, yes/no, date
  and date-time targets (an unconvertible value → `invalidValue`); *Validate* refuses a map whose outputs do not convert
  (`valueNotConvertible`) and still warns about active picklist values without an entry. The ADoF pack (version 2) maps probability and
  impact as picklist → number with `1`…`5`.
- **Bulk re-processing** of exceptions handles people mappings first, then records by load order, page by page until every open exception of
  the filter was processed; at the end of each run, `unknownUser` record exceptions whose person is now matched are retried with the missing
  parents. "Re-process all" with the reason *Unknown user* therefore resolves every record whose User / Contact now matches an employee.
- **Tests use real describes**: the fake org loads `User` (recorded from the live org through the Salesforce connector: 179 fields, no
  `IsDeleted`) and `Contact` (the standard object reference plus `Employee_Number__c`; the connector used could not describe Contact) from
  `src/Host/Strategy.Host/Infrastructure/SalesforceDescribes/`, answers unknown columns with HTTP 400 `INVALID_FIELD` like Salesforce, and
  the approval / history objects have their real relationship fields.

## Consequences
- One extra describe call per record / people object and run (≈ 40 calls for the full ADoF pack), against a daily allowance in the tens of
  thousands.
- A stored exception payload only holds the fields that were queried when it was recorded: a people exception recorded before a new match
  field was configured matches on that field only after the next run reads it (the run then resolves the records that pointed at it).
- A changed people configuration now clears the mapping's confirmation on pack import (the options are part of the change check).
