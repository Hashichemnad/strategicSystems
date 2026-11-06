# 0152 — Product version and release notes in the application

- **Status:** Proposed (Phase 18)
- **Context:** Release 1.0.0 needs a version that support, upgrades and backups can rely on, and users and administrators should see what
  changed. The version must not be advertised to anonymous callers (ASVS 14.3.3).

## Decision
- One product version in `Directory.Build.props` (`<Version>1.0.0</Version>`): assembly, file and informational version of every
  project. The SDK appends `+<git commit>` to the informational version in a repository build; image builds without `.git` pass
  `-p:SourceRevisionId=<commit>`.
- `GET /api/v1/system/version` (signed-in users): version, informational version, commit (12 characters) and build date (write time of
  the host assembly). `GET /api/v1/system/release-notes?language=ar|en` (signed-in users): the release notes embedded in the host as
  `ReleaseNotes/{en,ar}/<version>.md`, newest first.
- The SPA shows both in **About** (profile menu). The Markdown subset (headings, bullets, paragraphs) is rendered as text nodes only —
  never as HTML.
- `CHANGELOG.md` at the repository root and the full release notes in `docs/` are the documentation counterpart (release work).

## Consequences
Every release bumps one property and adds two Markdown files; nothing else changes. Anonymous callers learn nothing about the version.
