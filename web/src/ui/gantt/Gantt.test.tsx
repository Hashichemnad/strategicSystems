import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { Gantt } from './Gantt';
import type { GanttDependency, GanttRow } from './ganttLayout';

/** A project of 5 phases × 6 milestones = 30 milestones (+ 5 phase rows), some delayed, with baselines and arrows. */
function project(): { rows: GanttRow[]; dependencies: GanttDependency[] } {
  const rows: GanttRow[] = [];
  const dependencies: GanttDependency[] = [];
  const day = (offset: number) => new Date(Date.UTC(2026, 0, 1 + offset)).toISOString().slice(0, 10);
  let previous: string | null = null;
  for (let p = 0; p < 5; p++) {
    const phaseId = `phase-${p}`;
    rows.push({
      id: phaseId,
      kind: 'phase',
      parentId: null,
      code: `PH${p + 1}`,
      titleAr: `المرحلة ${p + 1}`,
      titleEn: `Phase ${p + 1}`,
      plannedStart: day(p * 72),
      plannedEnd: day(p * 72 + 71),
      actualStart: null,
      actualEnd: null,
      baselineStart: null,
      baselineEnd: null,
      weight: 0,
      achieved: 0.4,
      status: 'in-progress',
      delayed: false,
      highLevel: false,
    });
    for (let m = 0; m < 6; m++) {
      const id = `m-${p}-${m}`;
      const start = p * 72 + m * 12;
      const zero = m === 5;
      rows.push({
        id,
        kind: 'milestone',
        parentId: phaseId,
        code: `M${p * 6 + m + 1}`,
        titleAr: `معلم ${p * 6 + m + 1}`,
        titleEn: `Milestone ${p * 6 + m + 1}`,
        plannedStart: zero ? day(start + 11) : day(start),
        plannedEnd: day(start + 11),
        actualStart: start < 120 ? day(start + 1) : null,
        actualEnd: start < 100 ? day(start + 13) : null,
        baselineStart: day(start),
        baselineEnd: day(start + 9),
        weight: 1 / 30,
        achieved: start < 120 ? 1 : 0,
        status: 'planned',
        delayed: m === 2,
        highLevel: false,
      });
      if (previous) dependencies.push({ from: previous, to: id });
      previous = id;
    }
  }
  return { rows, dependencies };
}

describe('Gantt', () => {
  it.each(['en', 'ar'] as const)(
    'renders a 30-milestone project in %s within the time budget',
    async (language) => {
      const { rows, dependencies } = project();
      const started = performance.now();
      await renderWithProviders(
        <Gantt
          rows={rows}
          dependencies={dependencies}
          start="2026-01-01"
          end="2026-12-31"
          today="2026-05-01"
          title="PROJ-001"
          testId="gantt"
        />,
        { language },
      );
      const elapsed = performance.now() - started;
      const svg = screen.getByRole('group', { name: /PROJ-001/ });
      expect(svg).toBeInTheDocument();
      expect(screen.getAllByTestId('gantt-row')).toHaveLength(35);
      expect(svg.querySelectorAll('[data-part="diamond"]')).toHaveLength(5);
      expect(svg.querySelectorAll('[data-delayed="true"]')).toHaveLength(5);
      expect(screen.getAllByTestId('gantt-dependency').length).toBeGreaterThan(0);
      expect(screen.getByTestId('gantt-today')).toBeInTheDocument();
      // Product requirement < 5 s; jsdom budget well below that.
      expect(elapsed).toBeLessThan(1000);
      console.info(`Gantt 30 milestones (${language}): ${elapsed.toFixed(0)} ms`);
    },
  );

  it('mirrors the axis in RTL: the label column moves to the right', async () => {
    const { rows } = project();
    const props = { rows, start: '2026-01-01', end: '2026-12-31', today: '2026-05-01', title: 'P' };
    const { unmount } = await renderWithProviders(<Gantt {...props} />, { language: 'en' });
    const ltrToday = Number(screen.getByTestId('gantt-today').querySelector('line')?.getAttribute('x1'));
    const width = Number(document.querySelector('svg[data-gantt]')?.getAttribute('width'));
    unmount();
    await renderWithProviders(<Gantt {...props} />, { language: 'ar' });
    const rtlToday = Number(screen.getByTestId('gantt-today').querySelector('line')?.getAttribute('x1'));
    expect(rtlToday).toBeCloseTo(width - ltrToday);
  });

  it('shows the tooltip on keyboard focus and collapses a phase with Enter', async () => {
    const { rows } = project();
    await renderWithProviders(
      <Gantt rows={rows} start="2026-01-01" end="2026-12-31" today="2026-05-01" title="P" />,
    );
    const [phase] = screen.getAllByTestId('gantt-row');
    if (!phase) throw new Error('no rows');
    fireEvent.focus(phase);
    expect(screen.getByRole('tooltip')).toHaveTextContent('Phase 1');
    expect(phase).toHaveAttribute('aria-expanded', 'true');
    fireEvent.keyDown(phase, { key: 'Enter' });
    expect(screen.getAllByTestId('gantt-row')).toHaveLength(29);
    expect(screen.getAllByTestId('gantt-row')[0]).toHaveAttribute('aria-expanded', 'false');
  });

  it('switches the zoom between quarters and months', async () => {
    const { rows } = project();
    await renderWithProviders(
      <Gantt
        rows={rows}
        start="2026-01-01"
        end="2026-12-31"
        today="2026-05-01"
        title="P"
        initialWidth={800}
      />,
    );
    const quarterWidth = Number(document.querySelector('svg[data-gantt]')?.getAttribute('width'));
    fireEvent.click(screen.getByTestId('gantt-zoom-month'));
    expect(screen.getByTestId('gantt-zoom-month')).toHaveAttribute('aria-pressed', 'true');
    expect(Number(document.querySelector('svg[data-gantt]')?.getAttribute('width'))).toBeGreaterThan(
      quarterWidth,
    );
  });
});
