import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  Select,
  Skeleton,
  StatusBanner,
  Textarea,
  useToast,
} from '@/ui';
import { useProjectMutation, type Milestone, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { standardsApi, useProjectTemplates } from './standardsApi';

/** Quality sign-off of a deliverable milestone by the owner or the PM Office (docs/design/portfolio.md §35). */
export function SignOffDialog({
  project,
  milestone,
  onClose,
}: {
  project: ProjectFull;
  milestone: Milestone;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const signOff = useProjectMutation(project.id, standardsApi.signOff);
  const [decision, setDecision] = useState<'accepted' | 'rejected'>('accepted');
  const [comment, setComment] = useState(milestone.qualityComment ?? '');
  const needsComment = decision === 'rejected' && comment.trim() === '';
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={`${t('quality.signOff')} — ${milestone.code}`}
        description={pickText(i18n.language, milestone.titleAr, milestone.titleEn)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant={decision === 'rejected' ? 'danger' : 'primary'}
              disabled={needsComment}
              loading={signOff.isPending}
              onClick={() =>
                signOff.mutate(
                  {
                    mid: milestone.id,
                    decision,
                    comment: comment.trim() || null,
                    version: milestone.version,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('saved') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t(`quality.decisions.${decision}`)}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="sign-off-dialog">
          {milestone.acceptanceCriteria && (
            <div className="rounded-md border bg-muted/40 p-3 text-sm">
              <p className="font-medium">{t('quality.acceptanceCriteria')}</p>
              <p className="whitespace-pre-line" dir="auto">
                {milestone.acceptanceCriteria}
              </p>
            </div>
          )}
          <FormField label={t('quality.decision')} required>
            <Select
              value={decision}
              onValueChange={(value) => setDecision(value === 'rejected' ? 'rejected' : 'accepted')}
              options={(['accepted', 'rejected'] as const).map((d) => ({
                value: d,
                label: t(`quality.decisions.${d}`),
              }))}
            />
          </FormField>
          <FormField label={t('quality.comment')} required={decision === 'rejected'}>
            <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {signOff.isError && <FieldErrors error={signOff.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Creates the template's milestones / planning items / risks / epics on a project in charter or planning draft. */
export function ApplyTemplateDialog({ project, onClose }: { project: ProjectFull; onClose: () => void }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const templates = useProjectTemplates();
  const apply = useProjectMutation(project.id, standardsApi.applyTemplate);
  const [templateId, setTemplateId] = useState<string | undefined>();
  const rows = (templates.data ?? []).filter((x) => x.status === 'active');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('templates.apply')}
        description={t('templates.applyHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!templateId}
              loading={apply.isPending}
              onClick={() =>
                templateId &&
                apply.mutate(
                  { id: project.id, templateId },
                  {
                    onSuccess: (result) => {
                      toast({
                        title: t('templates.applied', {
                          milestones: result.milestones,
                          planningItems: result.planningItems,
                          risks: result.risks,
                          epics: result.epics,
                        }),
                      });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('templates.applyAction')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="apply-template-dialog">
          {templates.isPending ? (
            <Skeleton className="h-16" />
          ) : templates.isError ? (
            <StatusBanner tone="danger" title={errorMessage(tc, templates.error)} />
          ) : rows.length === 0 ? (
            <EmptyState title={t('templates.none')} />
          ) : (
            <FormField label={t('templates.template')} required>
              <Select
                value={templateId}
                onValueChange={setTemplateId}
                options={rows.map((x) => ({
                  value: x.id,
                  label: `${x.code} · ${pickText(i18n.language, x.nameAr, x.nameEn)} (${t(
                    `templates.methodologies.${methodologyCode(x.methodology)}`,
                  )})`,
                }))}
              />
            </FormField>
          )}
          {apply.isError && <FieldErrors error={apply.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

const methodologyCode = (m: string) => (m === 'agile' || m === 'hybrid' ? m : 'waterfall');

/** PM Office saves the project's plan as a reusable template (code + names). */
export function SaveTemplateDialog({ project, onClose }: { project: ProjectFull; onClose: () => void }) {
  const { t } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, standardsApi.saveAsTemplate);
  const [v, setV] = useState({ code: '', nameAr: project.nameAr ?? '', nameEn: project.nameEn ?? '' });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('templates.saveAs')}
        description={t('templates.saveHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={v.code.trim() === ''}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    id: project.id,
                    code: v.code.trim(),
                    nameAr: v.nameAr.trim() || null,
                    nameEn: v.nameEn.trim() || null,
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('templates.saved') });
                      onClose();
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
        <div className="flex flex-col gap-3" data-testid="save-template-dialog">
          <FormField label={t('templates.code')} required>
            <Input dir="ltr" value={v.code} onChange={(e) => setV((c) => ({ ...c, code: e.target.value }))} />
          </FormField>
          <FormField label={t('templates.nameAr')}>
            <Input
              dir="rtl"
              value={v.nameAr}
              onChange={(e) => setV((c) => ({ ...c, nameAr: e.target.value }))}
            />
          </FormField>
          <FormField label={t('templates.nameEn')}>
            <Input
              dir="ltr"
              value={v.nameEn}
              onChange={(e) => setV((c) => ({ ...c, nameEn: e.target.value }))}
            />
          </FormField>
          {save.isError && <FieldErrors error={save.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}
