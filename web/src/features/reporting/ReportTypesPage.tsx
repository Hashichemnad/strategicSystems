import { Plus, Save, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  useAdminReportTypes,
  useReportable,
  useSaveReportType,
  type ReportTypeAdmin,
  type ReportTypeNode,
} from './api2';

const aliases = ['a', 'b', 'c', 'd', 'e', 'f'];

interface Draft {
  id?: string;
  version?: number;
  code: string;
  labelAr: string;
  labelEn: string;
  descriptionAr: string;
  descriptionEn: string;
  active: boolean;
  nodes: ReportTypeNode[];
}

const empty: Draft = {
  code: '',
  labelAr: '',
  labelEn: '',
  descriptionAr: '',
  descriptionEn: '',
  active: true,
  nodes: [],
};

function toDraft(t: ReportTypeAdmin): Draft {
  return {
    id: t.id,
    version: t.version,
    code: t.code,
    labelAr: t.labelAr,
    labelEn: t.labelEn,
    descriptionAr: t.descriptionAr ?? '',
    descriptionEn: t.descriptionEn ?? '',
    active: t.active,
    nodes: (t.definition.nodes ?? []).map((n) => ({ ...n })),
  };
}

/**
 * Settings → Reporting → Report types (SPEC-ADDENDUM E1, ADR 0181): the primary object, related objects along declared relationships (also across
 * systems), "with" / "with or without", and the fields offered. Seeded types can be edited (a later seed then leaves them alone).
 */
