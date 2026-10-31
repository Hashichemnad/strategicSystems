import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import type { components } from '@/api/schema';
import { resources } from '@/i18n';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { useServerErrors } from './common';
import { translationOverridesKey, translationOverridesQuery } from './publicSettings';

type LookupType = components['schemas']['LookupTypeDto'];
type LookupValue = components['schemas']['LookupValueDto'];

// ---- Lists (lookups) ---------------------------------------------------------------------------------------

/** Settings → Lists: every picklist with AR/EN labels, order, active flag and parent value (SPEC §0.2). */
export function LookupsPage() {
  const { t } = useTranslation('settings');
  const { i18n } = useTranslation();
  const types = useQuery({
    queryKey: ['settings', 'lookup-types'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/lookups/types', { signal })),
  });
  const [selected, setSelected] = useState<string>();
  const [creatingType, setCreatingType] = useState(false);
  const type = types.data?.find((x) => x.code === (selected ?? types.data[0]?.code));
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('pages.lookups.title')}
        description={t('pages.lookups.description')}
        actions={
          <Button variant="outline" onClick={() => setCreatingType(true)}>
            <Plus aria-hidden />
            {t('lookups.newType')}
          </Button>
        }
      />
      {!types.data ? (
        <Skeleton className="h-64" />
      ) : (
        <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
          <ul className="flex flex-col gap-1" aria-label={t('lookups.types')}>
            {types.data.map((item) => (
              <li key={item.code}>
                <button
                  type="button"
                  aria-current={item.code === type?.code ? 'true' : undefined}
                  className="flex w-full items-center justify-between gap-2 rounded-md px-3 py-2 text-start text-sm hover:bg-muted aria-[current]:bg-accent aria-[current]:text-accent-foreground"
                  onClick={() => setSelected(item.code)}
                >
                  <span>
                    <span className="block font-medium">
                      {pickText(i18n.language, item.nameAr, item.nameEn)}
                    </span>
                    <span className="block text-xs text-muted-foreground" dir="ltr">
                      {item.code}
                    </span>
                  </span>
                  <StatusChip tone="neutral">{item.valueCount}</StatusChip>
                </button>
              </li>
            ))}
          </ul>
          {type && <LookupValues type={type} types={types.data} />}
        </div>
      )}
      {creatingType && (
        <LookupTypeDialog
          types={types.data ?? []}
          onClose={() => setCreatingType(false)}
          onCreated={setSelected}
        />
      )}
    </div>
  );
}

