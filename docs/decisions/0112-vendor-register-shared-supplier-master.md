# 0112 — Vendor register as the shared supplier master

- **Status:** Accepted by the owner (Phase 11)
- **Context:** SPEC §1.18: the vendor register is master data, also the supplier list for Contracts / POs and the basis of supplier rating.

## Decision
- Table `portfolio.vendor` (number `VND-{SEQ:4}`, names AR / EN, trade licence, expiry, country, address, contact, e-mail, phone, status Active /
  Suspended / Blacklisted with reason). Managed by `portfolio.vendor.manage` (PM Office). Other modules read it through
  `IVendorDirectory` in Portfolio.Contracts (search, get by id) — no other module writes it.
- Project contracts gain `vendor_id` (picked from the register; the free-text supplier stays for older rows) and `end_date`.
- Vendor performance history = Wasel ratings (ADR 0115) plus the Phase 10 contract supplier ratings of contracts linked to the vendor.

## Consequences
One supplier list; blacklisting a vendor immediately ends its portal access.
