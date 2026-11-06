# 0155 — Outbound URL policy (server-side request forgery guard)

- **Status:** Accepted by the owner (Phase 18)
- **Context:** Administrators type URLs and host names the server then calls: AI provider endpoints (Azure, OpenAI-compatible / Ollama / vLLM),
  identity-provider authority, discovery, token, user-info and SAML metadata URLs, LDAP hosts, SMTP host. A stolen or malicious administrator
  session (or a mistake) could point them at the host itself (`localhost:5432`), the cloud metadata service (`169.254.169.254`) or internal
  services, and read the answer through *Test connection* or error messages (ASVS V12.6.1, V5.2.6). Customers install on-premises: their AI
  server, IdP and directory are usually on **private** addresses, so a blanket "public internet only" rule would break legitimate installs.

## Decision
- `Strategy.Platform.Kernel.Net.OutboundUrlPolicy` with options `Outbound:AllowPrivateNetworks` (default **true**) and `Outbound:AllowLoopback`
  (default **false**; true only in Development and in test hosts, where Keycloak / OpenLDAP / fake providers listen on localhost).
- Always refused: schemes other than http / https, credentials in the URL, link-local (169.254.0.0/16 incl. the metadata service, fe80::/10),
  unspecified, multicast, broadcast and reserved ranges; loopback unless allowed; private ranges (10/8, 172.16/12, 192.168/16, 100.64/10,
  198.18/15, fc00::/7) when `AllowPrivateNetworks=false`. IPv4-mapped IPv6 is judged as IPv4.
- Two layers: **on save / test** (`Check(url)`, `CheckHost(host)`) → validation error `validation.outboundUrlRefused` on the field (AI provider
  `endpoint`, identity-provider URL fields, LDAP `host`); **at connect time** (`CreateHandler()`, a `SocketsHttpHandler` whose
  `ConnectCallback` resolves the name and connects only to allowed addresses; `AllowAutoRedirect=false`). The connect-time check defeats DNS
  rebinding and redirects to forbidden addresses. Used by: every AI provider SDK (`AiOutboundHttp`, shared `HttpClient` for the Anthropic and
  OpenAI SDKs), the OIDC and UAE Pass back channel (`BackchannelHttpHandler`), the identity-provider *Test connection* client.
- Not covered by the connect-time layer (validated on save only, documented as residual risk): SAML metadata download inside Sustainsys.Saml2
  (its own HTTP client), LDAP (`System.DirectoryServices.Protocols` connects itself), SMTP (MailKit). Gotenberg, S3 / MinIO, ClamAV, OTLP are
  set by the operator in configuration (environment), not in the UI, and are trusted.

## Consequences
Installations whose AI / IdP endpoints are only reachable on loopback (a model on the same host) set `Outbound__AllowLoopback=true`. High-
security installations with no internal providers set `Outbound__AllowPrivateNetworks=false`. Error for the user: the field message, or
`ai.errors.endpointRefused` on *Test connection*.
