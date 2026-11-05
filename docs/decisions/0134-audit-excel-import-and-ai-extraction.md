# 0134 — Excel import of findings and AI report-to-findings extraction

- **Status:** Accepted by the owner (Phase 16)
- **Context:** SPEC §10.3 B step 1 and §10.7: findings are recorded by form, Excel import or AI extraction from the report file.

## Decision
- Template with bilingual headers and a hidden key row (platform `ImportTable`); one row per recommendation grouped by `finding_ref`.
  Validate returns row / column issues (JSON) and an xlsx validation report; commit is all-or-nothing in one transaction, channel `import`.
- AI extraction (`auditmgmt-extract`) reads uploaded PDF / Word / scans (OCR, ADR 0033) or pasted text, passes the document as delimited
  untrusted data and returns **drafts** validated against a JSON schema. Nothing is saved until the Audit Office confirms the rows it keeps;
  confirmation goes through the same validation and create path as the form (due date still required per recommendation).

## Consequences
The three entry paths produce identical records and audit entries.
