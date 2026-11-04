import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Dialog, DialogClose, DialogContent, FormField, Input, Textarea, useToast } from '@/ui';
import { DatePicker } from '@/ui/date-picker';
import { FieldErrors } from '../project/FieldErrors';
import { type VendorDetail, type VendorValues, useVendorMutation, vendorApi } from '../wasel/waselApi';

const textFields = [
  'nameAr',
  'nameEn',
  'tradeLicenceNo',
  'countryCode',
  'contactPerson',
  'email',
  'phone',
] as const;

/** Create or edit a vendor of the register (PM Office, `portfolio.vendor.manage`). */
export function VendorDialog({ vendor, onClose }: { vendor?: VendorDetail; onClose: () => void }) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const save = useVendorMutation(vendorApi.save);
  const [v, setV] = useState<VendorValues>({
    nameAr: vendor?.nameAr ?? null,
    nameEn: vendor?.nameEn ?? null,
    tradeLicenceNo: vendor?.tradeLicenceNo ?? null,
    licenceExpiry: vendor?.licenceExpiry ?? null,
    countryCode: vendor?.countryCode ?? 'AE',
    address: vendor?.address ?? null,
    contactPerson: vendor?.contactPerson ?? null,
    email: vendor?.email ?? null,
    phone: vendor?.phone ?? null,
    notes: vendor?.notes ?? null,
  });
  const set = (patch: Partial<VendorValues>) => setV((c) => ({ ...c, ...patch }));
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={vendor ? t('detail.edit') : t('register.new')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              data-testid="vendor-save"
              onClick={() =>
                save.mutate(
                  { id: vendor?.id, values: v, version: vendor?.version },
                  {
                    onSuccess: (saved) => {
                      toast({ title: t('register.saved') });
                      onClose();
                      if (!vendor)
                        void navigate({ to: '/portfolio/vendors/$vendorId', params: { vendorId: saved.id } });
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 sm:grid-cols-2" data-testid="vendor-dialog">
          {textFields.map((field) => (
            <FormField
              key={field}
              label={t(`fields.${field}`)}
              required={field === 'nameEn' || field === 'nameAr'}
            >
              <Input
                dir={
                  field === 'nameAr'
                    ? 'rtl'
                    : field === 'nameEn' || field === 'email' || field === 'phone'
                      ? 'ltr'
                      : 'auto'
                }
                type={field === 'email' ? 'email' : 'text'}
                value={v[field] ?? ''}
                data-testid={`vendor-${field}`}
                onChange={(e) => set({ [field]: e.target.value || null })}
              />
            </FormField>
          ))}
          <FormField label={t('fields.licenceExpiry')}>
            <DatePicker
              value={v.licenceExpiry ?? undefined}
              onChange={(d) => set({ licenceExpiry: d ?? null })}
            />
          </FormField>
          <FormField label={t('fields.address')} className="sm:col-span-2">
            <Textarea
              dir="auto"
              value={v.address ?? ''}
              onChange={(e) => set({ address: e.target.value || null })}
            />
          </FormField>
          <FormField label={t('fields.notes')} className="sm:col-span-2">
            <Textarea
              dir="auto"
              value={v.notes ?? ''}
              onChange={(e) => set({ notes: e.target.value || null })}
            />
          </FormField>
          {save.error && (
            <div className="sm:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
