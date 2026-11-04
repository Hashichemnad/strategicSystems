import { FolderTree } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import { Button, FormField, Select, useToast } from '@/ui';
import { useFeature } from '../featuresApi';
import type { ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { usePortfolioGroups, useSetGrouping } from './viewsApi';

const NONE = 'none';

/** Project overview card: the project's portfolio / programme; PM Office (canManage) changes them (§35). */
export function GroupingCard({
  project,
}: {
  project: Pick<ProjectFull, 'id' | 'version' | 'permissions' | 'delivery'>;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const toast = useToast();
  const on = useFeature('portfolios');
  const groups = usePortfolioGroups(on);
  const save = useSetGrouping(project.id);
  const [editing, setEditing] = useState(false);
  const [portfolioId, setPortfolioId] = useState<string | null>(project.delivery.portfolioGroupId);
  const [programmeId, setProgrammeId] = useState<string | null>(project.delivery.programmeId);
  if (!on) return null;

  const list = groups.data ?? [];
  const byId = new Map(list.map((g) => [g.id, g]));
  const label = (id: string | null) => {
    const g = id ? byId.get(id) : undefined;
    return g ? `${g.code} · ${pickText(i18n.language, g.nameAr, g.nameEn)}` : t('grouping.none');
  };
  const portfolios = list.filter((g) => g.kind === 'portfolio' && g.status === 'active');
  const programmes = list.filter(
    (g) => g.kind === 'programme' && g.status === 'active' && (!portfolioId || g.parentId === portfolioId),
  );
  const canChange = project.permissions.canManage;

  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4" data-testid="project-grouping">
      <div className="flex items-center justify-between gap-2">
        <h3 className="flex items-center gap-2 font-semibold">
          <FolderTree className="size-4" aria-hidden />
          {t('grouping.title')}
        </h3>
        {canChange && !editing && (
          <Button
            variant="outline"
            size="sm"
            data-testid="grouping-edit"
            onClick={() => {
              setPortfolioId(project.delivery.portfolioGroupId);
              setProgrammeId(project.delivery.programmeId);
              setEditing(true);
            }}
          >
            {t('grouping.change')}
          </Button>
        )}
      </div>
      {!editing ? (
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted-foreground">{t('grouping.portfolio')}</dt>
          <dd data-testid="grouping-portfolio">{label(project.delivery.portfolioGroupId)}</dd>
          <dt className="text-muted-foreground">{t('grouping.programme')}</dt>
          <dd data-testid="grouping-programme">{label(project.delivery.programmeId)}</dd>
        </dl>
      ) : (
        <div className="flex flex-col gap-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <FormField label={t('grouping.portfolio')}>
              <Select
                value={portfolioId ?? NONE}
                onValueChange={(v) => {
                  setPortfolioId(v === NONE ? null : v);
                  setProgrammeId(null);
                }}
                options={[
                  { value: NONE, label: t('grouping.none') },
                  ...portfolios.map((g) => ({ value: g.id, label: label(g.id) })),
                ]}
              />
            </FormField>
            <FormField label={t('grouping.programme')} hint={t('grouping.programmeHint')}>
              <Select
                value={programmeId ?? NONE}
                onValueChange={(v) => setProgrammeId(v === NONE ? null : v)}
                options={[
                  { value: NONE, label: t('grouping.none') },
                  ...programmes.map((g) => ({ value: g.id, label: label(g.id) })),
                ]}
              />
            </FormField>
          </div>
          {save.isError && <FieldErrors error={save.error} />}
          <div className="flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setEditing(false)}>
              {t('grouping.cancel')}
            </Button>
            <Button
              size="sm"
              loading={save.isPending}
              data-testid="grouping-save"
              onClick={() =>
                save.mutate(
                  { portfolioGroupId: portfolioId, programmeId, version: project.version },
                  {
                    onSuccess: () => {
                      setEditing(false);
                      toast({ title: t('grouping.saved') });
                    },
                  },
                )
              }
            >
              {t('grouping.save')}
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}
