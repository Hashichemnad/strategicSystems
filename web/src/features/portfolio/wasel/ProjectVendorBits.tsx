import { Share2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import { Button, Dialog, DialogClose, DialogContent, FormField, Select, StatusChip, type SelectOption } from '@/ui';
import { FieldErrors } from '../project/FieldErrors';
import { usePanelMutation, useVendors, useWaselPanel, waselApi } from './waselApi';

/** Vendors of the register as select options (contract supplier picker, ADR 0112). */
export function useVendorOptions(): SelectOption[] {
  const { i18n } = useTranslation();
  const vendors = useVendors('', 'active');
  return (vendors.data ?? []).map((v) => ({ value: v.id, label: `${v.number} · ${pickText(i18n.language, v.nameAr, v.nameEn)}` }));
}

/** Meeting row cell: which vendor attends (sees the minutes) and whether it confirmed; the PM shares it (SPEC §1.18 "Meetings"). */
export function MeetingVendorCell({
  projectId,
  meetingId,
  vendorId,
  confirmedAt,
  canShare,
}: {
  projectId: string;
  meetingId: string;
  vendorId: string | null | undefined;
  confirmedAt: string | null | undefined;
  canShare: boolean;
}) {
  const { t, i18n } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const panel = useWaselPanel(projectId);
  const share = usePanelMutation(projectId, waselApi.shareMeeting);
  const [open, setOpen] = useState(false);
  const [choice, setChoice] = useState(vendorId ?? '*');
  const assignments = (panel.data?.assignments ?? []).filter((a) => a.status === 'active');
  const current = (panel.data?.assignments ?? []).find((a) => a.vendorId === vendorId);
  if (!vendorId && (!canShare || assignments.length === 0)) return <span className="text-muted-foreground">—</span>;
  return (
    <span className="inline-flex flex-wrap items-center gap-1" data-testid="meeting-vendor">
      {current && <bdi className="text-sm">{pickText(i18n.language, current.vendorNameAr, current.vendorNameEn)}</bdi>}
      {vendorId && (
        <StatusChip tone={confirmedAt ? 'success' : 'info'}>{confirmedAt ? t('meetings.confirmed') : t('meetings.shared')}</StatusChip>
      )}
      {canShare && assignments.length > 0 && (
        <Button size="sm" variant="ghost" aria-label={t('meetings.share')} onClick={() => setOpen(true)} data-testid="meeting-share-vendor">
          <Share2 aria-hidden />
        </Button>
      )}
      {open && (
        <Dialog open onOpenChange={(o) => !o && setOpen(false)}>
          <DialogContent
            title={t('meetings.share')}
            description={t('meetings.shareHint')}
            footer={
              <>
                <DialogClose asChild>
                  <Button variant="outline">{tc('actions.cancel')}</Button>
                </DialogClose>
                <Button
                  loading={share.isPending}
                  data-testid="meeting-share-save"
                  onClick={() =>
                    share.mutate({ meetingId, vendorId: choice === '*' ? null : choice }, { onSuccess: () => setOpen(false) })
                  }
                >
                  {tc('actions.save')}
                </Button>
              </>
            }
          >
            <FormField label={t('panel.vendor')}>
              <Select
                value={choice}
                onValueChange={setChoice}
                options={[
                  { value: '*', label: t('meetings.notShared') },
                  ...assignments.map((a) => ({ value: a.vendorId, label: pickText(i18n.language, a.vendorNameAr, a.vendorNameEn) })),
                ]}
              />
            </FormField>
            {share.error && <FieldErrors error={share.error} />}
          </DialogContent>
        </Dialog>
      )}
    </span>
  );
}