function LookupValues({ type, types }: { type: LookupType; types: LookupType[] }) {
  const { t } = useTranslation('settings');
  const { i18n } = useTranslation();
  const values = useQuery({
    queryKey: ['settings', 'lookups', type.code],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/settings/lookups/{type}/values', {
          params: { path: { type: type.code } },
          signal,
        }),
      ),
  });
  const parentValues = useQuery({
    queryKey: ['settings', 'lookups', type.parentTypeCode],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/settings/lookups/{type}/values', {
          params: { path: { type: type.parentTypeCode ?? '' } },
          signal,
        }),
      ),
    enabled: !!type.parentTypeCode,
  });
  const [editing, setEditing] = useState<LookupValue | 'new'>();
  const parentName = (id: string | null) => {
    const parent = parentValues.data?.find((v) => v.id === id);
    return parent ? pickText(i18n.language, parent.labelAr, parent.labelEn) : '—';
  };
  const parentType = types.find((x) => x.code === type.parentTypeCode);
  return (
    <section className="flex flex-col gap-4" aria-labelledby="lookup-values">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="lookup-values" className="text-lg font-semibold">
          {t('lookups.valuesOf', { name: pickText(i18n.language, type.nameAr, type.nameEn) })}
        </h2>
        <Button onClick={() => setEditing('new')}>
          <Plus aria-hidden />
          {t('lookups.newValue')}
        </Button>
      </div>
      {values.data?.length === 0 ? (
        <EmptyState title={t('lookups.empty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('lookups.code')}</TableHeaderCell>
              <TableHeaderCell>{t('lookups.labelAr')}</TableHeaderCell>
              <TableHeaderCell>{t('lookups.labelEn')}</TableHeaderCell>
              {parentType && (
                <TableHeaderCell>
                  {pickText(i18n.language, parentType.nameAr, parentType.nameEn)}
                </TableHeaderCell>
              )}
              <TableHeaderCell>{t('lookups.sort')}</TableHeaderCell>
              <TableHeaderCell>{t('lookups.state')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('lookups.edit')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {values.data?.map((value) => (
              <TableRow key={value.id}>
                <TableCell dir="ltr" className="font-mono text-xs">
                  {value.code}
                </TableCell>
                <TableCell dir="rtl">{value.labelAr ?? '—'}</TableCell>
                <TableCell dir="ltr">{value.labelEn ?? '—'}</TableCell>
                {parentType && <TableCell>{parentName(value.parentValueId)}</TableCell>}
                <TableCell>{value.sort}</TableCell>
                <TableCell>
                  <StatusChip tone={value.active ? 'success' : 'neutral'}>
                    {value.active ? t('lookups.active') : t('lookups.inactive')}
                  </StatusChip>
                </TableCell>
                <TableCell className="text-end">
                  <Button variant="ghost" size="sm" onClick={() => setEditing(value)}>
                    {t('lookups.edit')}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && (
        <LookupValueDialog
          type={type}
          value={editing === 'new' ? undefined : editing}
          parents={parentValues.data ?? []}
          onClose={() => setEditing(undefined)}
        />
      )}
    </section>
  );
}

function LookupValueDialog({
  type,
  value,
  parents,
  onClose,
}: {
  type: LookupType;
  value: LookupValue | undefined;
  parents: LookupValue[];
  onClose: () => void;
}) {
  const { t } = useTranslation('settings');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const [code, setCode] = useState(value?.code ?? '');
  const [labelAr, setLabelAr] = useState(value?.labelAr ?? '');
  const [labelEn, setLabelEn] = useState(value?.labelEn ?? '');
  const [sort, setSort] = useState(value?.sort ?? 0);
  const [active, setActive] = useState(value?.active ?? true);
  const [parentValueId, setParentValueId] = useState(value?.parentValueId ?? undefined);
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        labelAr: labelAr || null,
        labelEn: labelEn || null,
        sort,
        active,
        parentValueId: parentValueId ?? null,
        metadata: value?.metadata ?? null,
      };
      return value
        ? unwrap(
            await api.PUT('/api/v1/settings/lookups/values/{id}', {
              params: { path: { id: value.id } },
              body: { ...body, version: value.version },
            }),
          )
        : unwrap(
            await api.POST('/api/v1/settings/lookups/{type}/values', {
              params: { path: { type: type.code } },
              body: { ...body, code },
            }),
          );
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['settings'] });
      toast({ title: t('saved') });
      onClose();
    },
    onError: errors.fail,
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={value ? t('lookups.editValue') : t('lookups.newValue')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={() => save.mutate()}>
              {t('save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField
            label={t('lookups.code')}
            error={errors.errorFor('code')}
            hint={t('lookups.codeHint')}
            required
          >
            <Input dir="ltr" value={code} disabled={!!value} onChange={(e) => setCode(e.target.value)} />
          </FormField>
          <FormField label={t('lookups.labelAr')} error={errors.errorFor('labelAr')}>
            <Input dir="rtl" value={labelAr} onChange={(e) => setLabelAr(e.target.value)} />
          </FormField>
          <FormField label={t('lookups.labelEn')} error={errors.errorFor('labelEn')}>
            <Input dir="ltr" value={labelEn} onChange={(e) => setLabelEn(e.target.value)} />
          </FormField>
          {type.parentTypeCode && (
            <FormField label={t('lookups.parent')} error={errors.errorFor('parentValueId')}>
              <Select
                value={parentValueId ?? '-'}
                onValueChange={(id) => setParentValueId(id === '-' ? undefined : id)}
                options={[
                  { value: '-', label: '—' },
                  ...parents.map((p) => ({
                    value: p.id,
                    label: pickText(i18n.language, p.labelAr, p.labelEn),
                  })),
                ]}
              />
            </FormField>
          )}
          <FormField label={t('lookups.sort')}>
            <Input type="number" dir="ltr" value={sort} onChange={(e) => setSort(Number(e.target.value))} />
          </FormField>
          <Checkbox
            label={t('lookups.active')}
            checked={active}
            onChange={(e) => setActive(e.target.checked)}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function LookupTypeDialog({
  types,
  onClose,
  onCreated,
}: {
  types: LookupType[];
  onClose: () => void;
  onCreated: (code: string) => void;
}) {
  const { t } = useTranslation('settings');
  const { t: tc, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const errors = useServerErrors();
  const [code, setCode] = useState('');
  const [systemCode, setSystemCode] = useState('');
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [parent, setParent] = useState<string>();
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/v1/settings/lookups/types', {
          body: {
            code,
            systemCode,
            nameAr: nameAr || null,
            nameEn: nameEn || null,
            parentTypeCode: parent ?? null,
          },
        }),
      ),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ['settings', 'lookup-types'] });
      onCreated(created.code);
      onClose();
    },
    onError: errors.fail,
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('lookups.newType')}
        description={t('lookups.newTypeDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={create.isPending} onClick={() => create.mutate()}>
              {tc('actions.create')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField
            label={t('lookups.code')}
            error={errors.errorFor('code')}
            hint={t('lookups.typeCodeHint')}
            required
          >
            <Input dir="ltr" value={code} onChange={(e) => setCode(e.target.value)} />
          </FormField>
          <FormField label={t('lookups.system')} error={errors.errorFor('systemCode')} required>
            <Input dir="ltr" value={systemCode} onChange={(e) => setSystemCode(e.target.value)} />
          </FormField>
          <FormField label={t('lookups.nameAr')} error={errors.errorFor('nameAr')}>
            <Input dir="rtl" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          </FormField>
          <FormField label={t('lookups.nameEn')} error={errors.errorFor('nameEn')}>
            <Input dir="ltr" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
          </FormField>
          <FormField label={t('lookups.parentType')}>
            <Select
              value={parent ?? '-'}
              onValueChange={(value) => setParent(value === '-' ? undefined : value)}
              options={[
                { value: '-', label: '—' },
                ...types.map((x) => ({ value: x.code, label: pickText(i18n.language, x.nameAr, x.nameEn) })),
              ]}
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}

// ---- Numbering ---------------------------------------------------------------------------------------------

/** Settings → Numbering: the format of every human-readable number, with a live preview (CLAUDE.md §6). */
export function NumberingPage() {
  const { t } = useTranslation('settings');
  const { i18n } = useTranslation();
  const formats = useQuery({
    queryKey: ['settings', 'numbering'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/numbering', { signal })),
  });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.numbering.title')} description={t('pages.numbering.description')} />
      <div className="rounded-lg border bg-muted/40 p-4 text-sm">
        <p className="font-medium">{t('numbering.tokens')}</p>
        <ul className="mt-2 grid gap-1 sm:grid-cols-2">
          {(['YYYY', 'YY', 'MM', 'UNIT', 'SEQ'] as const).map((token) => (
            <li key={token}>
              <code
                dir="ltr"
                className="rounded bg-card px-1"
              >{`{${token === 'SEQ' ? 'SEQ:4' : token}}`}</code>{' '}
              — {t(`numbering.token.${token}`)}
            </li>
          ))}
        </ul>
      </div>
      {formats.data?.map((format) => (
        <NumberingFormatCard
          key={format.key}
          format={format}
          name={pickText(i18n.language, format.nameAr, format.nameEn)}
        />
      ))}
    </div>
  );
}

function NumberingFormatCard({
  format,
  name,
}: {
  format: components['schemas']['NumberingFormatDto'];
  name: string;
}) {
  const { t } = useTranslation('settings');
  const queryClient = useQueryClient();
  const toast = useToast();
  const errors = useServerErrors();
  const [pattern, setPattern] = useState(format.pattern);
  const preview = useQuery({
    queryKey: ['settings', 'numbering', 'preview', pattern],
    queryFn: async ({ signal }) =>
      unwrap(await api.POST('/api/v1/settings/numbering/preview', { body: { pattern }, signal })),
    enabled: pattern.length > 0,
    retry: false,
  });
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/v1/settings/numbering/{key}', {
          params: { path: { key: format.key } },
          body: { pattern, version: format.version },
        }),
      ),
    onSuccess: () => {
      errors.clear();
      void queryClient.invalidateQueries({ queryKey: ['settings', 'numbering'] });
      toast({ title: t('saved') });
    },
    onError: errors.fail,
  });
  return (
    <form
      className="flex flex-col gap-4 rounded-lg border bg-card p-5"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="font-semibold">{name}</h2>
        <code dir="ltr" className="text-xs text-muted-foreground">
          {format.key}
        </code>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          label={t('numbering.pattern')}
          error={errors.errorFor('pattern')}
          hint={t('numbering.default', { pattern: format.defaultPattern })}
        >
          <Input
            dir="ltr"
            className="font-mono"
            value={pattern}
            onChange={(e) => setPattern(e.target.value)}
          />
        </FormField>
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium">{t('numbering.preview')}</span>
          <output dir="ltr" className="rounded-md bg-muted px-3 py-2 font-mono text-sm" aria-live="polite">
            {preview.isError ? t('numbering.invalid') : (preview.data?.preview ?? '…')}
          </output>
        </div>
      </div>
      <div className="flex gap-2">
        <Button type="submit" loading={save.isPending} disabled={pattern === format.pattern}>
          {t('save')}
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={() => setPattern(format.defaultPattern)}
          disabled={pattern === format.defaultPattern}
        >
          {t('numbering.resetDefault')}
        </Button>
      </div>
    </form>
  );
}

