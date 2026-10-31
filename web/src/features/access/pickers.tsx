import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { useEffect, useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { useUnitTree } from '@/features/org/api';
import { pickText } from '@/lib/format';
import { Button, Input, Select } from '@/ui';

export type Principal = components['schemas']['PrincipalDto'];

/** Display name of a user / unit / everyone principal in the user's language. */
export function usePrincipalName() {
  const { t, i18n } = useTranslation('access');
  return (principal: Principal) => {
    if (principal.kind === 'everyone') return t('principals.everyone');
    const name = pickText(i18n.language, principal.nameAr, principal.nameEn);
    if (principal.kind === 'unit')
      return name ? `${name} (${principal.detail ?? ''})` : (principal.detail ?? '—');
    return name ? `${name} · ${principal.detail ?? ''}` : (principal.detail ?? '—');
  };
}

/** Select of the current units, indented by depth (for unit principals and owning units). */
export function UnitSelect(props: {
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  id?: string;
  allowEmpty?: boolean;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
}) {
  const { t, i18n } = useTranslation('access');
  const tree = useUnitTree();
  const options = useMemo(
    () => [
      ...(props.allowEmpty ? [{ value: '-', label: t('pickers.noUnit') }] : []),
      ...(tree.data ?? []).map((node) => ({
        value: node.id,
        label: `${'  '.repeat(node.depth)}${pickText(i18n.language, node.nameAr, node.nameEn)} (${node.code})`,
      })),
    ],
    [tree.data, i18n.language, props.allowEmpty, t],
  );
  return (
    <Select
      id={props.id}
      value={props.value ?? (props.allowEmpty ? '-' : undefined)}
      onValueChange={(value) => props.onChange(value === '-' ? undefined : value)}
      options={options}
      placeholder={t('pickers.unit')}
      aria-invalid={props['aria-invalid']}
      aria-describedby={props['aria-describedby']}
    />
  );
}

/** Search-as-you-type user chooser (user name, e-mail or employee name; Arabic spelling variants match). */
export function UserPicker({
  value,
  onChange,
  id,
  ...aria
}: {
  value: Principal | undefined;
  onChange: (value: Principal | undefined) => void;
  id?: string;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
}) {
  const { t } = useTranslation('access');
  const name = usePrincipalName();
  const listId = useId();
  const [text, setText] = useState('');
  const [debounced, setDebounced] = useState('');
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [text]);
  const results = useQuery({
    queryKey: ['access', 'users', debounced],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/access/users', { params: { query: { search: debounced } }, signal })),
    enabled: open && debounced.length > 0,
  });

  if (value)
    return (
      <div className="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm">
        <span>{name(value)}</span>
        <Button
          variant="ghost"
          size="icon"
          className="size-7"
          aria-label={t('pickers.clear')}
          onClick={() => onChange(undefined)}
        >
          <X aria-hidden />
        </Button>
      </div>
    );
  return (
    <div className="relative">
      <Input
        id={id}
        role="combobox"
        aria-expanded={open && !!results.data?.length}
        aria-controls={listId}
        aria-autocomplete="list"
        placeholder={t('pickers.user')}
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          setOpen(true);
        }}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        {...aria}
      />
      {open && !!results.data?.length && (
        <ul
          id={listId}
          role="listbox"
          className="absolute inset-x-0 top-full z-20 mt-1 max-h-64 overflow-auto rounded-md border bg-card shadow-lg"
        >
          {results.data.map((user) => (
            <li
              key={user.id}
              role="option"
              aria-selected={false}
              className="cursor-pointer px-3 py-2 text-sm hover:bg-muted"
              onMouseDown={(event) => {
                event.preventDefault();
                onChange(user);
                setText('');
                setOpen(false);
              }}
            >
              {name(user)}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function useSystems() {
  return useQuery({
    queryKey: ['access', 'systems'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/systems', { signal })),
    staleTime: 60_000,
  });
}
