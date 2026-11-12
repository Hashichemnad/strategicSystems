# 0192 — OData v4: URI parser and CSDL from Microsoft.OData.Core, execution through the record sources

- **Status:** Proposed (Phase 22) — awaiting owner acceptance
- **Context:** Read-only OData v4 feeds per system for Power BI, Excel, Tableau and Qlik: `$select`, `$filter`, `$top`, `$skip`, `$orderby`,
  `$count`, `$expand`, metadata, paging, labels by language, every row permission-filtered. Modules own their schemas (no cross-schema SQL),
  so `Microsoft.AspNetCore.OData` over one EF model does not fit; a hand-written parser and CSDL writer risk incompatibility with BI clients.

## Decision
- Use **Microsoft.OData.Core / Microsoft.OData.Edm / Microsoft.Spatial 8.2.3 (MIT)** for: building an `EdmModel` per request (only what the
  caller may read), `ODataUriParser` (filter, orderby, select / expand trees, top / skip / count) and `CsdlWriter` for `$metadata`.
- Translate the semantic tree to the neutral `RecordFilter` (ADR 0190): comparisons, `and / or / not`, `in`, `contains / startswith /
  endswith`, `null`; anything else (`$apply`, `$search`, `$compute`, any / all, navigation or label filters) → 400 `notImplemented`.
- JSON responses written in-house (minimal metadata: `@odata.context`, `@odata.count`, `value`, `@odata.nextLink`); server paging with
  `$skip` next links; `$expand` executed as one query per relationship with an `In` filter on the child's own scoped source.
- Labels: a read-only `<field>Label` property for each Lookup / Unit / Employee field in the `lang` language; codes always present.

## Consequences
Standard OData clients can browse and refresh; unsupported query options fail clearly. One new direct dependency family (MIT), listed in
`THIRD_PARTY_LICENSES.md`.
