import { describe, expect, it } from 'vitest';
import { findViolations, isLicenseAllowed } from './lib/licenses.mjs';
import { findPhysicalClasses } from './lib/rtl-classes.mjs';

describe('RTL class guard', () => {
  it.each([
    'ml-2',
    'pr-4',
    '-mr-1',
    'left-0',
    'right-3',
    'text-left',
    'border-r',
    'rounded-tl-lg',
    'float-right',
  ])('flags physical class %s', (cls) => {
    expect(findPhysicalClasses(`<div className="flex ${cls} gap-2" />`).map((f) => f.match)).toEqual([cls]);
  });

  it.each(['ms-2', 'pe-4', 'start-0', 'end-3', 'text-start', 'border-e', 'rounded-ss-lg', 'rtl:rotate-180'])(
    'accepts logical class %s',
    (cls) => {
      expect(findPhysicalClasses(`<div className="${cls}" />`)).toEqual([]);
    },
  );

  it('ignores identifiers outside strings and lines marked rtl-ok', () => {
    expect(findPhysicalClasses('const marginLeft = left-right;')).toEqual([]);
    expect(findPhysicalClasses('<svg className="left-0" /> // rtl-ok: chart axis is always LTR')).toEqual([]);
  });
});

describe('licence guard', () => {
  it.each([
    ['MIT', true],
    ['(MIT OR GPL-3.0)', true],
    ['MIT AND GPL-3.0', false],
    ['GPL-3.0', false],
    ['SEE LICENSE IN LICENSE.txt', false],
    [undefined, false],
  ])('%s allowed = %s', (expression, allowed) => {
    expect(isLicenseAllowed(expression)).toBe(allowed);
  });

  it('accepts data / tooling licences only for dev dependencies', () => {
    expect(isLicenseAllowed('CC-BY-4.0', { dev: true })).toBe(true);
    expect(isLicenseAllowed('CC-BY-4.0')).toBe(false);
  });

  it('flags banned and unlicensed packages', () => {
    const violations = findViolations({
      packages: {
        '': { name: 'strategy-web' },
        'node_modules/react': { version: '19.0.0', license: 'MIT' },
        'node_modules/ag-grid-enterprise': { version: '1.0.0', license: 'MIT' },
        'node_modules/mystery': { version: '1.0.0' },
      },
    });
    expect(violations).toHaveLength(2);
    expect(violations[0]).toContain('banned');
    expect(violations[1]).toContain('(none)');
  });
});
