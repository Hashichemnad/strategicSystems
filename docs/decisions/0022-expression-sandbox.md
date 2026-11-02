# 0022 — Shared sandboxed expression service (in-house)

- **Status:** Accepted (Phase 4)
- **Context:** CLAUDE.md §4.1 / §15 ask for NCalc "or an equivalent MIT evaluator" for admin-defined formulas (approval entry conditions now;
  risk scoring, reward rules later), sandboxed, whitelisted variables, time-limited.

## Decision
- An **in-house evaluator** in `Strategy.Platform.Kernel/Expressions` instead of NCalc: tokenizer → recursive-descent parser → static type
  check against the declared variables → tree-walking evaluator. No reflection, no member access on CLR objects, no user-defined functions,
  no loops. Values: decimal number, text, boolean, date, null.
- **Why not NCalc:** we need save-time validation with typed variables (unknown variable / function, type mismatch, "a condition must be
  yes / no"), error positions and translatable message keys, decimal-only arithmetic for money, and hard limits; with NCalc these would be
  layered on top of its own parser and its extension points (custom functions / parameters by reflection-free callbacks) anyway. The
  grammar is small (≈ 600 lines with tests) and fully owned.
- Limits: 2 000 characters, 300 nodes, nesting depth 40, 10 000 evaluation steps per evaluation (no expression can run long).
- Null semantics: `==` / `!=` compare with null; ordering or arithmetic with null gives null; `and` / `or` treat null as false.
- Errors carry `code`, `messageKey` (`expressions.errors.*`), `position`, so the UI shows them inline in both languages.

## Consequences
Later formulas (risk score, rewards) reuse the same service with their own variable sets and function whitelists.
