import { Link } from '@tanstack/react-router';
import { FileStack, Plus } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  CopyField,
  EmptyState,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { useRequestTypes } from './api';
import { TypeIcon } from './components';

/** Absolute address of an in-app path, for pasting into the intranet. */
export function absoluteUrl(path: string): string {
  return `${window.location.origin}${path}`;
}

/** Settings → Request types (docs/design/requests.md §2.1): built-in and custom types with their deep and embed links. */
export function RequestTypesPage() {
  const { t, i18n } = useTranslation('requests');
  const { t: tc } = useTranslation();
  const types = useRequestTypes();
  const lang = i18n.language;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('types.title')}
        description={t('types.description')}
        actions={
          <Button asChild data-testid="types-new">
            <Link to="/settings/request-types/$code" params={{ code: 'new' }}>
              <Plus aria-hidden />
              {t('types.new')}
            </Link>
          </Button>
        }
      />
      {types.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : types.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, types.error)} />
      ) : types.data.length === 0 ? (
        <EmptyState icon={FileStack} title={t('types.empty')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table data-testid="types-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('types.name')}</TableHeaderCell>
                <TableHeaderCell>{t('types.kind')}</TableHeaderCell>
                <TableHeaderCell>{t('types.state')}</TableHeaderCell>
                <TableHeaderCell className="text-end">{t('types.fields')}</TableHeaderCell>
                <TableHeaderCell className="text-end">{t('types.requests')}</TableHeaderCell>
                <TableHeaderCell className="min-w-72">{t('types.links')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {types.data.map((type) => (
                <TableRow key={type.code} data-testid={`type-row-${type.code}`}>
                  <TableCell>
                    <Link
                      to="/settings/request-types/$code"
                      params={{ code: type.code }}
                      className="flex items-center gap-2 font-medium text-primary hover:underline"
                    >
                      <TypeIcon name={type.icon} className="size-4 shrink-0" />
                      <span dir="auto">{pickText(lang, type.nameAr, type.nameEn) || type.code}</span>
                    </Link>
                    <code dir="ltr" className="text-xs text-muted-foreground">
                      {type.code}
                    </code>
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={type.kind === 'builtin' ? 'info' : 'neutral'}>
                      {type.kind === 'builtin' ? t('types.builtin') : t('types.custom')}
                    </StatusChip>
                  </TableCell>
                  <TableCell>
                    <span className="flex flex-wrap gap-1">
                      <StatusChip tone={type.active ? 'success' : 'neutral'}>
                        {type.active ? t('types.active') : t('types.inactive')}
                      </StatusChip>
                      {type.showInCatalogue && (
                        <StatusChip tone="neutral">{t('types.inCatalogue')}</StatusChip>
                      )}
                      {type.embeddable && <StatusChip tone="neutral">{t('types.embeddable')}</StatusChip>}
                    </span>
                  </TableCell>
                  <TableCell className="text-end tabular-nums">{formatNumber(type.fields, lang)}</TableCell>
                  <TableCell className="text-end tabular-nums">{formatNumber(type.requests, lang)}</TableCell>
                  <TableCell>
                    <div className="flex flex-col gap-1">
                      <CopyField
                        label={t('types.deepLink')}
                        value={absoluteUrl(`/requests/new/${type.code}`)}
                      />
                      {type.embeddable && (
                        <CopyField
                          label={t('types.embedLink')}
                          value={absoluteUrl(`/embed/requests/${type.code}`)}
                        />
                      )}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
