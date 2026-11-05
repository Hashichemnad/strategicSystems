import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { HeatCell, RatingBand } from './api';
import { HeatMap, RiskMatrix } from './components';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };

const bands: RatingBand[] = [
  {
    code: 'low',
    labelAr: 'منخفض',
    labelEn: 'Low',
    color: '#16a34a',
    min: 1,
    max: 4,
    residualStatus: 'no-issue',
  },
  {
    code: 'medium',
    labelAr: 'متوسط',
    labelEn: 'Medium',
    color: '#facc15',
    min: 5,
    max: 9,
    residualStatus: 'continuous-review',
  },
  {
    code: 'high',
    labelAr: 'مرتفع',
    labelEn: 'High',
    color: '#f97316',
    min: 10,
    max: 16,
    residualStatus: 'active-management',
  },
  {
    code: 'critical',
    labelAr: 'حرج',
    labelEn: 'Critical',
    color: '#dc2626',
    min: 20,
    max: 25,
    residualStatus: 'active-management',
  },
];

function cells(): HeatCell[] {
  const all: HeatCell[] = [];
  for (let l = 1; l <= 5; l++)
    for (let i = 1; i <= 5; i++) {
      const score = l * i;
      const band = score <= 4 ? 'low' : score <= 9 ? 'medium' : score <= 16 ? 'high' : 'critical';
      all.push({ likelihood: l, impact: i, score, band, enterprise: 0, project: 0 });
    }
  return all.map((c) =>
    c.likelihood === 4 && c.impact === 5
      ? { ...c, enterprise: 3, project: 2 }
      : c.likelihood === 1 && c.impact === 2
        ? { ...c, enterprise: 1 }
        : c,
  );
}

async function render(ui: React.ReactElement, language: 'ar' | 'en' = 'en') {
  // The band labels come from the props; options are not needed (the request never resolves).
  vi.spyOn(api as unknown as LooseGetApi, 'GET').mockReturnValue(
    new Promise(() => {}),
  );
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(<QueryClientProvider client={client}>{ui}</QueryClientProvider>, { language });
}

describe('HeatMap', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the counts, enterprise and project, with band colours', async () => {
    await render(<HeatMap kind="residual" cells={cells()} bands={bands} />);
    const cell = screen.getByTestId('risk-heat-residual-4-5');
    expect(cell).toHaveTextContent('3');
    expect(cell).toHaveTextContent('P2');
    expect(cell).toHaveStyle({ backgroundColor: '#dc2626' });
    expect(screen.getByTestId('risk-heat-residual-1-2')).toHaveTextContent('1');
    expect(screen.getByTestId('risk-heat-residual-3-3')).toHaveTextContent('');
    expect(screen.getAllByRole('gridcell')).toHaveLength(25);
  });

  it('names every cell with likelihood, impact, band and count', async () => {
    await render(<HeatMap kind="inherent" cells={cells()} bands={bands} />);
    expect(screen.getByTestId('risk-heat-inherent-4-5')).toHaveAccessibleName(
      'Likelihood 4, impact 5, Critical: 3 risks, 2 project risks',
    );
    expect(screen.getByTestId('risk-heat-inherent-2-2')).toHaveAccessibleName(
      'Likelihood 2, impact 2, Low: 0 risks, 0 project risks',
    );
  });

  it('puts the highest likelihood on top and impact 1 first', async () => {
    await render(<HeatMap kind="inherent" cells={cells()} bands={bands} />);
    const order = screen.getAllByRole('gridcell').map((c) => c.getAttribute('data-testid'));
    expect(order[0]).toBe('risk-heat-inherent-5-1');
    expect(order[4]).toBe('risk-heat-inherent-5-5');
    expect(order[24]).toBe('risk-heat-inherent-1-5');
  });

  it('reports the clicked cell', async () => {
    const onSelect = vi.fn();
    await render(<HeatMap kind="residual" cells={cells()} bands={bands} onSelect={onSelect} />);
    await userEvent.click(screen.getByTestId('risk-heat-residual-4-5'));
    expect(onSelect).toHaveBeenCalledWith(4, 5);
  });

  it('labels cells in Arabic with the Arabic band name', async () => {
    await render(<HeatMap kind="residual" cells={cells()} bands={bands} />, 'ar');
    expect(screen.getByTestId('risk-heat-residual-4-5').getAttribute('aria-label')).toContain('حرج');
  });
});

describe('RiskMatrix', () => {
  afterEach(() => vi.restoreAllMocks());

  it('selects a cell and marks it checked', async () => {
    const onChange = vi.fn();
    const matrix = cells().map(({ likelihood, impact, score, band }) => ({
      likelihood,
      impact,
      score,
      band,
    }));
    await render(
      <RiskMatrix
        cells={matrix}
        bands={bands}
        prefix="inherent"
        label="Inherent"
        value={{ likelihood: 3, impact: 4 }}
        onChange={onChange}
      />,
    );
    expect(screen.getByTestId('risk-matrix-inherent-3-4')).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByTestId('risk-matrix-inherent-3-4')).toHaveAccessibleName(
      'Likelihood 3, impact 4: score 12, High',
    );
    await userEvent.click(screen.getByTestId('risk-matrix-inherent-5-5'));
    expect(onChange).toHaveBeenCalledWith(5, 5);
  });
});
