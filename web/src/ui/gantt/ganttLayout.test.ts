import { describe, expect, it } from 'vitest';
import { jpegPdf } from './exportGantt';
import { dayOf, layoutGantt, orderRows, type GanttRow } from './ganttLayout';

function row(partial: Partial<GanttRow> & { id: string }): GanttRow {
  return {
    kind: 'milestone',
    parentId: null,
    code: partial.id.toUpperCase(),
    titleAr: null,
    titleEn: partial.id,
    plannedStart: null,
    plannedEnd: null,
    actualStart: null,
    actualEnd: null,
    baselineStart: null,
    baselineEnd: null,
    weight: 0,
    achieved: 0,
    status: 'not-started',
    delayed: false,
    highLevel: false,
    ...partial,
  };
}

const rows: GanttRow[] = [
  row({ id: 'p1', kind: 'phase', plannedStart: '2026-01-01', plannedEnd: '2026-06-30' }),
  row({
    id: 'm1',
    parentId: 'p1',
    plannedStart: '2026-01-01',
    plannedEnd: '2026-03-31',
    actualStart: '2026-01-10',
    actualEnd: '2026-04-09',
    baselineStart: '2026-01-01',
    baselineEnd: '2026-02-28',
    achieved: 0.5,
  }),
  row({
    id: 'm2',
    parentId: 'p1',
    plannedStart: '2026-04-01',
    plannedEnd: '2026-06-30',
    actualStart: '2026-04-01',
  }),
  row({ id: 'g1', plannedStart: '2026-09-15', plannedEnd: '2026-09-15' }),
];

const base = {
  rows,
  dependencies: [{ from: 'm1', to: 'm2' }],
  start: '2026-01-01',
  end: '2026-12-31',
  today: '2026-05-01',
  width: 1240,
  labelWidth: 240,
  rowHeight: 30,
  headerHeight: 40,
};

