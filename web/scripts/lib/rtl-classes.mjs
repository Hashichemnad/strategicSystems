// Physical (left/right) Tailwind utilities break RTL. CLAUDE.md §4.2 requires logical ones:
// ms-/me-/ps-/pe-/start-/end-/border-s/rounded-s/text-start… instead of ml-/mr-/pl-/pr-/left-/right-…
const PHYSICAL = new RegExp(
  '(?<![\\w-])-?(?:' +
    // spacing / position utilities always take a value: ml-2, pr-4, left-0, scroll-mr-3…
    '(?:ml|mr|pl|pr|left|right|scroll-ml|scroll-mr|scroll-pl|scroll-pr)-[\\w./[\\]%-]+' +
    // borders and radii work with or without a value: border-r, border-l-2, rounded-l, rounded-tr-lg…
    '|(?:border-[lr]|rounded-(?:l|r|tl|tr|bl|br))(?:-[\\w./[\\]%-]+)?' +
    '|(?:text|float|clear)-(?:left|right)' +
    ')(?![\\w-])',
  'g',
);

/** A line can opt out with a trailing `rtl-ok` comment and a reason (e.g. a chart that is always LTR). */
const OPT_OUT = /rtl-ok/;

/**
 * Returns `{ line, match }` for every physical utility class in string literals of the given source text.
 * Only quoted strings are scanned so identifiers such as `paddingLeft` in code never match.
 */
export function findPhysicalClasses(source) {
  const findings = [];
  source.split('\n').forEach((text, index) => {
    if (OPT_OUT.test(text)) return;
    for (const literal of text.matchAll(/(["'`])((?:\\.|(?!\1).)*)\1/g)) {
      for (const match of literal[2].matchAll(PHYSICAL)) {
        findings.push({ line: index + 1, match: match[0] });
      }
    }
  });
  return findings;
}
