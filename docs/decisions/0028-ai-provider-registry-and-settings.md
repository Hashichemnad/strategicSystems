# 0028 — AI provider registry, feature routing, per-role access and limits

- **Status:** Accepted (Phase 5)
- **Context:** SPEC §0.5 and CLAUDE.md §12: provider-neutral AI, configured in Settings (no hard-coded default), model per feature, master /
  per-feature / per-role switches, encrypted keys, usage log and monthly limits. Features must never talk to a provider directly, and the
  product must work fully with AI switched off.

## Decision
- **New platform component** `Strategy.Platform.AI` (schema `ai`) with `Strategy.Platform.AI.Contracts` (what modules plug in: features, AI
  forms, record sources, search sources, tools — see ADR 0031). Endpoints under `/api/v1/ai`.
- **Providers** are rows (`ai.provider`): type, endpoint, key, default model, model list, temperature, maximum answer tokens, time-out,
  active. The key is protected with `ISecretProtector` (purpose `ai.provider.key.v1`, ADR 0012), audited masked, never returned (`hasApiKey`).
- **Adapters** (`IAiProviderAdapter`, one per type) build a Microsoft.Extensions.AI `IChatClient`: the official Anthropic SDK (Apache-2.0)
  for Claude; the OpenAI SDK (MIT) for OpenAI, **Azure OpenAI through its v1 endpoint** (`…/openai/v1/`, deployment name as model, `api-key`
  header added) and any OpenAI-compatible server (Ollama, vLLM). No `Azure.AI.OpenAI` package is needed. A new provider = one adapter.
- **Demo provider** (`demo`): an offline, deterministic `IChatClient` registered only when `AI:EnableDemoProvider=true` (Development
  configuration and the E2E host; never in production configuration). The development seed configures it and switches AI on so the demo
  works without a key. Tests use their own fake adapter.
- **Settings** area `ai` (`settings.setting`): master switch (**off** on a new installation), default provider, per feature `{enabled,
  providerId, model}`, data sharing, limits. Saved through the settings editor, so changes are versioned (409) and audited.
- **Per-role access is a permission per feature** (`ai.<feature>.use`) rather than a role list inside AI settings: the existing roles,
  assignments, data review and permission-matrix screens apply unchanged. The seeded role *AI user* holds them all and is assigned to
  everyone; an administrator narrows it. Administration: `ai.settings.manage`, `ai.usage.read` (role *AI administrator*, MFA, one of the
  platform administrator roles).
- **One gateway** (`AiGateway`) for every call: internal principal → master → feature → permission → provider → limits → data controls
  → prompt → call (time-out) → JSON + schema → unmask → usage + audit. A disabled feature answers **404 `ai.featureDisabled`** (like a
  disabled module); `GET /ai/capabilities` tells the SPA what to show.
- **Usage log** `ai.usage_log` (append-only, not field-audited): user, feature, provider, model, tokens in / out, latency, outcome, record.
  **Monthly limits** (installation tokens, requests per user) are counted from the first day of the month in the installation's time zone,
  before each call → 422 `ai.limitReached`. Each call is also an explicit audit event `ai.<feature>` with **channel `ai`** (a new
  `AuditEvent.Channel` override keeps the acting user while labelling the channel).

## Consequences
- Changing provider or model is configuration only. Test connection (`POST /ai/providers/{id}/test`) works while AI is off.
- The usage limit is checked before the call and the tokens are known after it, so one call may overshoot the token limit; the next is
  refused. Per-user token limits and per-feature limits can be added to `AiLimits` later.
- "Similar records" needs no provider (ADR 0030 §similar) but still follows the master and feature switches and is logged with zero tokens.
