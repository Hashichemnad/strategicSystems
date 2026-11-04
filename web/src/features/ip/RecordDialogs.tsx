import { useState, type ReactNode, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useAttachments } from '@/features/files/api';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Select,
  StatusBanner,
  Textarea,
  useToast,
} from '@/ui';
import { IP_OWNER_TYPE, ipApi, useIpRecordMutation, type IpRecord } from './api';
import { actionError } from './components';
import { addYears, proposeExpiry, todayIso } from './logic';

export type IpDialog = 'filing' | 'internal' | 'registration' | 'moetRejection' | 'close' | 'renew';

const NONE = '-';

interface DialogProps {
  record: IpRecord;
  onClose: () => void;
}

/** The dialog of the chosen record action (each posts with the record's version; 409 on a stale copy). */
export function RecordActionDialog({ kind, record, onClose }: DialogProps & { kind: IpDialog | undefined }) {
  switch (kind) {
    case 'filing':
      return <FilingDialog record={record} onClose={onClose} />;
    case 'internal':
      return <InternalDialog record={record} onClose={onClose} />;
    case 'registration':
      return <RegistrationDialog record={record} onClose={onClose} />;
    case 'moetRejection':
      return <MoetRejectionDialog record={record} onClose={onClose} />;
    case 'close':
      return <CloseDialog record={record} onClose={onClose} />;
    case 'renew':
      return <RenewDialog record={record} onClose={onClose} />;
    default:
      return null;
  }
}

/** Shell of an action dialog: form, inline error, Cancel / confirm. */
function ActionForm({
  title,
  description,
  confirm,
  busy,
  error,
  disabled,
  destructive,
  onSubmit,
  onClose,
  children,
}: {
  title: string;
  description?: string;
  confirm: string;
  busy: boolean;
  error?: string | undefined;
  disabled?: boolean;
  destructive?: boolean;
  onSubmit: () => void;
  onClose: () => void;
  children?: ReactNode;
}) {
  const { t: tc } = useTranslation();
  const submit = (event: SyntheticEvent) => {
    event.preventDefault();
    if (!disabled && !busy) onSubmit();
  };
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent title={title} description={description}>
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          {children}
          {error && <StatusBanner tone="danger" title={error} />}
          <div className="flex flex-wrap justify-end gap-2">
            <DialogClose asChild>
              <Button type="button" variant="outline">
                {tc('actions.cancel')}
              </Button>
            </DialogClose>
            <Button
              type="submit"
              variant={destructive ? 'danger' : 'primary'}
              loading={busy}
              disabled={disabled}
              data-testid="dialog-confirm"
            >
              {confirm}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function useAction<TInput>(
  record: IpRecord,
  fn: (id: string, input: TInput) => Promise<IpRecord>,
  onClose: () => void,
) {
  const { t } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const mutation = useIpRecordMutation(record.id, fn);
  const [error, setError] = useState<string>();
  return {
    busy: mutation.isPending,
    error,
    run: (input: TInput) => {
      setError(undefined);
      mutation.mutate(input, {
        onSuccess: () => {
          toast({ title: t('actions.saved') });
          onClose();
        },
        onError: (e) => setError(actionError(tc, e)),
      });
    },
  };
}

/** Certificate chooser: one of the record's attachments (uploaded on the Documents tab). */
function CertificateSelect({
  recordId,
  value,
  onChange,
}: {
  recordId: string;
  value: string | undefined;
  onChange: (value: string | undefined) => void;
}) {
  const { t } = useTranslation('ip');
  const files = useAttachments(IP_OWNER_TYPE, recordId);
  const options = [
    { value: NONE, label: t('dialogs.noCertificate') },
    ...(files.data ?? []).map((f) => ({ value: f.id, label: f.name })),
  ];
  return (
    <FormField label={t('fields.certificate')} hint={t('dialogs.certificateHint')}>
      <Select
        value={value ?? NONE}
        onValueChange={(v) => onChange(v === NONE ? undefined : v)}
        options={options}
      />
    </FormField>
  );
}

function FilingDialog({ record, onClose }: DialogProps) {
  const { t } = useTranslation('ip');
  const action = useAction(record, ipApi.file, onClose);
  const [filingDate, setFilingDate] = useState<string | undefined>(todayIso());
  const [applicationNo, setApplicationNo] = useState('');
  const future = !!filingDate && filingDate > todayIso();
  return (
    <ActionForm
      title={t('actions.filing')}
      description={t('dialogs.filingDescription')}
      confirm={t('actions.filing')}
      busy={action.busy}
      error={action.error}
      disabled={!filingDate || !applicationNo.trim() || future}
      onSubmit={() =>
        action.run({
          filingDate: filingDate ?? null,
          applicationNo: applicationNo.trim(),
          version: record.version,
        })
      }
      onClose={onClose}
    >
      <FormField
        label={t('fields.filingDate')}
        required
        error={future ? t('dialogs.notInFuture') : undefined}
      >
        <DatePicker value={filingDate} onChange={setFilingDate} />
      </FormField>
      <FormField label={t('fields.applicationNo')} required>
        <Input
          value={applicationNo}
          maxLength={100}
          onChange={(e) => setApplicationNo(e.target.value)}
          dir="auto"
        />
      </FormField>
    </ActionForm>
  );
}

function InternalDialog({ record, onClose }: DialogProps) {
  const { t } = useTranslation('ip');
  const action = useAction(record, ipApi.registerInternally, onClose);
  const [notes, setNotes] = useState('');
  return (
    <ActionForm
      title={t('actions.internal')}
      description={t('dialogs.internalDescription')}
      confirm={t('actions.internal')}
      busy={action.busy}
      error={action.error}
      onSubmit={() => action.run({ notes: notes.trim() || null, version: record.version })}
      onClose={onClose}
    >
      <FormField label={t('fields.notes')}>
        <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} />
      </FormField>
    </ActionForm>
  );
}

