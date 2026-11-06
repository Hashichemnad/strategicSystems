import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { formatDate } from '@/lib/format';
import { DialogContent, Skeleton } from '@/ui';

type Block = { kind: 'h1' | 'h2' | 'h3' | 'p'; text: string } | { kind: 'ul'; items: string[] };

/**
 * The small Markdown subset of the release notes (headings, bullet lists, paragraphs; a line indented under a bullet continues it).
 * Rendered as React text nodes only — never as HTML.
 */
export function parseReleaseNotes(markdown: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: string[] | null = null;
  const flush = () => {
    if (paragraph.length) blocks.push({ kind: 'p', text: paragraph.join(' ') });
    paragraph = [];
    if (list) blocks.push({ kind: 'ul', items: list });
    list = null;
  };
  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trimEnd();
    const heading = /^(#{1,3})\s+(.*)$/.exec(line);
    const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
    if (!line.trim()) {
      flush();
    } else if (heading) {
      flush();
      const [, hashes = '#', title = ''] = heading;
      blocks.push({ kind: `h${Math.min(hashes.length, 3)}` as 'h1' | 'h2' | 'h3', text: title });
    } else if (bullet) {
      if (paragraph.length) {
        blocks.push({ kind: 'p', text: paragraph.join(' ') });
        paragraph = [];
      }
      (list ??= []).push(bullet[1] ?? '');
    } else if (list && list.length > 0 && /^\s+/.test(raw)) {
      list[list.length - 1] = `${list[list.length - 1] ?? ''} ${line.trim()}`;
    } else {
      if (list) {
        blocks.push({ kind: 'ul', items: list });
        list = null;
      }
      paragraph.push(line.trim());
    }
  }
  flush();
  return blocks;
}

function renderBlock(block: Block, index: number): ReactNode {
  switch (block.kind) {
    case 'h1':
      return (
        <h3 key={index} className="text-base font-semibold">
          {block.text}
        </h3>
      );
    case 'h2':
    case 'h3':
      return (
        <h4 key={index} className="text-sm font-semibold">
          {block.text}
        </h4>
      );
    case 'ul':
      return (
        <ul key={index} className="list-disc ps-5 text-sm">
          {block.items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      );
    default:
      return (
        <p key={index} className="text-sm">
          {block.text}
        </p>
      );
  }
}

/** About: version of this installation and its release notes in the user's language (ADR 0152). Mount inside a <Dialog>. */
export function AboutDialogContent() {
  const { t, i18n } = useTranslation('setup');
  const language = i18n.language === 'ar' ? 'ar' : 'en';
  const version = useQuery({
    queryKey: ['system', 'version'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/system/version', { signal })),
    staleTime: Infinity,
  });
  const notes = useQuery({
    queryKey: ['system', 'release-notes', language],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/system/release-notes', { params: { query: { language } }, signal })),
    staleTime: Infinity,
  });
  return (
    <DialogContent title={t('about.title')} className="max-w-2xl">
      {version.data ? (
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm" data-testid="about-version">
          <dt className="text-muted-foreground">{t('about.version')}</dt>
          <dd dir="ltr" className="text-start">
            {version.data.version}
          </dd>
          {version.data.commit && (
            <>
              <dt className="text-muted-foreground">{t('about.commit')}</dt>
              <dd dir="ltr" className="font-mono text-start">
                {version.data.commit}
              </dd>
            </>
          )}
          <dt className="text-muted-foreground">{t('about.built')}</dt>
          <dd>{formatDate(new Date(version.data.buildDate), i18n.language)}</dd>
        </dl>
      ) : (
        <Skeleton className="h-16" />
      )}
      <section aria-labelledby="release-notes-title" className="flex flex-col gap-3 border-t pt-4">
        <h3 id="release-notes-title" className="text-sm font-semibold text-muted-foreground">
          {t('about.releaseNotes')}
        </h3>
        {notes.data ? (
          notes.data.map((note) => (
            <article key={note.version} lang={note.language} className="flex flex-col gap-2">
              {parseReleaseNotes(note.markdown).map(renderBlock)}
            </article>
          ))
        ) : (
          <Skeleton className="h-32" />
        )}
      </section>
    </DialogContent>
  );
}
