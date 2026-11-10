import { useRouterState } from '@tanstack/react-router';
import { useEffect, useMemo, useRef } from 'react';
import { useRecordVisit, useSearchTypes, type SearchType } from './api';

const guid = '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Compiles the link templates (e.g. `/risk/risks/{id}`) into matchers for record-page paths. */
export function recordMatchers(types: SearchType[]): { type: string; pattern: RegExp }[] {
  return types
    .filter((t) => t.linkTemplate.includes('{id}'))
    .map((t) => ({
      type: t.type,
      pattern: new RegExp(`^${t.linkTemplate.split('{id}').map(escapeRegExp).join(guid)}/?$`),
    }));
}

/** The record a path shows, if it is a record page of a linkable type. */
export function matchRecordPath(
  pathname: string,
  matchers: { type: string; pattern: RegExp }[],
): { type: string; id: string } | null {
  for (const matcher of matchers) {
    const match = matcher.pattern.exec(pathname);
    if (match?.[1]) return { type: matcher.type, id: match[1].toLowerCase() };
  }
  return null;
}

/**
 * Records every record-page visit as a recent item (ADR 0160), centrally in the app shell: the path is matched against the link
 * templates of the linkable types. The server stores a visit only when the record is visible to the user.
 */
export function useRecordVisits(): void {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const types = useSearchTypes();
  const visit = useRecordVisit();
  const matchers = useMemo(() => recordMatchers(types.data ?? []), [types.data]);
  const last = useRef<string | null>(null);
  const { mutate } = visit;

  useEffect(() => {
    const record = matchRecordPath(pathname, matchers);
    if (!record) return;
    const key = `${record.type}:${record.id}`;
    if (last.current === key) return;
    last.current = key;
    mutate(record);
  }, [pathname, matchers, mutate]);
}