describe('ganttLayout', () => {
  it('maps the range onto the axis and mirrors the same date in RTL (x = width − x)', () => {
    const ltr = layoutGantt({ ...base, rtl: false });
    const rtl = layoutGantt({ ...base, rtl: true });
    // 2026 has 365 days over a 1000 px axis.
    expect(ltr.start).toBe('2026-01-01');
    expect(ltr.end).toBe('2027-01-01');
    expect(ltr.x('2026-01-01')).toBeCloseTo(240);
    expect(ltr.x('2027-01-01')).toBeCloseTo(1240);
    for (const date of ['2026-01-01', '2026-04-01', '2026-07-15', '2026-12-31'])
      expect(rtl.x(date)).toBeCloseTo(1240 - ltr.x(date));
    expect(rtl.x('2026-01-01')).toBeCloseTo(1000);
    // Label column: left in LTR, right in RTL; labels start from the column's outer edge.
    expect(ltr.labels.x).toBe(0);
    expect(rtl.labels.x).toBe(1000);
    expect(rtl.axis).toEqual({ x: 0, width: 1000 });
    const m1 = ltr.rows.find((r) => r.row.id === 'm1');
    const m1r = rtl.rows.find((r) => r.row.id === 'm1');
    expect(m1r?.labelX).toBeCloseTo(1240 - (m1?.labelX ?? 0));
    // The planned bar is mirrored as a whole.
    expect(m1r?.planned?.width).toBeCloseTo(m1?.planned?.width ?? -1);
    expect((m1r?.planned?.x ?? 0) + (m1r?.planned?.width ?? 0)).toBeCloseTo(1240 - (m1?.planned?.x ?? 0));
  });

  it('draws quarter bands on quarter boundaries over month ticks', () => {
    const layout = layoutGantt({ ...base, rtl: false });
    expect(layout.months).toHaveLength(12);
    expect(layout.quarters.map((q) => q.start)).toEqual([
      '2026-01-01',
      '2026-04-01',
      '2026-07-01',
      '2026-10-01',
    ]);
    expect(layout.quarters.map((q) => q.index)).toEqual([1, 2, 3, 4]);
    expect(layout.quarters[1]?.x).toBeCloseTo(layout.x('2026-04-01'));
    expect(layout.quarters[1]?.width).toBeCloseTo(layout.x('2026-07-01') - layout.x('2026-04-01'));
    // A range starting mid-quarter gets a partial first band (rows inside it: a given start never clips a row).
    const partial = layoutGantt({
      ...base,
      rows: base.rows.filter(
        (r) => (r.plannedStart ?? '') >= '2026-02-10' && (r.plannedEnd ?? '9') <= '2026-05-20',
      ),
      start: '2026-02-10',
      end: '2026-05-20',
      rtl: false,
    });
    expect(partial.start).toBe('2026-02-01');
    expect(partial.quarters.map((q) => q.start)).toEqual(['2026-02-01', '2026-04-01']);
    expect(partial.quarters[0]?.index).toBe(1);
    // RTL: the first quarter sits at the right end of the axis.
    const rtl = layoutGantt({ ...base, rtl: true });
    expect((rtl.quarters[0]?.x ?? 0) + (rtl.quarters[0]?.width ?? 0)).toBeCloseTo(1000);
    // A row planned before the given start widens the range instead of being clipped.
    const early = layoutGantt({ ...base, start: '2026-06-01', end: '2026-12-31', rtl: false });
    expect(early.start <= (base.rows[0]?.plannedStart ?? '')).toBe(true);
  });

  it('computes planned, actual (to today when open), baseline bars and the achievement fill', () => {
    const layout = layoutGantt({ ...base, rtl: false });
    const m1 = layout.rows.find((r) => r.row.id === 'm1');
    const m2 = layout.rows.find((r) => r.row.id === 'm2');
    expect(m1?.planned?.x).toBeCloseTo(layout.x('2026-01-01'));
    // End day inclusive: Jan 1 – Mar 31 ends at Apr 1.
    expect((m1?.planned?.x ?? 0) + (m1?.planned?.width ?? 0)).toBeCloseTo(layout.x('2026-04-01'));
    expect(m1?.achievedFill?.width).toBeCloseTo((m1?.planned?.width ?? 0) * 0.5);
    expect(m1?.achievedFill?.x).toBeCloseTo(m1?.planned?.x ?? -1);
    expect(m1?.actual?.x).toBeCloseTo(layout.x('2026-01-10'));
    expect((m1?.actual?.x ?? 0) + (m1?.actual?.width ?? 0)).toBeCloseTo(layout.x('2026-04-10'));
    expect(m1?.baseline?.x).toBeCloseTo(layout.x('2026-01-01'));
    expect((m1?.baseline?.x ?? 0) + (m1?.baseline?.width ?? 0)).toBeCloseTo(layout.x('2026-03-01'));
    // Started, not finished: the actual bar runs to today, where the today line is.
    expect((m2?.actual?.x ?? 0) + (m2?.actual?.width ?? 0)).toBeCloseTo(layout.x('2026-05-01'));
    expect(layout.todayX).toBeCloseTo(layout.x('2026-05-01'));
    expect(m2?.baseline).toBeNull();
    // RTL: the fill grows from the start side, i.e. the right end of the bar.
    const rtl = layoutGantt({ ...base, rtl: true }).rows.find((r) => r.row.id === 'm1');
    expect((rtl?.achievedFill?.x ?? 0) + (rtl?.achievedFill?.width ?? 0)).toBeCloseTo(
      (rtl?.planned?.x ?? 0) + (rtl?.planned?.width ?? 0),
    );
  });

  it('draws zero-length items as diamonds', () => {
    const layout = layoutGantt({ ...base, rtl: false });
    const g1 = layout.rows.find((r) => r.row.id === 'g1');
    expect(g1?.planned).toBeNull();
    expect(g1?.diamond?.cx).toBeCloseTo(layout.x('2026-09-15') + layout.dayWidth / 2);
  });

  it('routes dependency arrows from the finish of "from" to the start of "to"', () => {
    for (const rtl of [false, true]) {
      const layout = layoutGantt({ ...base, rtl });
      const m1 = layout.rows.find((r) => r.row.id === 'm1');
      const m2 = layout.rows.find((r) => r.row.id === 'm2');
      const [dep] = layout.dependencies;
      expect(dep?.x1).toBeCloseTo(layout.x('2026-04-01'));
      expect(dep?.y1).toBe(m1?.cy);
      expect(dep?.x2).toBeCloseTo(layout.x('2026-04-01'));
      expect(dep?.y2).toBe(m2?.cy);
      expect(dep?.d.startsWith('M ')).toBe(true);
      expect(dep?.head.split(' ')[0]).toBe(`${Math.round((dep?.x2 ?? 0) * 100) / 100},${dep?.y2}`);
    }
  });

  it('hides the children of a collapsed phase (and their arrows)', () => {
    const open = layoutGantt({ ...base, rtl: false });
    expect(open.rows.map((r) => r.row.id)).toEqual(['p1', 'm1', 'm2', 'g1']);
    expect(open.rows[0]?.hasChildren).toBe(true);
    expect(open.rows[1]?.depth).toBe(1);
    const closed = layoutGantt({ ...base, rtl: false, collapsed: new Set(['p1']) });
    expect(closed.rows.map((r) => r.row.id)).toEqual(['p1', 'g1']);
    expect(closed.rows[0]?.collapsed).toBe(true);
    expect(closed.rows[1]?.y).toBe(40 + 30);
    expect(closed.dependencies).toHaveLength(0);
    expect(closed.height).toBeLessThan(open.height);
  });

  it('orders children after their parent whatever the input order', () => {
    const [p1, m1, m2, g1] = rows as [GanttRow, GanttRow, GanttRow, GanttRow];
    const ordered = orderRows([m2, g1, m1, p1]);
    expect(ordered.map((o) => o.row.id)).toEqual(['g1', 'p1', 'm2', 'm1']);
  });

  it('derives the range from the rows when none is given', () => {
    const layout = layoutGantt({ ...base, start: null, end: null, rtl: false });
    expect(layout.start).toBe('2026-01-01');
    expect(layout.end).toBe('2026-10-01');
    expect(dayOf(layout.end) - dayOf(layout.start)).toBe(273);
  });
});

describe('jpegPdf', () => {
  it('writes a one-page PDF with the JPEG as a DCTDecode image and exact xref offsets', () => {
    const jpeg = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 0xff, 0xd9]);
    const pdf = jpegPdf(jpeg, 1600, 400);
    const text = new TextDecoder('latin1').decode(pdf);
    expect(text.startsWith('%PDF-1.4')).toBe(true);
    expect(text.trimEnd().endsWith('%%EOF')).toBe(true);
    expect(text).toContain('/Filter /DCTDecode');
    expect(text).toContain('/Width 1600 /Height 400');
    expect(text).toContain(`/Length ${jpeg.length}`);
    expect(text).toContain('/Count 1');
    const startxref = Number(/startxref\n(\d+)/.exec(text)?.[1]);
    expect(text.slice(startxref, startxref + 4)).toBe('xref');
    // Each xref entry points at "<n> 0 obj" (byte offsets: latin1 maps one byte to one char).
    const entries = [...text.matchAll(/(\d{10}) 00000 n/g)].map((m) => Number(m[1]));
    expect(entries).toHaveLength(5);
    entries.forEach((offset, i) => expect(text.slice(offset, offset + 8)).toBe(`${i + 1} 0 obj\n`));
  });
});
