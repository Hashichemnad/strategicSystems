import DOMPurify from 'dompurify';

/*
 * Rich text (SPEC-ADDENDUM A2, ADR 0161). The same allow-list as the server sanitiser
 * (src/Platform/Strategy.Platform.Kernel/Text/RichText.cs) — keep both in step.
 * A value without HTML markup is legacy / plain text: it is shown escaped and loaded into the editor as paragraphs.
 */

export const RICH_TEXT_TAGS = [
  'p',
  'br',
  'strong',
  'b',
  'em',
  'i',
  'u',
  's',
  'h2',
  'h3',
  'h4',
  'ul',
  'ol',
  'li',
  'blockquote',
  'a',
  'table',
  'thead',
  'tbody',
  'tr',
  'th',
  'td',
  'colgroup',
  'col',
  'code',
  'pre',
  'hr',
] as const;

const ATTRIBUTES = ['dir', 'href', 'rel', 'target', 'colspan', 'rowspan'];
const DIR_TAGS = new Set(['P', 'H2', 'H3', 'H4', 'UL', 'OL', 'LI', 'BLOCKQUOTE', 'TABLE', 'TH', 'TD', 'PRE']);
const DIRECTIONS = new Set(['ltr', 'rtl', 'auto']);
const SAFE_URL = /^(?:https?:|mailto:)/i;

/** Every HTML element name: a value containing one of them as a tag (or a comment) is HTML (same rule as the server). */
const KNOWN_TAGS = new Set(
  (
    'a abbr address area article aside audio b base bdi bdo blockquote body br button canvas caption center cite code col colgroup data ' +
    'datalist dd del details dfn dialog div dl dt em embed fieldset figcaption figure font footer form frame frameset h1 h2 h3 h4 h5 h6 ' +
    'head header hr html i iframe img input ins kbd label legend li link main map mark marquee math menu meta meter nav noembed noframes ' +
    'noscript object ol optgroup option output p param picture plaintext portal pre progress q rp rt ruby s samp script section select ' +
    'slot small source span strike strong style sub summary sup svg table tbody td template textarea tfoot th thead time title tr track ' +
    'tt u ul var video wbr xmp applet image'
  ).split(' '),
);

const TAG = /<\/?([a-zA-Z][a-zA-Z0-9]*)(?:[\s/][^<>]*)?>/g;

/** True when the value holds HTML markup; false for plain (legacy) text such as `a < b` or `x <y>`. */
export function isHtml(value: string | null | undefined): boolean {
  if (!value || !value.includes('<')) return false;
  if (value.includes('<!--')) return true;
  for (const match of value.matchAll(TAG)) {
    if (KNOWN_TAGS.has((match[1] ?? '').toLowerCase())) return true;
  }
  return false;
}

let instance: ReturnType<typeof DOMPurify> | undefined;

/** A dedicated DOMPurify instance, so the rich-text hook never changes other sanitising in the app. */
function purifier() {
  if (!instance) {
    instance = DOMPurify(window);
    instance.addHook('afterSanitizeAttributes', (node) => {
      if (!(node instanceof Element)) return;
      const dir = node.getAttribute('dir');
      if (dir !== null) {
        const value = dir.trim().toLowerCase();
        if (DIR_TAGS.has(node.tagName) && DIRECTIONS.has(value)) node.setAttribute('dir', value);
        else node.removeAttribute('dir');
      }
      if (node.tagName !== 'TD' && node.tagName !== 'TH') {
        node.removeAttribute('colspan');
        node.removeAttribute('rowspan');
      }
      if (node.tagName === 'A') {
        const href = node.getAttribute('href');
        if (href !== null && !SAFE_URL.test(href.trim())) node.removeAttribute('href');
        if (node.getAttribute('target') !== '_blank') node.removeAttribute('target');
        node.setAttribute('rel', 'noopener noreferrer');
      } else {
        node.removeAttribute('href');
        node.removeAttribute('target');
        node.removeAttribute('rel');
      }
    });
  }
  return instance;
}

/** Sanitises HTML with DOMPurify and the rich-text allow-list (no style / class / id / data-*, links http / https / mailto only). */
export function sanitizeHtml(html: string): string {
  return purifier().sanitize(html, {
    ALLOWED_TAGS: [...RICH_TEXT_TAGS],
    ALLOWED_ATTR: ATTRIBUTES,
    ALLOWED_URI_REGEXP: SAFE_URL,
    // Attributes other than href are checked against ALLOWED_URI_REGEXP unless they are "URI safe": these hold no URL.
    ADD_URI_SAFE_ATTR: ['dir', 'rel', 'target', 'colspan', 'rowspan'],
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false,
    KEEP_CONTENT: true,
    FORBID_CONTENTS: [
      'script',
      'style',
      'iframe',
      'object',
      'embed',
      'svg',
      'math',
      'template',
      'noscript',
      'textarea',
      'select',
      'button',
      'form',
      'title',
    ],
  });
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/** Plain text → paragraphs (blank lines) with line breaks, escaped: how a legacy value is loaded into the editor. */
export function plainToHtml(text: string): string {
  return text
    .replace(/\r\n/g, '\n')
    .trim()
    .split(/\n\s*\n/)
    .map((paragraph) => `<p>${escapeHtml(paragraph).replace(/\n/g, '<br>')}</p>`)
    .join('');
}

/** Editor content for any stored value: sanitised HTML, or plain text as paragraphs. */
export function toEditorHtml(value: string | null | undefined): string {
  if (!value) return '';
  return isHtml(value) ? sanitizeHtml(value) : plainToHtml(value);
}

/** Visible text of a value (previews, counters): block elements become new lines, list items get a bullet. */
export function toPlainText(value: string | null | undefined): string {
  if (!value) return '';
  if (!isHtml(value)) return value.replace(/\r\n/g, '\n').trim();
  const doc = new DOMParser().parseFromString(sanitizeHtml(value), 'text/html');
  const lines: string[] = [];
  let line = '';
  const flush = () => {
    if (line.trim()) lines.push(line.trim());
    line = '';
  };
  const walk = (node: Node) => {
    node.childNodes.forEach((child) => {
      if (child.nodeType === Node.TEXT_NODE) {
        line += (child.textContent ?? '').replace(/\s+/g, ' ');
        return;
      }
      if (!(child instanceof Element)) return;
      const tag = child.tagName;
      if (tag === 'BR') {
        flush();
        return;
      }
      if (tag === 'TR') {
        flush();
        line = Array.from(child.children)
          .map((cell) => cell.textContent.replace(/\s+/g, ' ').trim())
          .join(' | ');
        flush();
        return;
      }
      const block = /^(P|H2|H3|H4|LI|BLOCKQUOTE|PRE|UL|OL|TABLE|HR)$/.test(tag);
      if (block) flush();
      if (tag === 'LI')
        line +=
          child.parentElement?.tagName === 'OL'
            ? `${Array.from(child.parentElement.children).indexOf(child) + 1}. `
            : '• ';
      walk(child);
      if (block) flush();
    });
  };
  walk(doc.body);
  flush();
  return lines.join('\n');
}

/** True when a stored value has no visible text (empty editor, `<p></p>`). */
export function isBlank(value: string | null | undefined): boolean {
  return toPlainText(value).length === 0;
}
