# 0194 — Webhooks: catalogue from integration events, HMAC signature, back-off, dead letters, replay

- **Status:** Proposed (Phase 22) — awaiting owner acceptance
- **Context:** Subscriptions per integration client to integration events, JSON payloads with ids and changed fields, HMAC signature and
  timestamp, retries with back-off, dead-letter list, replay, delivery log, SSRF guard on target URLs.

## Decision
- Modules register webhook events (`AddWebhookEvent<TEvent>`) — code, labels, resource type, permission, resource id, sample. The
  registration subscribes a generic fan-out handler to the integration event (outbox, ADR 0043).
- Fan-out creates one delivery per (subscription, event key — SHA-256 of type and payload), unique, so outbox retries never duplicate;
  only for active clients whose allowed systems / types include the event and **who may read the record** (Links registry visibility as
  the client, or the event permission).
- Signature: `X-Strategy-Signature: v1=<hex HMAC-SHA256(secret, "<timestamp>.<body>")>` with `X-Strategy-Timestamp` (Unix seconds); secret
  per subscription, encrypted with Data Protection (needed to sign), shown once, regenerable.
- Delivery job every minute; outbound client from `OutboundUrlPolicy.CreateHandler()` (connect-time SSRF guard, no redirects), URL checked on
  save (https only unless `Integration:AllowHttpWebhooks`); 2xx = delivered; otherwise retry after `base · 2^(n−1)` (cap 6 h, default base 60 s,
  8 attempts) and then **dead**. Replay resets a delivery; every attempt is logged. Times from `IClock`.

## Consequences
At-least-once delivery; receivers deduplicate by `X-Strategy-Delivery`. Events without a natural resource the client can read are not
delivered to it.
