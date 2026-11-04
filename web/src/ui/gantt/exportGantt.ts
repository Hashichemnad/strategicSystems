/**
 * Gantt export (docs/design/portfolio.md §36): PNG by drawing the serialized SVG on a canvas, PDF by embedding that canvas
 * as a JPEG (DCTDecode) in a one-page PDF written here by hand — no library.
 * The product fonts (IBM Plex Sans Arabic / IBM Plex Sans, OFL, bundled through @fontsource) are embedded in the exported
 * SVG as data-URL @font-face rules, so Arabic is shaped with the product font and not a system font (ADR 0117).
 */
import plexArabic400 from '@fontsource/ibm-plex-sans-arabic/files/ibm-plex-sans-arabic-arabic-400-normal.woff2?url';
import plexArabic600 from '@fontsource/ibm-plex-sans-arabic/files/ibm-plex-sans-arabic-arabic-600-normal.woff2?url';
import plexLatin400 from '@fontsource/ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2?url';
import plexLatin600 from '@fontsource/ibm-plex-sans/files/ibm-plex-sans-latin-600-normal.woff2?url';

/** A font file to embed: family name as the Gantt uses it, weight and the woff2 bytes as base64. */
export interface EmbeddedFont {
  family: string;
  weight: number;
  base64: string;
}

const FONT_SOURCES: readonly { family: string; weight: number; url: string }[] = [
  { family: 'IBM Plex Sans Arabic', weight: 400, url: plexArabic400 },
  { family: 'IBM Plex Sans Arabic', weight: 600, url: plexArabic600 },
  { family: 'IBM Plex Sans', weight: 400, url: plexLatin400 },
  { family: 'IBM Plex Sans', weight: 600, url: plexLatin600 },
];

let fontCache: Promise<EmbeddedFont[]> | null = null;

function toBase64(bytes: Uint8Array): string {
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}

/** Loads the bundled fonts once; a font that fails to load is skipped (the export still works, with a fallback font). */
export function loadExportFonts(fetcher: typeof fetch = fetch): Promise<EmbeddedFont[]> {
  fontCache ??= Promise.all(
    FONT_SOURCES.map(async (source) => {
      try {
        const response = await fetcher(source.url);
        if (!response.ok) return null;
        const bytes = new Uint8Array(await response.arrayBuffer());
        return { family: source.family, weight: source.weight, base64: toBase64(bytes) };
      } catch {
        return null;
      }
    }),
  ).then((fonts) => fonts.filter((f): f is EmbeddedFont => f !== null));
  return fontCache;
}

/** Inserts the fonts as data-URL @font-face rules into the standalone SVG markup (right after the opening tag). */
export function embedFonts(markup: string, fonts: readonly EmbeddedFont[]): string {
  if (fonts.length === 0) return markup;
  const rules = fonts
    .map(
      (f) =>
        `@font-face{font-family:'${f.family}';font-style:normal;font-weight:${f.weight};` +
        `src:url(data:font/woff2;base64,${f.base64}) format('woff2');}`,
    )
    .join('');
  return markup.replace(/^<svg([^>]*)>/, `<svg$1><defs><style type="text/css">${rules}</style></defs>`);
}

/** Style properties copied inline so the standalone SVG keeps the theme colours (CSS variables do not travel). */
const STYLE_PROPS = [
  'fill',
  'fill-opacity',
  'stroke',
  'stroke-opacity',
  'stroke-width',
  'stroke-dasharray',
  'opacity',
  'font-size',
  'font-weight',
  'font-family',
] as const;

/** A standalone copy of the chart with computed colours inlined. */
export function standaloneSvg(svg: SVGSVGElement): { markup: string; width: number; height: number } {
  const clone = svg.cloneNode(true) as SVGSVGElement;
  const source = [svg, ...Array.from(svg.querySelectorAll('*'))];
  const target = [clone, ...Array.from(clone.querySelectorAll('*'))];
  source.forEach((element, i) => {
    const copy = target[i];
    if (!copy || !(element instanceof SVGElement)) return;
    const computed = window.getComputedStyle(element);
    const style = STYLE_PROPS.map((p) => {
      const value = computed.getPropertyValue(p);
      return value ? `${p}:${value}` : '';
    })
      .filter(Boolean)
      .join(';');
    if (style) copy.setAttribute('style', style);
    copy.removeAttribute('class');
    copy.removeAttribute('tabindex');
  });
  const width = Number(svg.getAttribute('width')) || svg.clientWidth || 960;
  const height = Number(svg.getAttribute('height')) || svg.clientHeight || 400;
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  clone.setAttribute('width', String(width));
  clone.setAttribute('height', String(height));
  const background = window.getComputedStyle(svg.parentElement ?? svg).backgroundColor;
  const markup = new XMLSerializer()
    .serializeToString(clone)
    .replace(
      /^<svg([^>]*)>/,
      `<svg$1><rect x="0" y="0" width="${width}" height="${height}" fill="${background && background !== 'rgba(0, 0, 0, 0)' ? background : '#ffffff'}"/>`,
    );
  return { markup, width, height };
}

