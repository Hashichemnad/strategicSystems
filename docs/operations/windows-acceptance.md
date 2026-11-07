# Windows acceptance — install, backup, restore and upgrade on a real Windows machine (Phase 19, A4 / A5)

The owner runs this checklist during acceptance (ADR 0163). CI covers the scripts with a fake `docker` under Windows PowerShell 5.1 and
PowerShell 7 (`deploy-windows` job); this run covers what only real Docker Desktop can: building, the containers, ports and file ACLs.

**Machine:** Windows 10 / 11 or Windows Server 2022 with **Docker Desktop 4.30+** (Linux containers, WSL 2), Python is **not** needed.
About 15 GB free disk. Use a **ZIP of the release** (no `.git` folder) — for example GitHub's *Download ZIP* of the release tag, or
`git archive --format zip -o strategy.zip HEAD` — unpacked to a path **with a space and a non-ASCII character**, e.g.
`C:\Strategy Test\نسخة`. Git may be installed (it must not matter).

Record for each step: date, Windows / Docker Desktop / PowerShell versions, exit code (`$LASTEXITCODE`) and anything unexpected.

## A. Windows PowerShell 5.1 (`powershell.exe`)

Open **Windows PowerShell** (not PowerShell 7) in the release folder; check `$PSVersionTable.PSVersion` shows `5.1.x`.

| # | Step | Expected |
|---|---|---|
| A1 | `powershell -ExecutionPolicy Bypass -File deploy\install.ps1` | Builds `strategy-app:1.0.0` (compose's build output scrolls by, **no red `NativeCommandError` text**), starts the services, prints `http://<host>:8080` and a one-time setup token; exit code 0 |
| A2 | Same command again, output redirected: `powershell -ExecutionPolicy Bypass -File deploy\install.ps1 *> install.log; $LASTEXITCODE` | 0; `install.log` contains "Keeping the existing deploy\.env" (redirected output is the case that used to fail) |
| A3 | `Get-Content deploy\.env -Encoding Byte -TotalCount 3` and open `deploy\.env` in Notepad | No BOM (`239 187 191` must **not** appear); the comment line with `→` (Portfolio Management) is readable, not `â†’` |
| A4 | `docker image inspect strategy-app:1.0.0 \| Select-String 'image.revision', 'image.version'` | revision `""` (ZIP release without `.git`); version `1.0.0` |
| A5 | Open `/setup`, enter the token, create the administrator, sign in, upload one document | works |
| A6 | `powershell -ExecutionPolicy Bypass -File deploy\backup.ps1` | `backups\strategy-1.0.0-<UTC>\` with `database.dump`, `files.tar.gz`, `env`, `table-counts.tsv`, `SHA256SUMS`, `manifest.json`; only you and Administrators have access (folder Properties → Security); exit 0 |
| A7 | `powershell -ExecutionPolicy Bypass -File deploy\restore.ps1 -Backup <that folder> -Yes` | checksums OK, "N tables, M rows: identical", audit chain verified, application healthy; the document from A5 opens |
| A8 | Make a second ZIP folder with `deploy\VERSION` = `1.0.1` and the old `deploy\.env` copied in; there `powershell -ExecutionPolicy Bypass -File deploy\upgrade.ps1` | new image `strategy-app:1.0.1` built while 1.0.0 runs, pre-upgrade backup, migrations, `APP_VERSION=1.0.1` in `.env`, healthy; exit 0 |
| A9 | Rollback: `$env:APP_VERSION='1.0.0'; powershell -ExecutionPolicy Bypass -File deploy\restore.ps1 -Backup <pre-upgrade backup> -Yes` | 1.0.0 running with the data from before the upgrade |
| A10 | Stop Docker Desktop, run `deploy\backup.ps1` | "Docker is installed but not running…", exit 10 |

## B. PowerShell 7 (`pwsh`)

Repeat A1, A2, A6, A7 and A8 with `pwsh -File deploy\<script>.ps1` (same expectations). Optionally with a slow or throttled connection
(e.g. a mobile hotspot): `deploy\.env` → `NUGET_RESTORE_ATTEMPTS=5`, `NPM_FETCH_RETRIES=8`, then `deploy\install.ps1`; the build may take
longer but must not fail on a single timeout.

## C. Clean-up

`docker compose -f deploy\docker-compose.yml --profile app down -v` in each test folder, `docker image rm strategy-app:1.0.0 strategy-app:1.0.1`,
`docker builder prune -af`.

## Result

| Run | Date | Windows / Docker Desktop / PowerShell | Result | Notes |
|---|---|---|---|---|
| A (5.1) | | | | |
| B (7) | | | | |
