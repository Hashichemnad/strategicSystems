import { Mail, RotateCcw } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useServerErrors } from '@/features/settings/common';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import { useDeliveries, useResetTemplate, useSaveTemplate, useTemplates, type Template } from './api';

/** Settings → Notifications: AR / EN templates (plain text with placeholders, previewed in the bilingual layout) and the delivery log. */
export function TemplatesPage() {
  const { t, i18n } = useTranslation('notifications');
  const { t: tc } = useTranslation();
  const templates = useTemplates();
  const [editing, setEditing] = useState<Template | null>(null);
  const [page, setPage] = useState(1);
  const deliveries = useDeliveries(page);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('templates.title')} description={t('templates.description')} />
      <Tabs defaultValue="templates">
        <TabsList>
          <TabsTrigger value="templates">{t('templates.tab')}</TabsTrigger>
          <TabsTrigger value="deliveries">{t('deliveries.tab')}</TabsTrigger>
        </TabsList>
        <TabsContent value="templates">
          {templates.isPending ? (
            <Skeleton className="h-64" />
          ) : templates.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, templates.error)} />
          ) : (
            <div className="overflow-x-auto rounded-lg border bg-card">
              <Table>
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('templates.name')}</TableHeaderCell>
                    <TableHeaderCell>{t('templates.subject')}</TableHeaderCell>
                    <TableHeaderCell>{t('templates.system')}</TableHeaderCell>
                    <TableHeaderCell>
                      <span className="sr-only">{tc('actions.edit')}</span>
                    </TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {templates.data.map((template) => (
                    <TableRow key={template.key}>
                      <TableCell>
                        <div className="font-medium">
                          {pickText(i18n.language, template.nameAr, template.nameEn)}
                        </div>
                        <div className="font-mono text-xs text-muted-foreground" dir="ltr">
                          {template.key}
                        </div>
                      </TableCell>
                      <TableCell dir="auto">
                        {i18n.language === 'ar' ? template.subjectAr : template.subjectEn}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          <StatusChip>{template.systemCode}</StatusChip>
                          {template.mandatory && (
                            <StatusChip tone="warning">{t('templates.mandatory')}</StatusChip>
                          )}
                          {template.customised && (
                            <StatusChip tone="info">{t('templates.customised')}</StatusChip>
                          )}
                        </div>
                      </TableCell>
                      <TableCell>
                        <Button variant="outline" size="sm" onClick={() => setEditing(template)}>
                          {tc('actions.edit')}
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </TabsContent>
        <TabsContent value="deliveries">
          {deliveries.isPending ? (
            <Skeleton className="h-64" />
          ) : deliveries.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, deliveries.error)} />
          ) : (
            <div className="flex flex-col gap-3">
              <div className="overflow-x-auto rounded-lg border bg-card">
                <Table>
                  <TableHead>
                    <TableRow>
                      <TableHeaderCell>{t('deliveries.when')}</TableHeaderCell>
                      <TableHeaderCell>{t('deliveries.to')}</TableHeaderCell>
                      <TableHeaderCell>{t('deliveries.template')}</TableHeaderCell>
                      <TableHeaderCell>{t('deliveries.status')}</TableHeaderCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {deliveries.data.items.map((delivery) => (
                      <TableRow key={delivery.id}>
                        <TableCell className="whitespace-nowrap">
                          {formatDateTime(delivery.at, i18n.language)}
                        </TableCell>
                        <TableCell dir="ltr" className="text-start">
                          {delivery.to}
                        </TableCell>
                        <TableCell className="font-mono text-xs">{delivery.templateKey}</TableCell>
                        <TableCell>
                          <StatusChip tone={delivery.status === 'sent' ? 'success' : 'danger'}>
                            {t(`deliveries.statuses.${delivery.status as 'sent'}`)}
                          </StatusChip>
                          {delivery.error && (
                            <div className="mt-1 text-xs text-muted-foreground">{delivery.error}</div>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
              <Pagination
                page={page}
                totalPages={Math.max(1, Math.ceil(deliveries.data.totalCount / deliveries.data.pageSize))}
                totalCount={deliveries.data.totalCount}
                onPageChange={setPage}
              />
            </div>
          )}
        </TabsContent>
      </Tabs>
      {editing && <TemplateDialog template={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function TemplateDialog({ template, onClose }: { template: Template; onClose: () => void }) {
  const { t, i18n } = useTranslation('notifications');
  const { t: tc } = useTranslation();
  const save = useSaveTemplate();
  const reset = useResetTemplate();
  const toast = useToast();
  const errors = useServerErrors();
  const [draft, setDraft] = useState({
    subjectAr: template.subjectAr,
    subjectEn: template.subjectEn,
    bodyAr: template.bodyAr,
    bodyEn: template.bodyEn,
    buttonAr: template.buttonAr ?? '',
    buttonEn: template.buttonEn ?? '',
  });
  const [version, setVersion] = useState(template.version);
  const [previewKey, setPreviewKey] = useState(0);
  const set = (key: keyof typeof draft) => (value: string) => setDraft({ ...draft, [key]: value });

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={pickText(i18n.language, template.nameAr, template.nameEn)}
        description={t('templates.placeholders', {
          list: template.placeholders.map((p) => `{{${p}}}`).join(' ') || '—',
        })}
        className="max-w-5xl"
        footer={
          <>
            {template.customised && (
              <Button
                variant="outline"
                loading={reset.isPending}
                onClick={() =>
                  reset.mutate(
                    { key: template.key, version },
                    {
                      onSuccess: (saved) => {
                        setVersion(saved.version);
                        setDraft({
                          subjectAr: saved.subjectAr,
                          subjectEn: saved.subjectEn,
                          bodyAr: saved.bodyAr,
                          bodyEn: saved.bodyEn,
                          buttonAr: saved.buttonAr ?? '',
                          buttonEn: saved.buttonEn ?? '',
                        });
                        setPreviewKey((k) => k + 1);
                        toast({ title: t('templates.resetDone') });
                      },
                      onError: errors.fail,
                    },
                  )
                }
              >
                <RotateCcw aria-hidden />
                {t('templates.reset')}
              </Button>
            )}
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    key: template.key,
                    ...draft,
                    buttonAr: draft.buttonAr || null,
                    buttonEn: draft.buttonEn || null,
                    version,
                  },
                  {
                    onSuccess: (saved) => {
                      errors.clear();
                      setVersion(saved.version);
                      setPreviewKey((k) => k + 1);
                      toast({ title: t('templates.saved') });
                    },
                    onError: errors.fail,
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-4 lg:grid-cols-2">
          <div className="flex flex-col gap-3" dir="rtl" lang="ar">
            <FormField label={t('templates.subjectAr')} error={errors.errorFor('subjectAr')}>
              <Input value={draft.subjectAr} onChange={(e) => set('subjectAr')(e.target.value)} />
            </FormField>
            <FormField
              label={t('templates.bodyAr')}
              hint={t('templates.bodyHint')}
              error={errors.errorFor('bodyAr')}
            >
              <Textarea rows={7} value={draft.bodyAr} onChange={(e) => set('bodyAr')(e.target.value)} />
            </FormField>
            <FormField label={t('templates.buttonAr')} error={errors.errorFor('buttonAr')}>
              <Input value={draft.buttonAr} onChange={(e) => set('buttonAr')(e.target.value)} />
            </FormField>
          </div>
          <div className="flex flex-col gap-3" dir="ltr" lang="en">
            <FormField label={t('templates.subjectEn')} error={errors.errorFor('subjectEn')}>
              <Input value={draft.subjectEn} onChange={(e) => set('subjectEn')(e.target.value)} />
            </FormField>
            <FormField
              label={t('templates.bodyEn')}
              hint={t('templates.bodyHint')}
              error={errors.errorFor('bodyEn')}
            >
              <Textarea rows={7} value={draft.bodyEn} onChange={(e) => set('bodyEn')(e.target.value)} />
            </FormField>
            <FormField label={t('templates.buttonEn')} error={errors.errorFor('buttonEn')}>
              <Input value={draft.buttonEn} onChange={(e) => set('buttonEn')(e.target.value)} />
            </FormField>
          </div>
        </div>
        <section aria-label={t('templates.preview')} className="flex flex-col gap-2">
          <h3 className="flex items-center gap-2 font-semibold">
            <Mail className="size-4" aria-hidden />
            {t('templates.preview')}
          </h3>
          <iframe
            key={previewKey}
            title={t('templates.preview')}
            src={`/api/v1/notifications/templates/${encodeURIComponent(template.key)}/preview.html?v=${previewKey}`}
            sandbox=""
            className="h-96 w-full rounded border bg-white"
          />
        </section>
      </DialogContent>
    </Dialog>
  );
}
