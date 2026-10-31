import { getRouteApi, Link } from '@tanstack/react-router';
import { Plus, UsersRound } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import {
  Avatar,
  Button,
  EmptyState,
  Input,
  PageHeader,
  Pagination,
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
} from '@/ui';
import { useEmployees, useUnitTree } from './api';
import { EmployeeDialog } from './EmployeeDialog';

const routeApi = getRouteApi('/app/org/employees');
const PAGE_SIZE = 25;

export const statusTone = { active: 'success', on_leave: 'warning', left: 'neutral' } as const;

export function EmployeesPage() {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const search = routeApi.useSearch();
  const navigate = routeApi.useNavigate();
  const canManage = useCan('org.employee.manage');
  const [text, setText] = useState(search.search ?? '');
  const [creating, setCreating] = useState(false);
  const units = useUnitTree();
  const employees = useEmployees({
    page: search.page,
    pageSize: PAGE_SIZE,
    search: search.search,
    status: search.status,
    unitId: search.unitId,
    includeDescendants: true,
    sort: 'nameEn',
  });

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if ((search.search ?? '') !== text)
        void navigate({ search: (s) => ({ ...s, page: 1, search: text || undefined }) });
    }, 300);
    return () => window.clearTimeout(timer);
  }, [text, search.search, navigate]);

  const data = employees.data;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('employees.title')}
        description={t('employees.description')}
        actions={
          canManage && (
            <Button onClick={() => setCreating(true)}>
              <Plus aria-hidden />
              {t('employees.create')}
            </Button>
          )
        }
      />
      <div className="flex flex-wrap gap-3">
        <Input
          type="search"
          className="max-w-xs"
          placeholder={t('employees.searchPlaceholder')}
          aria-label={tc('actions.search')}
          value={text}
          onChange={(event) => setText(event.target.value)}
        />
        <Select
          className="w-56"
          value={search.unitId ?? 'all'}
          onValueChange={(value) =>
            void navigate({ search: (s) => ({ ...s, page: 1, unitId: value === 'all' ? undefined : value }) })
          }
          options={[
            { value: 'all', label: t('employees.allUnits') },
            ...(units.data ?? []).map((unit) => ({
              value: unit.id,
              label: `${'— '.repeat(unit.depth)}${pickText(i18n.language, unit.nameAr, unit.nameEn)}`,
            })),
          ]}
        />
        <Select
          className="w-44"
          value={search.status ?? 'all'}
          onValueChange={(value) =>
            void navigate({
              search: (s) => ({
                ...s,
                page: 1,
                status: value === 'all' ? undefined : (value as 'active' | 'on_leave' | 'left'),
              }),
            })
          }
          options={[
            { value: 'all', label: t('employees.allStatuses') },
            ...(['active', 'on_leave', 'left'] as const).map((value) => ({
              value,
              label: t(`employeeStatus.${value}`),
            })),
          ]}
        />
      </div>
      {employees.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, employees.error)} />
      ) : !data ? (
        <div className="flex flex-col gap-2">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : data.items.length === 0 ? (
        <EmptyState title={t('employees.empty')} icon={UsersRound} />
      ) : (
        <>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.employeeNo')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.jobTitle')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.items.map((employee) => {
                const name = pickText(i18n.language, employee.nameAr, employee.nameEn);
                return (
                  <TableRow key={employee.id}>
                    <TableCell>
                      <Link
                        to="/org/employees/$employeeId"
                        params={{ employeeId: employee.id }}
                        className="flex items-center gap-3 font-medium text-primary hover:underline"
                      >
                        <Avatar
                          name={name}
                          size="sm"
                          src={employee.hasPhoto ? `/api/v1/org/employees/${employee.id}/photo` : undefined}
                        />
                        {name}
                      </Link>
                    </TableCell>
                    <TableCell dir="ltr" className="text-start">
                      {employee.employeeNo}
                    </TableCell>
                    <TableCell>{pickText(i18n.language, employee.unitNameAr, employee.unitNameEn)}</TableCell>
                    <TableCell>
                      {pickText(i18n.language, employee.jobTitleAr, employee.jobTitleEn) || '—'}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={statusTone[employee.status as keyof typeof statusTone]}>
                        {t(`employeeStatus.${employee.status as 'active'}`)}
                      </StatusChip>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <Pagination
            page={data.page}
            totalPages={Math.max(1, Math.ceil(data.totalCount / data.pageSize))}
            totalCount={data.totalCount}
            onPageChange={(page) => void navigate({ search: (s) => ({ ...s, page }) })}
          />
        </>
      )}
      {creating && <EmployeeDialog open onOpenChange={setCreating} />}
    </div>
  );
}
