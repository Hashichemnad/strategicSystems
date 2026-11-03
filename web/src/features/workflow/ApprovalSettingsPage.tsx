import { Link } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useServerErrors } from '@/features/settings/common';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  FormField,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { useApprovalSettings, useProcesses, useSaveApprovalSettings, type ProcessSummary } from './api';

function LevelsCard() {
  const { t, i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canManage = useCan('workflow.process.manage');
  const settings = useApprovalSettings();
  const save = useSaveApprovalSettings();
  const errors = useServerErrors();
  const [section, setSection] = useState<string>();
  const [department, setDepartment] = useState<string>();
  useEffect(() => {
    setSection(settings.data?.value.sectionLevelCode);
    setDepartment(settings.data?.value.departmentLevelCode);
  }, [settings.data]);
  if (!settings.data) return <Skeleton className="h-40 rounded-lg" />;
  const options = settings.data.levels.map((level) => ({
    value: level.code,
    label: pickText(i18n.language, level.labelAr, level.labelEn) || level.code,
  }));
  return (
    <form
      className="flex flex-col gap-4 rounded-lg border bg-card p-5"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate(
          {
            sectionLevelCode: section ?? '',
            departmentLevelCode: department ?? '',
            version: settings.data.version,
          },
          {
            onSuccess: () => {
              errors.clear();
              toast({ title: t('settings.levels.saved') });
            },
            onError: errors.fail,
          },
        );
      }}
    >
      <div>
        <h2 className="font-semibold">{t('settings.levels.title')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.levels.description')}</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t('settings.levels.section')} error={errors.errorFor('sectionLevelCode')}>
          <Select value={section} onValueChange={setSection} options={options} disabled={!canManage} />
        </FormField>
        <FormField label={t('settings.levels.department')} error={errors.errorFor('departmentLevelCode')}>
          <Select value={department} onValueChange={setDepartment} options={options} disabled={!canManage} />
        </FormField>
      </div>
      {canManage && (
        <div>
          <Button type="submit" loading={save.isPending}>
            {tc('actions.save')}
          </Button>
        </div>
      )}
    </form>
  );
}

/** Settings → Approvals: the global Approval Settings centre (SPEC §0.2). Every process key registered by the systems. */
export function ApprovalSettingsPage() {
  const { t, i18n } = useTranslation('workflow');
  const { t: tc } = useTranslation();
  const processes = useProcesses();
  const groups = useMemo(() => {
    const bySystem = new Map<string, { first: ProcessSummary; items: ProcessSummary[] }>();
    for (const process of processes.data ?? []) {
      const group = bySystem.get(process.systemCode);
      if (group) group.items.push(process);
      else bySystem.set(process.systemCode, { first: process, items: [process] });
    }
    return [...bySystem.values()];
  }, [processes.data]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      {processes.isError && <StatusBanner tone="danger" title={errorMessage(tc, processes.error)} />}
      <section className="flex flex-col gap-3" aria-label={t('settings.processes.title')}>
        <h2 className="font-semibold">{t('settings.processes.title')}</h2>
        {processes.isPending && <Skeleton className="h-40 rounded-lg" />}
        {processes.data?.length === 0 && <EmptyState title={t('settings.processes.none')} />}
        {groups.map((group) => (
          <div key={group.first.systemCode} className="flex flex-col gap-2">
            <h3 className="text-sm font-medium text-muted-foreground">
              {pickText(i18n.language, group.first.systemNameAr, group.first.systemNameEn) ||
                group.first.systemCode}
            </h3>
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('settings.processes.process')}</TableHeaderCell>
                  <TableHeaderCell>{t('settings.processes.steps')}</TableHeaderCell>
                  <TableHeaderCell>{t('settings.processes.version')}</TableHeaderCell>
                  <TableHeaderCell>{t('settings.processes.changed')}</TableHeaderCell>
                  <TableHeaderCell>{t('settings.processes.inProgress')}</TableHeaderCell>
                  <TableHeaderCell>
                    <span className="sr-only">{tc('fields.actions', { defaultValue: '' })}</span>
                  </TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {group.items.map((process) => (
                  <TableRow key={process.key}>
                    <TableCell>
                      <div className="flex flex-col">
                        <span className="font-medium">
                          {pickText(i18n.language, process.nameAr, process.nameEn)}
                        </span>
                        <span className="text-xs text-muted-foreground" dir="ltr">
                          {process.key}
                        </span>
                      </div>
                    </TableCell>
                    <TableCell>{formatNumber(process.stepCount, i18n.language)}</TableCell>
                    <TableCell>
                      <StatusChip>{t('editor.version', { version: process.versionNo })}</StatusChip>
                    </TableCell>
                    <TableCell>
                      {process.changedAt ? formatDateTime(process.changedAt, i18n.language) : '—'}
                    </TableCell>
                    <TableCell>{formatNumber(process.inProgress, i18n.language)}</TableCell>
                    <TableCell>
                      <Button asChild size="sm" variant="outline">
                        <Link
                          to="/settings/approvals/$processKey"
                          params={{ processKey: process.key }}
                          search={{}}
                        >
                          {t('settings.processes.edit')}
                        </Link>
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        ))}
      </section>
      <LevelsCard />
    </div>
  );
}
