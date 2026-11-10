import { useState } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeAll, describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { isHtml, plainToHtml, sanitizeHtml, toPlainText } from './rich-text';
import RichTextEditorImpl from './rich-text-editor';
import { RichTextEditor } from './rich-text-field';
import { RichTextView } from './rich-text-view';

describe('rich-text helpers', () => {
  it.each([
    ['<p>x</p>', true],
    ['a<br>b', true],
    ['<!-- c -->', true],
    ['a < b and c > d', false],
    ['x <y> z', false],
    ['&lt;b&gt;', false],
    ['', false],
  ])('isHtml(%j) is %s', (value, expected) => {
    expect(isHtml(value)).toBe(expected);
  });

  it.each([
    '<script>alert(1)</script>',
    '<img src=x onerror=alert(1)>',
    '<svg onload=alert(1)><circle/></svg>',
    '<iframe src="https://evil.example"></iframe>',
    '<a href="javascript:alert(1)">x</a>',
    '<a href="jav&#x61;script:alert(1)">x</a>',
    '<a href="data:text/html,<script>alert(1)</script>">x</a>',
    '<p style="background:url(javascript:alert(1))" class="c" id="i" onclick="alert(1)">x</p>',
    '<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>',
  ])('sanitizeHtml strips %s', (payload) => {
    const clean = sanitizeHtml(payload).toLowerCase();
    for (const marker of [
      '<script',
      '<img',
      '<svg',
      '<iframe',
      'javascript:',
      'data:',
      'onerror',
      'onload',
      'onclick',
      'style=',
      'class=',
      'id=',
    ]) {
      expect(clean).not.toContain(marker);
    }
  });

  it('keeps the allowed formatting and forces rel on links', () => {
    expect(sanitizeHtml('<p dir="RTL"><strong>a</strong></p><ul><li>b</li></ul>')).toBe(
      '<p dir="rtl"><strong>a</strong></p><ul><li>b</li></ul>',
    );
    expect(sanitizeHtml('<a href="https://example.org" target="_blank">x</a>')).toBe(
      '<a href="https://example.org" target="_blank" rel="noopener noreferrer">x</a>',
    );
    expect(sanitizeHtml('<p dir="sideways">a</p>')).toBe('<p>a</p>');
  });

  it('turns plain text into escaped paragraphs and HTML into plain text', () => {
    expect(plainToHtml('a < b\nline\n\nnext')).toBe('<p>a &lt; b<br>line</p><p>next</p>');
    expect(
      toPlainText(
        '<h2>T</h2><ul><li>one</li></ul><ol><li>x</li><li>y</li></ol><table><tr><td>1</td><td>2</td></tr></table>',
      ),
    ).toBe('T\n• one\n1. x\n2. y\n1 | 2');
    expect(toPlainText('plain\r\ntext')).toBe('plain\ntext');
  });
});

describe('RichTextView', () => {
  it('renders sanitised HTML and drops unsafe content', async () => {
    const { container } = await renderWithProviders(
      <RichTextView
        value={
          '<p><strong>Bold</strong> <a href="javascript:alert(1)">bad</a></p><script>window.x=1</script><img src=x onerror="alert(1)">'
        }
      />,
    );
    expect(screen.getByText('Bold').tagName).toBe('STRONG');
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText('bad').getAttribute('href')).toBeNull();
  });

  it('renders legacy plain text escaped, with paragraphs and line breaks', async () => {
    const { container } = await renderWithProviders(
      <RichTextView value={'a < b and c > d are text\nsecond line\n\nnext paragraph'} />,
    );
    expect(container.querySelector('b')).toBeNull();
    const paragraphs = container.querySelectorAll('p');
    expect(paragraphs).toHaveLength(2);
    expect(paragraphs[0]?.textContent).toBe('a < b and c > d are text\nsecond line');
    expect(paragraphs[0]?.className).toContain('whitespace-pre-line');
  });

  it('shows the fallback when empty', async () => {
    await renderWithProviders(<RichTextView value={null} fallback="none" />);
    expect(screen.getByText('none')).toBeTruthy();
  });
});