function RegistrationDialog({ record, onClose }: DialogProps) {
  const { t } = useTranslation('ip');
  const action = useAction(record, ipApi.register, onClose);
  const today = todayIso();
  const term = record.termYears ?? undefined;
  const byPublication = record.termFrom === 'publication';
  const [registrationNo, setRegistrationNo] = useState('');
  const [registrationDate, setRegistrationDate] = useState<string | undefined>(today);
  const [publicationDate, setPublicationDate] = useState<string | undefined>(
    record.firstPublicationDate ?? undefined,
  );
  const [expiryDate, setExpiryDate] = useState<string | undefined>(
    proposeExpiry(term, record.termFrom, today, record.firstPublicationDate ?? undefined),
  );
  const [expiryTouched, setExpiryTouched] = useState(false);
  const [certificate, setCertificate] = useState<string | undefined>();
  const invalidExpiry = !!expiryDate && !!registrationDate && expiryDate <= registrationDate;
  const invalidPublication =
    !!publicationDate &&
    ((!!registrationDate && publicationDate > registrationDate) || publicationDate > today);
  const missingPublication = byPublication && !!term && !publicationDate && !expiryDate;
  const repropose = (registration: string | undefined, publication: string | undefined) => {
    if (!expiryTouched) setExpiryDate(proposeExpiry(term, record.termFrom, registration, publication));
  };
  return (
    <ActionForm
      title={t('actions.register')}
      description={t('dialogs.registrationDescription')}
      confirm={t('actions.register')}
      busy={action.busy}
      error={action.error}
      disabled={
        !registrationNo.trim() ||
        !registrationDate ||
        invalidExpiry ||
        invalidPublication ||
        missingPublication
      }
      onSubmit={() =>
        action.run({
          registrationNo: registrationNo.trim(),
          registrationDate: registrationDate ?? null,
          firstPublicationDate: publicationDate ?? null,
          expiryDate: expiryDate ?? null,
          certificateFileId: certificate ?? null,
          version: record.version,
        })
      }
      onClose={onClose}
    >
      <FormField label={t('fields.registrationNo')} required>
        <Input
          value={registrationNo}
          maxLength={100}
          onChange={(e) => setRegistrationNo(e.target.value)}
          dir="auto"
        />
      </FormField>
      <FormField label={t('fields.registrationDate')} required>
        <DatePicker
          value={registrationDate}
          onChange={(value) => {
            setRegistrationDate(value);
            repropose(value, publicationDate);
          }}
        />
      </FormField>
      <FormField
        label={t('fields.firstPublicationDate')}
        required={byPublication && !!term}
        hint={byPublication ? t('dialogs.publicationHint') : undefined}
        error={invalidPublication ? t('errors.publicationDate') : undefined}
      >
        <DatePicker
          value={publicationDate}
          onChange={(value) => {
            setPublicationDate(value);
            repropose(registrationDate, value);
          }}
        />
      </FormField>
      <FormField
        label={t('fields.expiryDate')}
        hint={
          term
            ? t(byPublication ? 'dialogs.expiryHintPublication' : 'dialogs.expiryHint', { count: term })
            : t('dialogs.noExpiryHint')
        }
        error={invalidExpiry ? t('errors.expiryBeforeRegistration') : undefined}
      >
        <DatePicker
          value={expiryDate}
          onChange={(value) => {
            setExpiryTouched(true);
            setExpiryDate(value);
          }}
        />
      </FormField>
      <CertificateSelect recordId={record.id} value={certificate} onChange={setCertificate} />
    </ActionForm>
  );
}