// ---- Translations ------------------------------------------------------------------------------------------

interface Row {
  namespace: string;
  key: string;
  ar: string;
  en: string;
}

function flatten(tree: unknown, prefix = ''): [string, string][] {
  return Object.entries(tree as Record<string, unknown>).flatMap(([key, value]) =>
    typeof value === 'string'
      ? [[`${prefix}${key}`, value] as [string, string]]
      : flatten(value, `${prefix}${key}.`),
  );
}

/** Every bundled label, AR and EN, so administrators can override any of them (SPEC §0.2 localisation). */
const allRows: Row[] = Object.keys(resources.en).flatMap((namespace) => {
  const ar = Object.fromEntries(flatten(resources.ar[namespace as keyof typeof resources.ar]));
  return flatten(resources.en[namespace as keyof typeof resources.en]).map(([key, en]) => ({
    namespace,
    key,
    en,
    ar: ar[key] ?? '',
  }));
});

/** Settings → Translations: search any label and override it in Arabic and / or English; the change is live. */
export function TranslationsPage() {
  const { t } = useTranslation('settings');
  const overrides = useQuery(translationOverridesQuery);
  const [search, setSearch] = useState('');
  const [onlyOverridden, setOnlyOverridden] = useState(false);
  const [editing, setEditing] = useState<Row>();
  const overrideOf = (language: 'ar' | 'en', row: Row) =>
    overrides.data?.languages[language]?.[row.namespace]?.[row.key];
  const rows = useMemo(() => {
    const term = search.trim().toLowerCase();
    return allRows
      .filter(
        (row) =>
          !onlyOverridden ||
          overrides.data?.languages.ar?.[row.namespace]?.[row.key] ||
          overrides.data?.languages.en?.[row.namespace]?.[row.key],
      )
      .filter(
        (row) => !term || `${row.namespace}.${row.key} ${row.ar} ${row.en}`.toLowerCase().includes(term),
      )
      .slice(0, 100);
  }, [search, onlyOverridden, overrides.data]);
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('pages.translations.title')} description={t('pages.translations.description')} />
      <div className="flex flex-wrap items-center gap-4">
        <div className="relative max-w-sm flex-1">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            type="search"
            className="ps-9"
            placeholder={t('translations.search')}
            aria-label={t('translations.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Checkbox
          label={t('translations.onlyOverridden')}
          checked={onlyOverridden}
          onChange={(e) => setOnlyOverridden(e.target.checked)}
        />
      </div>
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('translations.key')}</TableHeaderCell>
            <TableHeaderCell>{t('translations.arabic')}</TableHeaderCell>
            <TableHeaderCell>{t('translations.english')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{t('lookups.edit')}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={`${row.namespace}:${row.key}`}>
              <TableCell dir="ltr" className="font-mono text-xs">
                {row.namespace}:{row.key}
              </TableCell>
              <TableCell dir="rtl">
                {overrideOf('ar', row) ?? row.ar}
                {overrideOf('ar', row) && (
                  <StatusChip tone="info" className="ms-2">
                    {t('translations.overridden')}
                  </StatusChip>
                )}
              </TableCell>
              <TableCell dir="ltr">
                {overrideOf('en', row) ?? row.en}
                {overrideOf('en', row) && (
                  <StatusChip tone="info" className="ms-2">
                    {t('translations.overridden')}
                  </StatusChip>
                )}
              </TableCell>
              <TableCell className="text-end">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setEditing(row)}
                  aria-label={t('translations.editKey', { key: row.key })}
                >
                  {t('lookups.edit')}
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {editing && (
        <TranslationDialog
          row={editing}
          current={{ ar: overrideOf('ar', editing), en: overrideOf('en', editing) }}
          onClose={() => setEditing(undefined)}
        />
      )}
    </div>
  );
}