function Controlled({ initial, onValue }: { initial: string | null; onValue: (v: string | null) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <RichTextEditorImpl
      aria-label="Minutes"
      value={value}
      onChange={(v) => {
        setValue(v);
        onValue(v);
      }}
    />
  );
}

describe('RichTextEditor', () => {
  beforeAll(() => {
    // jsdom has no layout: ProseMirror asks for the element under the pointer on mouse down.
    Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => null });
  });

  it('has a labelled toolbar with named buttons in both languages', async () => {
    const { unmount } = await renderWithProviders(<Controlled initial={null} onValue={() => undefined} />);
    expect(screen.getByRole('toolbar', { name: 'Formatting' })).toBeTruthy();
    for (const name of [
      'Bold',
      'Italic',
      'Bulleted list',
      'Numbered list',
      'Link',
      'Insert table',
      // Text buttons: the visible short text starts the name, the full label completes it (WCAG 2.5.3).
      'RTL (Right to left)',
      'LTR (Left to right)',
      'Auto (Direction from the text)',
    ]) {
      expect(screen.getByRole('button', { name })).toBeTruthy();
    }
    expect(screen.getByRole('textbox', { name: 'Minutes' })).toBeTruthy();
    unmount();
    await renderWithProviders(<Controlled initial={null} onValue={() => undefined} />, { language: 'ar' });
    expect(screen.getByRole('toolbar', { name: 'التنسيق' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'غامق' })).toBeTruthy();
    expect(screen.getByRole('textbox').getAttribute('dir')).toBe('rtl');
  });

  it('loads a legacy plain value as escaped paragraphs', async () => {
    await renderWithProviders(<Controlled initial={'a < b\n\nsecond'} onValue={() => undefined} />);
    const box = screen.getByRole('textbox');
    const paragraphs = box.querySelectorAll('p');
    expect(paragraphs).toHaveLength(2);
    expect(paragraphs[0]?.textContent).toBe('a < b');
  });

  it('toggles bold on the selection and the paragraph direction', async () => {
    const user = userEvent.setup();
    const values: (string | null)[] = [];
    await renderWithProviders(<Controlled initial="<p>Hello</p>" onValue={(v) => values.push(v)} />);
    const box = screen.getByRole('textbox');
    await user.click(box);
    await user.keyboard('{Control>}a{/Control}');
    await user.click(screen.getByRole('button', { name: 'Bold' }));
    await waitFor(() => expect(values.at(-1)).toContain('<strong>Hello</strong>'));
    expect(screen.getByRole('button', { name: 'Bold' }).getAttribute('aria-pressed')).toBe('true');

    await user.click(screen.getByRole('button', { name: 'RTL (Right to left)' }));
    await waitFor(() => expect(values.at(-1)).toMatch(/^<p dir="rtl">/));
    await user.click(screen.getByRole('button', { name: 'LTR (Left to right)' }));
    await waitFor(() => expect(values.at(-1)).toMatch(/^<p dir="ltr">/));
  });

  it('moves along the toolbar with the arrow keys', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Controlled initial={null} onValue={() => undefined} />);
    const bold = screen.getByRole('button', { name: 'Bold' });
    expect(bold.tabIndex).toBe(0);
    bold.focus();
    await user.keyboard('{ArrowRight}');
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Italic' }));
    await user.keyboard('{End}');
    await user.keyboard('{Home}');
    expect(document.activeElement).toBe(bold);
  });

  it('is lazy-loaded through RichTextEditor', async () => {
    await renderWithProviders(
      <RichTextEditor aria-label="Notes" value="<p>x</p>" onChange={() => undefined} />,
    );
    expect(await screen.findByRole('textbox', { name: 'Notes' })).toBeTruthy();
  });
});