/** Draws the chart on a canvas (2× for sharp output). */
export async function ganttCanvas(svg: SVGSVGElement, scale = 2): Promise<HTMLCanvasElement> {
  const standalone = standaloneSvg(svg);
  const { width, height } = standalone;
  const markup = embedFonts(standalone.markup, await loadExportFonts());
  const url = URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml;charset=utf-8' }));
  try {
    const image = new Image();
    image.decoding = 'async';
    await new Promise<void>((resolve, reject) => {
      image.onload = () => resolve();
      image.onerror = () => reject(new Error('gantt.export.image'));
      image.src = url;
    });
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);
    const context = canvas.getContext('2d');
    if (!context) throw new Error('gantt.export.canvas');
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.scale(scale, scale);
    context.drawImage(image, 0, 0, width, height);
    return canvas;
  } finally {
    URL.revokeObjectURL(url);
  }
}

function download(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function canvasBlob(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<Blob> {
  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('gantt.export.blob'))), type, quality),
  );
}

export async function exportGanttPng(svg: SVGSVGElement, fileName: string): Promise<void> {
  const canvas = await ganttCanvas(svg);
  download(await canvasBlob(canvas, 'image/png'), fileName.endsWith('.png') ? fileName : `${fileName}.png`);
}

export async function exportGanttPdf(svg: SVGSVGElement, fileName: string): Promise<void> {
  const canvas = await ganttCanvas(svg);
  const jpeg = new Uint8Array(await (await canvasBlob(canvas, 'image/jpeg', 0.92)).arrayBuffer());
  const pdf = jpegPdf(jpeg, canvas.width, canvas.height);
  download(
    new Blob([pdf.buffer as ArrayBuffer], { type: 'application/pdf' }),
    fileName.endsWith('.pdf') ? fileName : `${fileName}.pdf`,
  );
}

/**
 * A minimal one-page PDF 1.4 holding one JPEG image (DCTDecode), scaled to fit an A4 landscape page (or a taller
 * page when the image is very tall) with a 24 pt margin. Byte offsets in the xref table are exact.
 */
export function jpegPdf(jpeg: Uint8Array, imageWidth: number, imageHeight: number): Uint8Array {
  const margin = 24;
  const pageWidth = 842;
  const fitWidth = pageWidth - margin * 2;
  const scale = Math.min(fitWidth / imageWidth, 1);
  const drawWidth = imageWidth * scale;
  const drawHeight = imageHeight * scale;
  const pageHeight = Math.max(595, Math.ceil(drawHeight + margin * 2));
  const offsetY = pageHeight - margin - drawHeight;
  const fmt = (n: number) => (Math.round(n * 100) / 100).toString();

  const encoder = new TextEncoder();
  const chunks: Uint8Array[] = [];
  const offsets: number[] = [];
  let length = 0;
  const push = (part: string | Uint8Array) => {
    const bytes = typeof part === 'string' ? encoder.encode(part) : part;
    chunks.push(bytes);
    length += bytes.length;
  };
  const object = (body: () => void) => {
    offsets.push(length);
    push(`${offsets.length} 0 obj\n`);
    body();
    push('\nendobj\n');
  };

  push('%PDF-1.4\n%âãÏÓ\n');
  object(() => push('<< /Type /Catalog /Pages 2 0 R >>'));
  object(() => push('<< /Type /Pages /Kids [3 0 R] /Count 1 >>'));
  object(() =>
    push(
      `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${pageWidth} ${pageHeight}] ` +
        '/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>',
    ),
  );
  object(() => {
    push(
      `<< /Type /XObject /Subtype /Image /Width ${imageWidth} /Height ${imageHeight} /ColorSpace /DeviceRGB ` +
        `/BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >>\nstream\n`,
    );
    push(jpeg);
    push('\nendstream');
  });
  const content = `q ${fmt(drawWidth)} 0 0 ${fmt(drawHeight)} ${fmt(margin)} ${fmt(offsetY)} cm /Im0 Do Q`;
  object(() => push(`<< /Length ${encoder.encode(content).length} >>\nstream\n${content}\nendstream`));

  const xref = length;
  push(`xref\n0 ${offsets.length + 1}\n0000000000 65535 f \n`);
  for (const offset of offsets) push(`${String(offset).padStart(10, '0')} 00000 n \n`);
  push(`trailer\n<< /Size ${offsets.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);

  const out = new Uint8Array(length);
  let position = 0;
  for (const chunk of chunks) {
    out.set(chunk, position);
    position += chunk.length;
  }
  return out;
}
