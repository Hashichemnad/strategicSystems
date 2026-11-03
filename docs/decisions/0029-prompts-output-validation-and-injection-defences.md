# 0029 — Prompt templates, output validation, prompt-injection defences and masking

- **Status:** Accepted (Phase 5)
- **Context:** CLAUDE.md §12: versioned prompt templates per feature, bilingual instructions, structured JSON output validated against a
  schema, untrusted document text passed as data, optional PII masking. Several providers with different "JSON mode" support must work.

## Decision
- **Templates** are embedded Markdown resources `Prompts/<feature>.v<n>.md` with front matter (`feature`, `version`) and a system part and a
  user part separated by `=== user ===`. The newest version wins; modules add theirs from their own assembly (`AddAiPromptAssembly`).
  `{{name}}` placeholders take **trusted values only**; an unknown placeholder is a programming error.
- **Output**: the prompt inlines the JSON schema; the reply's first JSON object (code fences ignored) is validated by an **in-house
  validator** for the subset our schemas use (`type`, `properties`, `required`, `additionalProperties`, `enum`, `items`, `maxItems`,
  `maxLength`, `minimum`, `maximum`). One repair round sends the errors back; then `ai.invalidOutput`. No provider-specific JSON mode is
  relied on, so Anthropic, OpenAI, Azure and small self-hosted models behave the same. No extra package (a JSON-schema library would add a
  dependency for a small subset).
- **Prompt-injection defences** for documents and record text:
  1. untrusted text only in the user message, inside `<untrusted_document id nonce>` with a fresh random nonce per call;
  2. tag look-alikes in the text (any case, spacing, `_`/`-`) are replaced by `[removed-tag]`, control characters removed, so content cannot
     close the block or open a new one;
  3. the system prompt says the block is data, never instructions, and that the only output is the schema;
  4. extraction gets no tools; "Ask the system" gets read tools that check permissions anyway (ADR 0031);
  5. outputs are post-processed against the form (unknown fields dropped, values coerced, **source snippets verified in the document** —
     an unverified value is flagged and its confidence capped at 0.3);
  6. nothing is acted on automatically: a person reviews every value (CLAUDE.md §2 rule 6).
- **Data controls**: per system "may be sent" and excluded field names (removed before sending; also applied by the `get_record` tool for
  MCP clients), documents on / off. **Masking** (on by default) replaces e-mail addresses, Emirates ID numbers, phone numbers (with a leading
  `+` or `0`; plain amounts are untouched) and the names of the record's people with placeholders, and restores them in every string of the
  output.

## Consequences
- Masking names relies on the caller supplying them (`AiRecordContent.People`); free-text names in a document are not detected (no NER).
- Prompt wording changes are a new template version, reviewed like code.