export function ReportTypesPage() {
  const { t, i18n } = useTranslation('reportBuilder');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const types = useAdminReportTypes();
  const metadata = useReportable('types');
  const [draft, setDraft] = useState<Draft | null>(null);
  const save = useSaveReportType(draft?.id);
  const datasets = useMemo(
    () => new Map((metadata.data?.datasets ?? []).map((d) => [d.code, d])),
    [metadata.data],
  );
  const text = (ar: string, en: string) => pickText(i18n.language, ar, en);
  const datasetLabel = (code: string) => {
    const d = datasets.get(code);
    return d ? text(d.labelAr, d.labelEn) : code;
  };

  if (types.isPending || metadata.isPending) return <Skeleton className="h-96" />;
  const nodes = draft?.nodes ?? [];
  const setNodes = (next: ReportTypeNode[]) => draft && setDraft({ ...draft, nodes: next });
  const updateNode = (alias: string, patch: Partial<ReportTypeNode>) =>
    setNodes(nodes.map((n) => (n.alias === alias ? { ...n, ...patch } : n)));
  const depth = (alias: string | null | undefined): number => {
    const node = nodes.find((n) => n.alias === alias);
    return node?.parent ? 1 + depth(node.parent) : 0;
  };
  return (
    <div className="flex flex-col gap-6" data-testid="report-types-page">
      <PageHeader
        title={t('types.title')}
        description={t('types.description')}
        actions={
          <Button onClick={() => setDraft({ ...empty })} data-testid="report-type-new">
            <Plus className="size-4" aria-hidden />
            {t('types.new')}
          </Button>
        }
      />
      <div className="grid gap-6 xl:grid-cols-5">
        <div className="xl:col-span-2">
          <Table data-testid="report-type-list">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('types.label')}</TableHeaderCell>
                <TableHeaderCell>{t('types.objects')}</TableHeaderCell>
                <TableHeaderCell>{t('types.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(types.data ?? []).map((type) => (
                <TableRow key={type.id} aria-selected={draft?.id === type.id}>
                  <TableCell>
                    <button
                      type="button"
                      className="text-start text-primary hover:underline"
                      onClick={() => setDraft(toDraft(type))}
                    >
                      {text(type.labelAr, type.labelEn)}
                    </button>
                    <span className="block text-xs text-muted-foreground" dir="ltr">
                      {type.code}
                    </span>
                  </TableCell>
                  <TableCell className="text-sm">
                    {(type.definition.nodes ?? []).map((n) => datasetLabel(n.dataset ?? '')).join(' → ')}
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      <StatusChip tone={type.active ? 'success' : 'neutral'}>
                        {type.active ? t('types.active') : t('types.inactive')}
                      </StatusChip>
                      {type.seeded && <StatusChip tone="info">{t('types.seeded')}</StatusChip>}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        {draft && (
          <section
            className="flex flex-col gap-4 rounded-lg border bg-card p-4 xl:col-span-3"
            aria-label={t('types.editor')}
            data-testid="report-type-editor"
          >
            <div className="grid gap-3 sm:grid-cols-2">
              <FormField label={t('types.code')}>
                <Input
                  dir="ltr"
                  value={draft.code}
                  disabled={!!draft.id}
                  onChange={(e) => setDraft({ ...draft, code: e.target.value })}
                />
              </FormField>
              <Switch
                label={t('types.active')}
                checked={draft.active}
                onCheckedChange={(active) => setDraft({ ...draft, active })}
              />
              <FormField label={t('types.labelAr')}>
                <Input
                  dir="rtl"
                  value={draft.labelAr}
                  onChange={(e) => setDraft({ ...draft, labelAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('types.labelEn')}>
                <Input
                  dir="ltr"
                  value={draft.labelEn}
                  onChange={(e) => setDraft({ ...draft, labelEn: e.target.value })}
                />
              </FormField>
            </div>
            <FormField label={t('types.primary')}>
              <Select
                aria-label={t('types.primary')}
                value={nodes[0]?.dataset ?? undefined}
                onValueChange={(v) => setNodes([{ alias: 'a', dataset: v, join: 'outer', fields: null }])}
                placeholder={t('types.choosePrimary')}
                options={(metadata.data?.datasets ?? [])
                  .filter((d) => !d.joinOnly)
                  .map((d) => ({ value: d.code, label: `${text(d.labelAr, d.labelEn)} (${d.systemCode})` }))}
              />
            </FormField>
            {nodes.map((node, index) => {
              const dataset = datasets.get(node.dataset ?? '');
              const relationships = (metadata.data?.relationships ?? []).filter(
                (r) => r.parentDataset === nodes.find((n) => n.alias === node.parent)?.dataset,
              );
              const offered = (dataset?.fields ?? []).filter((f) => f.type !== 'reference');
              return (
                <div
                  key={node.alias}
                  className="flex flex-col gap-2 rounded border p-3"
                  style={{ marginInlineStart: `${depth(node.alias) * 1.25}rem` }}
                  data-testid={`report-type-node-${node.alias}`}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-medium">
                      <span className="me-2 rounded bg-muted px-1.5 font-mono text-xs" dir="ltr">
                        {node.alias}
                      </span>
                      {datasetLabel(node.dataset ?? '')}
                    </span>
                    {index > 0 && (
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => {
                          const removed = new Set([node.alias]);
                          for (const n of nodes)
                            if (n.parent && removed.has(n.parent)) removed.add(n.alias ?? '');
                          setNodes(nodes.filter((n) => !removed.has(n.alias ?? '')));
                        }}
                      >
                        <Trash2 className="size-4" aria-hidden />
                        {t('types.removeObject')}
                      </Button>
                    )}
                  </div>
                  {index > 0 && (
                    <div className="grid gap-2 sm:grid-cols-3">
                      <Select
                        aria-label={t('types.parent')}
                        value={node.parent ?? undefined}
                        onValueChange={(v) =>
                          updateNode(node.alias ?? '', { parent: v, relationship: null, dataset: '' })
                        }
                        options={nodes.slice(0, index).map((n) => ({
                          value: n.alias ?? '',
                          label: `${n.alias} · ${datasetLabel(n.dataset ?? '')}`,
                        }))}
                      />
                      <Select
                        aria-label={t('types.relationship')}
                        value={node.relationship ?? undefined}
                        placeholder={t('types.chooseRelationship')}
                        onValueChange={(v) => {
                          const r = relationships.find((x) => x.code === v);
                          updateNode(node.alias ?? '', {
                            relationship: v,
                            dataset: r?.childDataset ?? '',
                            fields: null,
                          });
                        }}
                        options={relationships.map((r) => ({
                          value: r.code,
                          label: `${text(r.labelAr, r.labelEn)} → ${datasetLabel(r.childDataset)}`,
                        }))}
                      />
                      <Select
                        aria-label={t('types.join')}
                        value={node.join ?? 'outer'}
                        onValueChange={(v) => updateNode(node.alias ?? '', { join: v })}
                        options={[
                          { value: 'inner', label: t('builder.with') },
                          { value: 'outer', label: t('builder.withOrWithout') },
                        ]}
                      />
                    </div>
                  )}
                  {dataset && (
                    <details>
                      <summary className="cursor-pointer text-sm">
                        {t('types.fields', { n: node.fields?.length ?? offered.length })}
                      </summary>
                      <div className="mt-2 grid gap-1 sm:grid-cols-2">
                        {offered.map((f) => (
                          <Checkbox
                            key={f.code}
                            label={
                              text(f.labelAr, f.labelEn) + (f.permission ? ` · ${t('types.restricted')}` : '')
                            }
                            checked={node.fields ? node.fields.includes(f.code) : true}
                            onChange={(e) => {
                              const current = node.fields ?? offered.map((x) => x.code);
                              const next = e.target.checked
                                ? [...current, f.code]
                                : current.filter((c) => c !== f.code);
                              updateNode(node.alias ?? '', {
                                fields: next.length === offered.length ? null : next,
                              });
                            }}
                          />
                        ))}
                      </div>
                    </details>
                  )}
                </div>
              );
            })}
            {nodes.length > 0 && nodes.length < 6 && (
              <Button
                size="sm"
                variant="outline"
                className="self-start"
                data-testid="report-type-add-object"
                onClick={() =>
                  setNodes([
                    ...nodes,
                    {
                      alias: aliases.find((a) => !nodes.some((n) => n.alias === a)) ?? 'z',
                      parent: nodes[nodes.length - 1]?.alias,
                      dataset: '',
                      join: 'outer',
                      fields: null,
                    },
                  ])
                }
              >
                <Plus className="size-4" aria-hidden />
                {t('types.addObject')}
              </Button>
            )}
            <p className="text-xs text-muted-foreground">{t('types.hint')}</p>
            <Button
              className="self-start"
              disabled={save.isPending || !draft.code || nodes.length === 0}
              data-testid="report-type-save"
              onClick={() =>
                save.mutate(
                  {
                    input: {
                      code: draft.code.trim(),
                      systemCode: datasets.get(nodes[0]?.dataset ?? '')?.systemCode ?? '',
                      labelAr: draft.labelAr,
                      labelEn: draft.labelEn,
                      descriptionAr: draft.descriptionAr || null,
                      descriptionEn: draft.descriptionEn || null,
                      definition: { nodes },
                      active: draft.active,
                    },
                    version: draft.version,
                  },
                  {
                    onSuccess: (saved) => {
                      toast({ title: t('types.saved') });
                      setDraft(toDraft(saved));
                    },
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              <Save className="size-4" aria-hidden />
              {t('types.save')}
            </Button>
          </section>
        )}
      </div>
    </div>
  );
}
