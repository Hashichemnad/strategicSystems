import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { UnitSelect, UserPicker } from '@/features/access/pickers';
import { pickText } from '@/lib/format';
import { Button, Select, StatusChip } from '@/ui';

export interface PrincipalRef {
  kind: 'user' | 'role' | 'unit';
  ref: string;
  label: string;
}

export function useRoleOptions(enabled = true) {
  const { i18n } = useTranslation();
  const roles = useQuery({
    queryKey: ['access', 'roles'],
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/roles', { signal })),
  });
  return useMemo(
    () =>
      (roles.data ?? [])
        .filter((role) => role.active)
        .map((role) => ({
          value: `${role.systemCode}.${role.code}`,
          label: `${pickText(i18n.language, role.nameAr, role.nameEn) || role.code} (${role.systemCode})`,
        })),
    [roles.data, i18n.language],
  );
}

/** Users, roles and units (and below) for sharing and schedule recipients. Names are shown as chosen; the server checks every reference. */
export function PrincipalListEditor({
  value,
  onChange,
  testId,
}: {
  value: PrincipalRef[];
  onChange: (value: PrincipalRef[]) => void;
  testId?: string;
}) {
  const { t } = useTranslation('reporting');
  const [kind, setKind] = useState<'user' | 'role' | 'unit'>('user');
  const roles = useRoleOptions(kind === 'role');
  const add = (item: PrincipalRef) => {
    if (!value.some((v) => v.kind === item.kind && v.ref === item.ref)) onChange([...value, item]);
  };
  return (
    <div className="flex flex-col gap-2" data-testid={testId}>
      <div className="flex flex-wrap items-end gap-2">
        <div className="w-36">
          <Select
            aria-label={t('principals.kind')}
            value={kind}
            onValueChange={(v) => setKind(v as typeof kind)}
            options={[
              { value: 'user', label: t('principals.user') },
              { value: 'role', label: t('principals.role') },
              { value: 'unit', label: t('principals.unit') },
            ]}
          />
        </div>
        <div className="min-w-56 flex-1">
          {kind === 'user' && (
            <UserPicker
              value={undefined}
              onChange={(principal) => {
                if (principal?.id)
                  add({
                    kind: 'user',
                    ref: principal.id,
                    label: principal.nameEn ?? principal.nameAr ?? principal.detail ?? principal.id,
                  });
              }}
            />
          )}
          {kind === 'role' && (
            <Select
              aria-label={t('principals.role')}
              value={undefined}
              placeholder={t('principals.chooseRole')}
              onValueChange={(v) =>
                add({ kind: 'role', ref: v, label: roles.find((r) => r.value === v)?.label ?? v })
              }
              options={roles}
            />
          )}
          {kind === 'unit' && (
            <UnitSelect
              value={undefined}
              onChange={(v) => v && add({ kind: 'unit', ref: v, label: t('principals.unitAndBelow') })}
            />
          )}
        </div>
      </div>
      <ul className="flex flex-wrap gap-2">
        {value.map((item) => (
          <li key={`${item.kind}:${item.ref}`}>
            <StatusChip>
              {t(`principals.${item.kind}`)}: {item.label}
              <Button
                size="sm"
                variant="ghost"
                className="h-5 px-1"
                aria-label={t('principals.remove', { name: item.label })}
                onClick={() => onChange(value.filter((v) => !(v.kind === item.kind && v.ref === item.ref)))}
              >
                <X className="size-3" aria-hidden />
              </Button>
            </StatusChip>
          </li>
        ))}
      </ul>
    </div>
  );
}