function TranslationDialog({
  row,
  current,
  onClose,
}: {
  row: Row;
  current: { ar: string | undefined; en: string | undefined };
  onClose: () => void;
}) {
  const { t } = useTranslation('settings');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [ar, setAr] = useState(current.ar ?? '');
  const [en, setEn] = useState(current.en ?? '');
  const save = useMutation({
    mutationFn: async () => {
      let result;
      for (const [language, value, before] of [
        ['ar', ar, current.ar],
        ['en', en, current.en],
      ] as const) {
        if ((value || undefined) !== before)
          result = unwrap(
            await api.PUT('/api/v1/settings/translations', {
              body: { language, namespace: row.namespace, key: row.key, value: value || null },
            }),
          );
      }
      return result;
    },
    onSuccess: (result) => {
      if (result) queryClient.setQueryData(translationOverridesKey, result);
      toast({ title: t('saved') });
      onClose();
    },
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('translations.edit')}
        description={<span dir="ltr">{`${row.namespace}:${row.key}`}</span>}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={() => save.mutate()}>
              {t('save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <FormField label={t('translations.overrideAr')} hint={t('translations.default', { text: row.ar })}>
            <Input dir="rtl" value={ar} placeholder={row.ar} onChange={(e) => setAr(e.target.value)} />
          </FormField>
          <FormField label={t('translations.overrideEn')} hint={t('translations.default', { text: row.en })}>
            <Input dir="ltr" value={en} placeholder={row.en} onChange={(e) => setEn(e.target.value)} />
          </FormField>
          <p className="text-xs text-muted-foreground">{t('translations.clearHint')}</p>
        </div>
      </DialogContent>
    </Dialog>
  );
}
