import { describe, expect, it } from 'vitest';
import { embedFonts, loadExportFonts, type EmbeddedFont } from './exportGantt';

const fonts: EmbeddedFont[] = [
  { family: 'IBM Plex Sans Arabic', weight: 400, base64: 'QVJBQklD' },
  { family: 'IBM Plex Sans', weight: 400, base64: 'TEFUSU4=' },
];

describe('Gantt export fonts (ADR 0117)', () => {
  it('embeds both bundled families as woff2 data URLs inside the exported SVG', () => {
    const markup = embedFonts('<svg xmlns="http://www.w3.org/2000/svg" width="10"><rect/></svg>', fonts);
    expect(markup.startsWith('<svg xmlns="http://www.w3.org/2000/svg" width="10"><defs><style')).toBe(true);
    expect(markup).toContain("@font-face{font-family:'IBM Plex Sans Arabic'");
    expect(markup).toContain("@font-face{font-family:'IBM Plex Sans'");
    expect(markup).toContain('url(data:font/woff2;base64,QVJBQklD)');
    expect(markup).toContain('url(data:font/woff2;base64,TEFUSU4=)');
    expect(markup).toContain('<rect/>');
  });

  it('leaves the markup unchanged when no font could be loaded', () => {
    const svg = '<svg width="1"></svg>';
    expect(embedFonts(svg, [])).toBe(svg);
  });

  it('loads every bundled font and skips the ones that fail, without throwing', async () => {
    let calls = 0;
    const fetcher = (() => {
      calls++;
      if (calls === 1) return Promise.reject(new Error('offline'));
      return Promise.resolve(new Response(new Uint8Array([1, 2, 3])));
    }) as unknown as typeof fetch;
    const loaded = await loadExportFonts(fetcher);
    expect(calls).toBe(4);
    expect(loaded).toHaveLength(3);
    expect(new Set(loaded.map((f) => f.family))).toEqual(new Set(['IBM Plex Sans Arabic', 'IBM Plex Sans']));
    expect(loaded[0]?.base64).toBe('AQID');
  });
});
