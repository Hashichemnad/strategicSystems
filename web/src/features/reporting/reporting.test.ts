import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import { flowLayout } from './DashboardBuilderPage';
import { moveItem } from './MyWorkCards';
import { operatorsFor } from './ReportBuilderPage';
import { formatValue, toRag } from './WidgetRenderer';

/** Every reporting message key the server can return (errors, My Work cards, counts and items) from the C# sources. */
function serverKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(/"(reporting\.(?:errors|myWork\.items)\.[A-Za-z]+)"/g))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src/Platform');
  walk('../src/Modules');
  return [...keys].sort();
}

/** Card titles and count keys are relative to the reporting namespace (`myWork.cards.*`, count `myWork.counts.<key>`). */
function cardKeys(): string[] {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs')) {
        const text = readFileSync(path, 'utf8');
        if (!text.includes('IMyWorkCardSource')) continue;
        for (const match of text.matchAll(/"(myWork\.cards\.[A-Za-z-]+)"/g)) if (match[1]) keys.add(`reporting.${match[1]}`);
        for (const match of text.matchAll(/new MyWorkCount\("([A-Za-z]+)"/g)) if (match[1]) keys.add(`reporting.myWork.counts.${match[1]}`);
      }
    }
  };
  walk('../src/Platform');
  walk('../src/Modules');
  return [...keys].sort();
}

describe('Reporting message keys', () => {
  const keys = [...serverKeys(), ...cardKeys()];

  it('finds the keys', () => {
    expect(keys).toContain('reporting.errors.fieldNotAllowed');
    expect(keys).toContain('reporting.myWork.cards.approvals');
    expect(keys).toContain('reporting.myWork.counts.overdue');
  });

  it.each(['en', 'ar'] as const)('are all translated in %s', async (language) => {
    await i18n.changeLanguage(language);
    const fallback = translateMessageKey(i18n.t, 'nope.unknownKey');
    const missing = keys.filter((key) => translateMessageKey(i18n.t, key) === fallback);
    expect(missing).toEqual([]);
  });
});

describe('My Work layout', () => {
  it('moves a card and leaves the list unchanged for invalid moves', () => {
    expect(moveItem(['a', 'b', 'c'], 0, 2)).toEqual(['b', 'c', 'a']);
    expect(moveItem(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b']);
    expect(moveItem(['a', 'b'], 0, 5)).toEqual(['a', 'b']);
  });
});

describe('Dashboard builder layout', () => {
  it('flows widgets onto the 12-column grid in reading order', () => {
    const widgets = flowLayout([
      { id: 'a', type: 'tile', x: 0, y: 0, w: 3, h: 2 },
      { id: 'b', type: 'tile', x: 0, y: 0, w: 6, h: 2 },
      { id: 'c', type: 'bar', x: 0, y: 0, w: 6, h: 4 },
      { id: 'd', type: 'bar', x: 0, y: 0, w: 12, h: 3 },
    ] as never);
    expect(widgets.map((w) => [w.x, w.y])).toEqual([
      [0, 0],
      [3, 0],
      [0, 2],
      [0, 6],
    ]);
  });
});

describe('Report builder', () => {
  it('offers only the operators the server accepts for a field type', () => {
    expect(operatorsFor('text')).toContain('contains');
    expect(operatorsFor('money')).not.toContain('contains');
    expect(operatorsFor('lookup')).toContain('in');
    expect(operatorsFor('bool')).toEqual(['eq']);
  });
});

describe('Widget values', () => {
  it('formats ratios as percentages and counts as numbers in both languages', () => {
    expect(formatValue(0.875, 'ratio', 'en')).toBe('87.5 %');
    expect(formatValue(1234, 'count', 'en')).toBe('1,234');
    expect(formatValue(null, 'count', 'en')).toBe('—');
    expect(formatValue(0.5, 'score', 'ar')).toContain('%');
  });

  it('maps server RAG codes to the kit', () => {
    expect(toRag('green')).toBe('green');
    expect(toRag('none')).toBe('grey');
    expect(toRag(undefined)).toBeUndefined();
  });
});
