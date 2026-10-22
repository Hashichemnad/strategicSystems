# Integrated Strategy System — Claude Code build package

This package contains everything Claude Code needs to build the product.

| File | Purpose |
|---|---|
| `CLAUDE.md` | How to build: stack, architecture, security, audit, bilingual/RTL, testing, working method. Claude Code reads it automatically in every session. |
| `docs/SPEC.md` | What to build: the full requirements for the shared platform and all 11 systems (source of truth). |
| `docs/BUILD_PLAN.md` | In which order: 19 phases, each with a paste-ready prompt and acceptance checks, plus a progress checklist. |

## Option A — Browser only (Claude Code on the web, nothing installed on your PC)

Claude Code runs in Anthropic's cloud at **claude.ai/code**. It needs a GitHub repository to read and save the code. (Available on Pro, Max, Team and Enterprise plans.)

1. **GitHub (in the browser):** create a free account at github.com → **New repository** → name it (e.g. `strategy-system`) → **Private** → tick "Add a README" → Create.
   Then **Add file → Upload files**, drag in `CLAUDE.md`, `README.md` and the `docs` folder from this package (keep the folder), and **Commit changes**.
2. **Connect Claude to GitHub:** open **claude.ai/code**, follow the onboarding, and install the Claude GitHub App on your new repository.
3. **Create the cloud environment** (environment selector under the message box → add environment):
   - Name: `strategy-dotnet` · Network access: **Trusted**
   - Setup script (installs the .NET 10 SDK; Node.js 22, Docker and PostgreSQL 16 are already in the cloud machine):
     ```bash
     #!/bin/bash
     set -e
     if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^10\.'; then
       apt-get update
       apt-get install -y dotnet-sdk-10.0 || {
         . /etc/os-release
         wget -q "https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb" -O /tmp/ms.deb
         dpkg -i /tmp/ms.deb && apt-get update && apt-get install -y dotnet-sdk-10.0
       }
     fi
     dotnet --info | head -n 5
     ```
     If the install is ever blocked, ask Claude in the session to install the .NET 10 SDK and fix the setup script — it can see the error.
4. **Each phase = one new session:** select your repository and the `strategy-dotnet` environment, set the mode dropdown to **Plan**, paste the phase prompt from `docs/BUILD_PLAN.md`, review the plan, then switch the mode so it can build.
5. Claude works on its own branch. When the phase is done, review the changes (diff view), **create a pull request**, and **merge** it in GitHub. Start the next phase in a new session so it begins from the merged code.
6. You can close the browser while it works; the session keeps running and you can check it from any device, including the Claude mobile app.

Notes for browser-only use:
- Cloud sessions share your plan's usage limits, so spread the phases over several days if needed.
- To see the screens yourself, ask Claude to commit Playwright screenshots (Arabic and English) for each phase; to click through the live app, deploy it to a test server with the Docker files from Phase 0 (or Phase 18).
- In the Phase 0 prompt, the CI file targets GitHub Actions, which will run automatically on your repository.

## Option B — On your own PC

## Before you start

Install on the development machine:
- .NET 10 SDK
- Node.js LTS (22 or later) and npm
- Docker Desktop (or Docker Engine + Compose)
- Git
- Claude Code

## Start the build (Option B)

1. Create an empty Git repository and copy this package into its root, keeping the folders (`CLAUDE.md` at the root, the two documents under `docs/`).
2. Commit: `git add . && git commit -m "docs: requirements and build plan"`.
3. Open a terminal in the repository root and start Claude Code.
4. Open `docs/BUILD_PLAN.md`, copy the **Phase 0** prompt, paste it into Claude Code.
5. Review the plan it proposes, approve it, and let it build. When it finishes, check the phase's acceptance checks yourself in Arabic and English.
6. Tick the phase in the progress checklist, commit, then start a fresh session (or `/clear`) and paste the next phase's prompt.

## Tips

- One phase per session keeps Claude Code focused and the results consistent.
- Always review the plan before approving — that is where misunderstandings are cheapest to fix.
- If a requirement changes, edit `docs/SPEC.md` first, then ask Claude Code to apply the change and update tests.
- Claude Code records any assumption it had to make as an ADR in `docs/decisions/`. Review these after each phase.
- Default values for settings that weren't confirmed are listed in SPEC Appendix A. All of them are editable in the product's Settings after installation.
