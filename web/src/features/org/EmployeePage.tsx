import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { api, ApiError, unwrap } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { formatDate, pickText } from '@/lib/format';
import {
  Avatar,
  Button,
  FileDrop,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  useToast,
} from '@/ui';
import { orgKeys, useEmployee, useEmployeeHistory, useInvalidateOrg } from './api';
import { EmployeeDialog } from './EmployeeDialog';
import { statusTone } from './EmployeesPage';

const routeApi = getRouteApi('/app/org/employees/$employeeId');

export function EmployeePage() {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const { employeeId } = routeApi.useParams();
  const admin = useCan('org.employee.manage');
  const employee = useEmployee(employeeId);
  const history = useEmployeeHistory(employeeId);
  const invalidate = useInvalidateOrg();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [photo, setPhoto] = useState<File | null>(null);
  const [photoVersion, setPhotoVersion] = useState(0);

  const changeStatus = useMutation({
    mutationFn: async (status: string) =>
      unwrap(
        await api.POST('/api/v1/org/employees/{id}/change-status', {
          params: { path: { id: employeeId } },
          body: { status, version: employee.data?.version ?? 0 },
        }),
      ),
    onSuccess: () => void invalidate(),
    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
  });
  const upload = useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData();
      form.append('file', file);
      unwrap(
        await api.PUT('/api/v1/org/employees/{id}/photo', {
          params: { path: { id: employeeId } },
          body: form as unknown as { file: string },
          bodySerializer: (body: unknown) => body as FormData,
        }),
      );
    },
    onSuccess: () => {
      setPhoto(null);
      setPhotoVersion((v) => v + 1);
      void queryClient.invalidateQueries({ queryKey: orgKeys.employee(employeeId) });
      toast({ title: t('employees.photoSaved') });
    },
    onError: (failure) =>
      toast({
        tone: 'error',
        title:
          failure instanceof ApiError
            ? translateMessageKey(tc, failure.problem.messageKey, failure.problem.params)
            : errorMessage(tc, failure),
      }),
  });

  if (employee.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (employee.isError) return <StatusBanner tone="danger" title={errorMessage(tc, employee.error)} />;
  const data = employee.data;
  const name = pickText(i18n.language, data.nameAr, data.nameEn);
  const ref = (r?: { nameAr?: string | null; nameEn?: string | null } | null) =>
    r ? pickText(i18n.language, r.nameAr, r.nameEn) : '—';

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/org/employees"
        search={{ page: 1 }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('employees.back')}
      </Link>
      <PageHeader
        title={
          <span className="flex items-center gap-4">
            <Avatar
              name={name}
              size="lg"
              src={data.hasPhoto ? `/api/v1/org/employees/${data.id}/photo?v=${photoVersion}` : undefined}
            />
            <span className="flex flex-col">
              {name}
              <span className="text-base font-normal text-muted-foreground">
                {pickText(i18n.language, data.jobTitleAr, data.jobTitleEn)}
              </span>
            </span>
          </span>
        }
        meta={
          <StatusChip tone={statusTone[data.status as keyof typeof statusTone]}>
            {t(`employeeStatus.${data.status as 'active'}`)}
          </StatusChip>
        }
        actions={
          admin && (
            <div className="flex flex-wrap gap-2">
              <Select
                className="w-40"
                value={data.status}
                onValueChange={(status) => status !== data.status && changeStatus.mutate(status)}
                options={(['active', 'on_leave', 'left'] as const).map((value) => ({
                  value,
                  label: t(`employeeStatus.${value}`),
                }))}
              />
              <Button variant="outline" onClick={() => setEditing(true)}>
                {tc('actions.edit')}
              </Button>
            </div>
          )
        }
      />
      <Tabs defaultValue="details">
        <TabsList>
          <TabsTrigger value="details">{t('employees.tabs.details')}</TabsTrigger>
          <TabsTrigger value="history">{t('employees.tabs.history')}</TabsTrigger>
        </TabsList>
        <TabsContent value="details" className="grid gap-6 pt-4 lg:grid-cols-[2fr_1fr]">
          <dl className="grid gap-x-6 gap-y-3 rounded-lg border bg-card p-5 text-sm sm:grid-cols-2">
            <Item label={t('fields.employeeNo')} value={<span dir="ltr">{data.employeeNo}</span>} />
            <Item label={t('fields.email')} value={<span dir="ltr">{data.email}</span>} />
            <Item label={t('fields.nameAr')} value={data.nameAr ?? '—'} />
            <Item label={t('fields.nameEn')} value={data.nameEn ?? '—'} />
            <Item
              label={t('fields.unit')}
              value={
                <Link
                  to="/org/units"
                  search={{ unit: data.unit.id }}
                  className="text-primary hover:underline"
                >
                  {ref(data.unit)}
                </Link>
              }
            />
            <Item
              label={t('fields.manager')}
              value={
                data.manager ? (
                  <Link
                    to="/org/employees/$employeeId"
                    params={{ employeeId: data.manager.id }}
                    className="text-primary hover:underline"
                  >
                    {ref(data.manager)}
                  </Link>
                ) : (
                  '—'
                )
              }
            />
            <Item label={t('fields.mobile')} value={<span dir="ltr">{data.mobile ?? '—'}</span>} />
            <Item label={t('fields.grade')} value={data.grade ?? '—'} />
            <Item
              label={t('fields.joinDate')}
              value={data.joinDate ? formatDate(data.joinDate, i18n.language) : '—'}
            />
          </dl>
          {admin && (
            <div className="flex flex-col gap-3 rounded-lg border bg-card p-5">
              <h2 className="font-semibold">{t('employees.photo')}</h2>
              <FileDrop
                label={t('employees.photoDrop')}
                hint={t('employees.photoHint')}
                accept="image/png,image/jpeg,image/webp"
                file={photo}
                onFile={setPhoto}
              />
              <Button
                disabled={!photo}
                loading={upload.isPending}
                onClick={() => photo && upload.mutate(photo)}
              >
                {t('employees.photoUpload')}
              </Button>
            </div>
          )}
        </TabsContent>
        <TabsContent value="history" className="pt-4">
          <ol className="flex flex-col gap-3">
            {history.data?.map((assignment) => (
              <li key={assignment.validFrom} className="rounded-md border bg-card p-3 text-sm">
                <p className="font-medium">
                  {formatDate(assignment.validFrom, i18n.language)} –{' '}
                  {assignment.validTo ? formatDate(assignment.validTo, i18n.language) : t('units.current')}
                </p>
                <p>
                  {ref(assignment.unit)} ·{' '}
                  {pickText(i18n.language, assignment.jobTitleAr, assignment.jobTitleEn) || '—'}
                </p>
                <p className="text-xs text-muted-foreground">
                  {t('fields.manager')}: {ref(assignment.manager)}
                </p>
              </li>
            ))}
          </ol>
        </TabsContent>
      </Tabs>
      {editing && <EmployeeDialog open onOpenChange={setEditing} employee={data} />}
    </div>
  );
}

function Item({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
