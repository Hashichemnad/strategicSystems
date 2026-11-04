import { describe, expect, it } from 'vitest';
import { ApiError } from '@/api/client';
import type { FormSchema } from './api';
import {
  fieldErrorsOf,
  holds,
  initialValues,
  isVisible,
  normalise,
  otherErrors,
  parsePrefill,
  sectionOfError,
} from './formLogic';

const schema: FormSchema = {
  sections: [
    {
      code: 'main',
      fields: [
        { code: 'kind', type: 'picklist', lookupType: 'k' },
        { code: 'amount', type: 'currency', visibleWhen: { field: 'kind', op: 'eq', value: 'paid' } },
        { code: 'agree', type: 'checkbox' },
      ],
    },
    {
      code: 'more',
      fields: [
        {
          code: 'rows',
          type: 'table',
          minRows: 2,
          columns: [
            { code: 'name', type: 'text' },
            { code: 'share', type: 'number' },
          ],
        },
      ],
    },
  ],
};

describe('conditions (same rules as the server)', () => {
  it.each([
    [{ field: 'a', op: 'eq', value: 'x' }, { a: 'x' }, true],
    [{ field: 'a', op: 'eq', value: 5 }, { a: '5' }, true],
    [{ field: 'a', op: 'eq', value: 'true' }, { a: true }, true],
    [{ field: 'a', op: 'ne', value: 'x' }, { a: 'y' }, true],
    [{ field: 'a', op: 'ne', value: 'x' }, {}, true],
    [{ field: 'a', op: 'in', value: ['x', 'y'] }, { a: 'y' }, true],
    [{ field: 'a', op: 'in', value: ['x'] }, { a: 'z' }, false],
    [{ field: 'a', op: 'filled' }, { a: '  ' }, false],
    [{ field: 'a', op: 'filled' }, { a: ['x'] }, true],
    [{ field: 'a', op: 'empty' }, { a: [] }, true],
    [{ field: 'a', op: 'eq', value: '' }, {}, true],
    [{ field: 'a', op: 'bogus' }, { a: 'x' }, false],
  ])('%o on %o → %s', (condition, values, expected) => {
    expect(holds(condition, values)).toBe(expected);
  });

  it('hides a field whose condition does not hold', () => {
    const amount = schema.sections?.[0]?.fields?.[1];
    expect(amount && isVisible(amount, { kind: 'free' })).toBe(false);
    expect(amount && isVisible(amount, { kind: 'paid' })).toBe(true);
  });
});

describe('normalise', () => {
  it('drops hidden and empty values, types numbers, recurses into tables', () => {
    expect(
      normalise(schema, {
        kind: 'free',
        amount: '12.50',
        agree: false,
        rows: [{ name: 'A', share: '0.5' }, { name: '' }],
        stray: 'x',
      }),
    ).toEqual({ kind: 'free', agree: false, rows: [{ name: 'A', share: 0.5 }, {}] });
    expect(normalise(schema, { kind: 'paid', amount: '12.50' })).toEqual({ kind: 'paid', amount: 12.5 });
  });

  it('starts tables with their minimum rows and keeps prefill', () => {
    expect(initialValues(schema, { kind: 'paid' })).toEqual({ kind: 'paid', rows: [{}, {}] });
  });
});

describe('server errors', () => {
  const error = new ApiError(400, {
    status: 400,
    code: 'validation',
    messageKey: 'errors.validation',
    errors: [
      { field: 'fields.rows[1].share', code: 'x', messageKey: 'requests.errors.number' },
      { field: 'form.level', code: 'x', messageKey: 'validation.notEmpty' },
      { field: 'title', code: 'x', messageKey: 'requests.errors.notAllowed' },
    ],
  });

  it('maps field paths relative to the form and finds the section', () => {
    const fields = fieldErrorsOf(error, 'fields');
    expect(Object.keys(fields)).toEqual(['rows[1].share']);
    expect(sectionOfError(schema, fields)).toBe(1);
    expect(Object.keys(fieldErrorsOf(error, 'form'))).toEqual(['level']);
    expect(otherErrors(error, ['fields', 'form']).map((e) => e.messageKey)).toEqual([
      'requests.errors.notAllowed',
    ]);
  });

  it('ignores anything that is not an API error', () => {
    expect(fieldErrorsOf(new Error('x'), 'fields')).toEqual({});
  });
});

describe('prefill', () => {
  it('accepts a JSON object only', () => {
    expect(parsePrefill('{"documentId":"a"}')).toEqual({ documentId: 'a' });
    expect(parsePrefill('[1]')).toEqual({});
    expect(parsePrefill('nope')).toEqual({});
    expect(parsePrefill(undefined)).toEqual({});
  });
});
