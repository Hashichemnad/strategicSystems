import { Download, Eye, FileText, History, Trash2, Upload } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime, formatNumber } from '@/lib/format';
import {
  Button,
  buttonVariants,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FileDrop,
  FormField,
  Input,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  contentUrl,
  useAttachments,
  useDeleteFile,
  useFileVersions,
  useUploadAttachment,
  type StoredFile,
} from './api';
import { canPreview, FilePreview } from './FilePreview';

const ACCEPT = '.pdf,.png,.jpg,.jpeg,.gif,.webp,.docx,.xlsx,.pptx,.doc,.xls,.ppt,.txt,.csv';

interface DocumentsPanelProps {
  ownerType: string;
  ownerId: string;
  canEdit: boolean;
}

/** Documents tab of a record: typed attachments with versions, preview, authorised downloads and deletion with a reason. */
export function DocumentsPanel({ ownerType, ownerId, canEdit }: DocumentsPanelProps) {
  const { t, i18n } = useTranslation('files');
  const { t: tc } = useTranslation();
  const files = useAttachments(ownerType, ownerId);
  const [uploading, setUploading] = useState<{ replaces?: StoredFile } | null>(null);
  const [previewing, setPreviewing] = useState<StoredFile | null>(null);
  const [versionsOf, setVersionsOf] = useState<StoredFile | null>(null);
  const [deleting, setDeleting] = useState<StoredFile | null>(null);

  if (files.isPending) return <Skeleton className="h-48 rounded-lg" />;
  if (files.isError) return <StatusBanner tone="danger" title={errorMessage(tc, files.error)} />;

  return (
    <div className="flex flex-col gap-4">
      {canEdit && (
        <div>
          <Button onClick={() => setUploading({})}>
            <Upload aria-hidden />
            {t('upload')}
          </Button>
        </div>
      )}
      {files.data.length === 0 ? (
        <EmptyState icon={FileText} title={t('empty')} description={canEdit ? t('emptyHint') : undefined} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('name')}</TableHeaderCell>
                <TableHeaderCell>{t('version')}</TableHeaderCell>
                <TableHeaderCell>{t('size')}</TableHeaderCell>
                <TableHeaderCell>{t('updated')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{tc('actions.more')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {files.data.map((file) => (
                <TableRow key={file.id}>
                  <TableCell className="font-medium" dir="auto">
                    {file.name}
                  </TableCell>
                  <TableCell>{formatNumber(file.versionNo, i18n.language)}</TableCell>
                  <TableCell>{formatSize(file.sizeBytes, i18n.language)}</TableCell>
                  <TableCell className="whitespace-nowrap">
                    {formatDateTime(file.updatedAt, i18n.language)}
                  </TableCell>
                  <TableCell>
                    <div className="flex justify-end gap-1">
                      {canPreview(file.contentType) && (
                        <Button
                          variant="ghost"
                          size="icon"
                          aria-label={t('preview', { name: file.name })}
                          onClick={() => setPreviewing(file)}
                        >
                          <Eye aria-hidden />
                        </Button>
                      )}
                      <a
                        className={buttonVariants({ variant: 'ghost', size: 'icon' })}
                        href={contentUrl(file.id, undefined, true)}
                        aria-label={t('download', { name: file.name })}
                      >
                        <Download aria-hidden />
                      </a>
                      <Button
                        variant="ghost"
                        size="icon"
                        aria-label={t('versions', { name: file.name })}
                        onClick={() => setVersionsOf(file)}
                      >
                        <History aria-hidden />
                      </Button>
                      {canEdit && (
                        <>
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={t('newVersion', { name: file.name })}
                            onClick={() => setUploading({ replaces: file })}
                          >
                            <Upload aria-hidden />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={t('delete', { name: file.name })}
                            onClick={() => setDeleting(file)}
                          >
                            <Trash2 aria-hidden />
                          </Button>
                        </>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {uploading && (
        <UploadDialog
          ownerType={ownerType}
          ownerId={ownerId}
          replaces={uploading.replaces}
          onClose={() => setUploading(null)}
        />
      )}
      <Dialog open={previewing !== null} onOpenChange={(open) => !open && setPreviewing(null)}>
        {previewing && (
          <DialogContent title={previewing.name} className="max-w-4xl">
            <FilePreview file={previewing} />
          </DialogContent>
        )}
      </Dialog>
      <Dialog open={versionsOf !== null} onOpenChange={(open) => !open && setVersionsOf(null)}>
        {versionsOf && <VersionsDialog file={versionsOf} />}
      </Dialog>
      {deleting && <DeleteDialog file={deleting} onClose={() => setDeleting(null)} />}
    </div>
  );
}

function UploadDialog({
  ownerType,
  ownerId,
  replaces,
  onClose,
}: {
  ownerType: string;
  ownerId: string;
  replaces?: StoredFile;
  onClose: () => void;
}) {
  const { t } = useTranslation('files');
  const { t: tc } = useTranslation();
  const upload = useUploadAttachment(ownerType, ownerId);
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={replaces ? t('newVersionTitle', { name: replaces.name }) : t('uploadTitle')}
        description={t('allowed')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!file}
              loading={upload.isPending}
              onClick={() =>
                file &&
                upload.mutate(
                  { file, comment: comment.trim() || undefined, replaces: replaces?.id },
                  {
                    onSuccess: () => {
                      toast({ title: t('uploaded') });
                      onClose();
                    },
                    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                  },
                )
              }
            >
              {t('upload')}
            </Button>
          </>
        }
      >
        <FileDrop
          label={t('chooseFile')}
          hint={t('allowedHint')}
          accept={ACCEPT}
          file={file}
          onFile={setFile}
        />
        <FormField label={t('comment')}>
          <Input value={comment} maxLength={500} onChange={(e) => setComment(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

function VersionsDialog({ file }: { file: StoredFile }) {
  const { t, i18n } = useTranslation('files');
  const versions = useFileVersions(file.id);
  return (
    <DialogContent title={t('versionsTitle', { name: file.name })} className="max-w-2xl">
      {versions.isPending ? (
        <Skeleton className="h-32" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('version')}</TableHeaderCell>
              <TableHeaderCell>{t('updated')}</TableHeaderCell>
              <TableHeaderCell>{t('size')}</TableHeaderCell>
              <TableHeaderCell>{t('comment')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('downloadVersion')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(versions.data ?? []).map((version) => (
              <TableRow key={version.id}>
                <TableCell>{formatNumber(version.versionNo, i18n.language)}</TableCell>
                <TableCell className="whitespace-nowrap">
                  {formatDateTime(version.uploadedAt, i18n.language)}
                </TableCell>
                <TableCell>{formatSize(version.sizeBytes, i18n.language)}</TableCell>
                <TableCell dir="auto">{version.comment ?? '—'}</TableCell>
                <TableCell>
                  <a
                    className={buttonVariants({ variant: 'ghost', size: 'icon' })}
                    href={contentUrl(file.id, version.id, true)}
                    aria-label={t('downloadVersion')}
                  >
                    <Download aria-hidden />
                  </a>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </DialogContent>
  );
}

function DeleteDialog({ file, onClose }: { file: StoredFile; onClose: () => void }) {
  const { t } = useTranslation('files');
  const { t: tc } = useTranslation();
  const remove = useDeleteFile();
  const toast = useToast();
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('deleteTitle', { name: file.name })}
        description={t('deleteDescription')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={!reason.trim()}
              loading={remove.isPending}
              onClick={() =>
                remove.mutate(
                  { fileId: file.id, reason: reason.trim() },
                  {
                    onSuccess: () => {
                      toast({ title: t('deleted') });
                      onClose();
                    },
                    onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
                  },
                )
              }
            >
              {t('deleteConfirm')}
            </Button>
          </>
        }
      >
        <FormField label={t('reason')} required>
          <Input value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

function formatSize(bytes: number, language: string): string {
  if (bytes < 1024) return `${formatNumber(bytes, language)} B`;
  if (bytes < 1024 * 1024) return `${formatNumber(Math.round(bytes / 102.4) / 10, language)} KB`;
  return `${formatNumber(Math.round(bytes / (1024 * 104.8576)) / 10, language)} MB`;
}
