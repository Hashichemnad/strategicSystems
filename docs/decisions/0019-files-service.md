# 0019 — Files service: purposes, storage, checks and owner access

- **Status:** Accepted (Phase 3)
- **Context:** SPEC §0.2 "typed attachments per record, version history, size / type limits, virus-scan hook"; CLAUDE.md §14 (outside the
  web root, allow-list, magic number, optional ClamAV, versioned, authorised and audited downloads, pdf.js preview). Phase 1–2 kept employee
  photos and logos as bytea (ADR 0014, 0017).

## Decision
- **Purposes in code** (`FilePurpose`: key, owner type, allowed kinds, max size, single-file): the owner declares what may be stored; the
  global upload limit and ClamAV switch are settings (Settings → Files).
- **Kinds by content:** `ContentInspector` sniffs PDF, PNG, JPEG, GIF, WebP, OOXML (by the main part named in `[Content_Types].xml`),
  legacy OLE Office and UTF-8 text / CSV; the sniffed kind must equal the extension's kind. SVG, HTML, scripts and executables are never
  recognised. File names are sanitised.
- **Storage:** `IFileStore` with a local volume (default, `/var/lib/strategy/files` in the container) or S3-compatible storage
  (`AWSSDK.S3`, Apache-2.0). Keys `yyyy/MM/<version id>`; versions are immutable; deletion is a soft delete with reason.
- **Virus scan:** in-house clamd `INSTREAM` client; infected uploads are refused (audited); when scanning is required and clamd is
  unreachable, the version is kept `pending`, cannot be downloaded, and the `files.rescan-pending` job scans it later.
- **Owner access:** owners register `IFileOwnerAccess` (read / write through `IAccessService`). The generic API answers **404** for an
  owner outside the caller's scope and writes `file.download.denied`; uploads, downloads and deletions are audited events, the
  file rows themselves are audited entities. Downloads carry `Content-Security-Policy: sandbox` and `no-store`.
- **Photos and logos** keep their endpoints; their bytes move by migration into `files.pending_import` and are written to the store by the
  files seeder (Files migrates before Settings and Organisation).
- **Preview:** images inline; PDFs with pdf.js (`pdfjs-dist`, Apache-2.0) with eval off, `disableFontFace` (the CSP forbids the inline
  `@font-face` it would inject) and `useSystemFonts: false` (otherwise it substitutes the SPA's own IBM Plex web font and draws wrong glyphs).

## Consequences
The S3 provider is implemented but not covered by an automated test in this environment (no MinIO image available through the registry
mirror); the local provider is covered end to end.
