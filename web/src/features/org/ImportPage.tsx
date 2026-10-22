import { Download } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import {
  Button,
  FileDrop,
  PageHeader,
  Select,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Wizard,
  useToast,
} from '@/ui';
import { useInvalidateOrg } from './api';

type Report = components['schemas']['ImportReportDto'];
type Kind = 'units' | 'employees';

/** Multipart body for the typed client (the API client middleware still adds antiforgery, correlation and channel). */
function fileForm(file: File) {
  const form = new FormData();
  form.append('file', file);
  return { body: form as unknown as { file: string }, bodySerializer: (body: unknown) => body as FormData };
}

/**
 * Bulk import from the Excel / CSV templates: download template → upload → validation report → commit.
 * Nothing is written until the whole file is valid; units first, then employees, then units again for heads.
 */
export function ImportPage() {
  const { t } = useTranslation('import');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const invalidate = useInvalidateOrg();
  const [kind, setKind] = useState<Kind>('units');
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<Report>();
  const [result, setResult] = useState<Report>();
  const [busy, setBusy] = useState(false);

  async function run(mode: 'validate' | 'commit'): Promise<Report | undefined> {
    if (!file) return undefined;
    setBusy(true);
    try {
      return unwrap(
        await api.POST('/api/v1/org/import/{kind}', {
          params: { path: { kind }, query: { mode } },
          ...fileForm(file),
        }),
      );
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
      return undefined;
    } finally {
      setBusy(false);
    }
  }

  async function downloadReport() {
    if (!file) return;
    const { data: blob } = await api.POST('/api/v1/org/import/{kind}/report', {
      params: { path: { kind } },
      ...fileForm(file),
      parseAs: 'blob',
    });
    if (!blob) return;
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${kind}-validation-report.xlsx`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const issues = report?.issues ?? [];
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('title')} description={t('description')} />
      <div className="rounded-lg border bg-card p-5">
        <Wizard
          busy={busy}
          finishLabel={t('commit')}
          onFinish={async () => {
            const committed = await run('commit');
            if (committed?.committed) {
              setResult(committed);
              await invalidate();
            } else if (committed) {
              setReport(committed);
            }
          }}
          steps={[
            {
              key: 'template',
              title: t('steps.template'),
              content: (
                <div className="flex flex-col gap-4">
                  <p className="text-sm text-muted-foreground">{t('templateIntro')}</p>
                  <Select
                    className="w-64"
                    value={kind}
                    onValueChange={(value) => {
                      setKind(value as Kind);
                      setReport(undefined);
                      setResult(undefined);
                    }}
                    options={[
                      { value: 'units', label: t('kinds.units') },
                      { value: 'employees', label: t('kinds.employees') },
                    ]}
                  />
                  <Button variant="outline" asChild className="self-start">
                    <a href={`/api/v1/org/import/templates/${kind}`} download>
                      <Download aria-hidden />
                      {t('downloadTemplate')}
                    </a>
                  </Button>
                  <p className="text-xs text-muted-foreground">{t('orderHint')}</p>
                </div>
              ),
            },
            {
              key: 'upload',
              title: t('steps.upload'),
              validate: async () => {
                const validated = await run('validate');
                setReport(validated);
                return validated !== undefined;
              },
              content: (
                <FileDrop
                  label={t('drop')}
                  hint={t('dropHint')}
                  accept=".xlsx,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv"
                  file={file}
                  onFile={(next) => {
                    setFile(next);
                    setReport(undefined);
                    setResult(undefined);
                  }}
                />
              ),
            },
            {
              key: 'review',
              title: t('steps.review'),
              validate: () => issues.length === 0,
              content: result ? (
                <StatusBanner
                  tone="success"
                  title={t('done', { created: result.created, updated: result.updated })}
                />
              ) : issues.length === 0 ? (
                <StatusBanner
                  tone="success"
                  title={t('valid', { count: report?.totalRows ?? 0 })}
                  description={t('validHint')}
                />
              ) : (
                <div className="flex flex-col gap-3">
                  <StatusBanner
                    tone="danger"
                    title={t('invalid', { count: issues.length })}
                    actions={
                      <Button variant="outline" size="sm" onClick={() => void downloadReport()}>
                        <Download aria-hidden />
                        {t('downloadReport')}
                      </Button>
                    }
                  />
                  <Table>
                    <TableHead>
                      <TableRow>
                        <TableHeaderCell>{t('row')}</TableHeaderCell>
                        <TableHeaderCell>{t('column')}</TableHeaderCell>
                        <TableHeaderCell>{t('problem')}</TableHeaderCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {issues.slice(0, 200).map((issue, index) => (
                        <TableRow key={`${issue.row}-${issue.column ?? ''}-${index}`}>
                          <TableCell>{issue.row}</TableCell>
                          <TableCell dir="ltr" className="text-start font-mono text-xs">
                            {issue.column ?? '—'}
                          </TableCell>
                          <TableCell>
                            {translateMessageKey(tc, issue.messageKey, issue.params ?? undefined)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              ),
            },
          ]}
        />
      </div>
    </div>
  );
}