function MoetRejectionDialog({ record, onClose }: DialogProps) {
  const { t } = useTranslation('ip');
  const action = useAction(record, ipApi.moetRejection, onClose);
  const [reason, setReason] = useState('');
  return (
    <ActionForm
      title={t('actions.moetRejection')}
      description={t('dialogs.moetRejectionDescription')}
      confirm={t('actions.moetRejection')}
      destructive
      busy={action.busy}
      error={action.error}
      disabled={!reason.trim()}
      onSubmit={() => action.run({ reason: reason.trim(), version: record.version })}
      onClose={onClose}
    >
      <FormField label={t('fields.moetRejectionReason')} required>
        <Textarea value={reason} maxLength={2000} onChange={(e) => setReason(e.target.value)} rows={4} />
      </FormField>
    </ActionForm>
  );
}

function CloseDialog({ record, onClose }: DialogProps) {
  const { t } = useTranslation('ip');
  const action = useAction(record, ipApi.close, onClose);
  return (
    <ActionForm
      title={t('actions.close')}
      description={t('dialogs.closeDescription')}
      confirm={t('actions.close')}
      destructive
      busy={action.busy}
      error={action.error}
      onSubmit={() => action.run({ notes: null, version: record.version })}
      onClose={onClose}
    />
  );
}

function RenewDialog({ record, onClose }: DialogProps) {
  const { t } = useTranslation('ip');
  const action = useAction(record, ipApi.renew, onClose);
  const today = todayIso();
  const term = record.termYears ?? undefined;
  const initialFrom = record.expiryDate ?? today;
  const [registrationNo, setRegistrationNo] = useState(record.registrationNo ?? '');
  const [from, setFrom] = useState<string | undefined>(initialFrom);
  const [to, setTo] = useState<string | undefined>(term ? addYears(initialFrom, term) : undefined);
  const [toTouched, setToTouched] = useState(false);
  const [certificate, setCertificate] = useState<string | undefined>();
  const invalid = !!from && !!to && to <= from;
  return (
    <ActionForm
      title={t('actions.renew')}
      description={t('dialogs.renewDescription')}
      confirm={t('actions.renew')}
      busy={action.busy}
      error={action.error}
      disabled={!registrationNo.trim() || !from || !to || invalid}
      onSubmit={() =>
        action.run({
          registrationNo: registrationNo.trim(),
          from: from ?? null,
          to: to ?? null,
          certificateFileId: certificate ?? null,
          version: record.version,
        })
      }
      onClose={onClose}
    >
      <FormField label={t('fields.registrationNo')} required>
        <Input
          value={registrationNo}
          maxLength={100}
          onChange={(e) => setRegistrationNo(e.target.value)}
          dir="auto"
        />
      </FormField>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t('fields.from')} required>
          <DatePicker
            value={from}
            onChange={(value) => {
              setFrom(value);
              if (!toTouched && value && term) setTo(addYears(value, term));
            }}
          />
        </FormField>
        <FormField label={t('fields.to')} required error={invalid ? t('errors.period') : undefined}>
          <DatePicker
            value={to}
            onChange={(value) => {
              setToTouched(true);
              setTo(value);
            }}
          />
        </FormField>
      </div>
      <CertificateSelect recordId={record.id} value={certificate} onChange={setCertificate} />
    </ActionForm>
  );
}
