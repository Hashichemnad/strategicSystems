import { Link } from '@tanstack/react-router';
import { Inbox, ListChecks, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { normaliseSearch } from './search';
import { pickText } from '@/lib/format';
import { Button, EmptyState, Input, PageHeader, Skeleton, StatusBanner } from '@/ui';
import { useCatalogue, type CatalogueType } from './api';
import { TypeIcon } from './components';

/** Request catalogue (docs/design/requests.md §2.7): the types the user may submit, grouped by category, searchable. */
export function RequestCataloguePage() {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const catalogue = useCatalogue();
  const [search, setSearch] = useState('');
  const lang = i18n.language;

  const groups = useMemo(() => {
    if (!catalogue.data) return [];
    const term = normaliseSearch(search);
    const matches = (type: CatalogueType) =>
      !term ||
      [type.nameAr, type.nameEn, type.descriptionAr, type.descriptionEn, type.code].some((value) =>
        normaliseSearch(value ?? '').includes(term),
      );
    const types = catalogue.data.types.filter(matches);
    const categories = catalogue.data.categories;
    const known = new Set(categories.map((c) => c.code));
    const result = categories
      .map((category) => ({
        code: category.code,
        label: pickText(lang, category.labelAr, category.labelEn) || category.code,
        types: types.filter((type) => type.category === category.code),
      }))
      .filter((group) => group.types.length > 0);
    const other = types.filter((type) => !type.category || !known.has(type.category));
    if (other.length > 0) result.push({ code: '-', label: t('catalogue.other'), types: other });
    return result;
  }, [catalogue.data, search, lang, t]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('catalogue.title')}
        description={t('catalogue.description')}
        actions={
          <>
            <Button variant="outline" asChild>
              <Link to="/requests/mine">
                <Inbox aria-hidden />
                {t('nav.mine')}
              </Link>
            </Button>
            <Button variant="outline" asChild>
              <Link to="/requests/queue">
                <ListChecks aria-hidden />
                {t('nav.queue')}
              </Link>
            </Button>
          </>
        }
      />
      <div className="relative max-w-md">
        <Search
          className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden
        />
        <Input
          type="search"
          className="ps-9"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('catalogue.search')}
          aria-label={t('catalogue.search')}
        />
      </div>
      {catalogue.isPending ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-36 rounded-lg" />
          ))}
        </div>
      ) : catalogue.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, catalogue.error)} />
      ) : groups.length === 0 ? (
        <EmptyState title={search ? t('catalogue.emptySearch') : t('catalogue.empty')} />
      ) : (
        groups.map((group) => (
          <section
            key={group.code}
            className="flex flex-col gap-3"
            aria-labelledby={`category-${group.code}`}
          >
            <h2 id={`category-${group.code}`} className="text-lg font-semibold">
              {group.label}
            </h2>
            <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {group.types.map((type) => (
                <li
                  key={type.code}
                  className="flex flex-col gap-3 rounded-lg border bg-card p-4"
                  data-testid={`catalogue-type-${type.code}`}
                >
                  <div className="flex items-start gap-3">
                    <span className="flex size-10 shrink-0 items-center justify-center rounded-md bg-accent text-primary">
                      <TypeIcon name={type.icon} className="size-5" />
                    </span>
                    <div className="flex min-w-0 flex-col gap-1">
                      <h3 className="font-medium" dir="auto">
                        {pickText(lang, type.nameAr, type.nameEn) || type.code}
                      </h3>
                      <p className="line-clamp-3 text-sm text-muted-foreground" dir="auto">
                        {pickText(lang, type.descriptionAr, type.descriptionEn)}
                      </p>
                    </div>
                  </div>
                  <div className="mt-auto flex justify-end">
                    <Button size="sm" asChild>
                      <Link to="/requests/new/$typeCode" params={{ typeCode: type.code }} search={{}}>
                        {t('catalogue.start')}
                      </Link>
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        ))
      )}
    </div>
  );
}
