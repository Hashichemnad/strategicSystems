import { Extension } from '@tiptap/core';
import { TableKit } from '@tiptap/extension-table';
import { EditorContent, useEditor, useEditorState, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import {
  Bold,
  Code,
  Heading2,
  Heading3,
  Italic,
  Link2,
  List,
  ListOrdered,
  Pilcrow,
  Quote,
  Redo2,
  Strikethrough,
  Table as TableIcon,
  Underline,
  Undo2,
  type LucideIcon,
} from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';
import { toEditorHtml } from './rich-text';

export type TextDirection = 'rtl' | 'ltr' | 'auto';

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    paragraphDirection: {
      /** Sets `dir` on the paragraphs, headings, quotes, list items and table cells of the selection. */
      setDirection: (direction: TextDirection) => ReturnType;
    };
  }
}

const DIRECTION_TYPES = ['paragraph', 'heading', 'blockquote', 'listItem', 'tableCell', 'tableHeader'];

/**
 * Per-paragraph direction (ADR 0161): a `dir` attribute on block nodes, default from the UI language, so text written in
 * Arabic stays right-to-left wherever it is shown later.
 */
export const ParagraphDirection = Extension.create<{ defaultDirection: TextDirection | null }>({
  name: 'paragraphDirection',
  addOptions() {
    return { defaultDirection: null };
  },
  addGlobalAttributes() {
    return [
      {
        types: DIRECTION_TYPES,
        attributes: {
          dir: {
            default: this.options.defaultDirection,
            parseHTML: (element) => {
              const dir = element.getAttribute('dir')?.toLowerCase();
              return dir === 'rtl' || dir === 'ltr' || dir === 'auto' ? dir : this.options.defaultDirection;
            },
            renderHTML: (attributes: { dir?: string | null }) =>
              attributes.dir ? { dir: attributes.dir } : {},
          },
        },
      },
    ];
  },
  addCommands() {
    return {
      setDirection:
        (direction) =>
        ({ commands }) =>
          DIRECTION_TYPES.map((type) => commands.updateAttributes(type, { dir: direction })).some(Boolean),
    };
  },
});

export interface RichTextEditorProps {
  value: string | null | undefined;
  /** Sanitised-on-the-server HTML of the content, or null when the editor is empty. */
  onChange: (value: string | null) => void;
  /** Accessible name of the text area (the visible label's text). */
  'aria-label'?: string;
  id?: string;
  'aria-invalid'?: boolean;
  'aria-describedby'?: string;
  disabled?: boolean;
  className?: string;
  /** Minimum height of the writing area (CSS length, default 8rem). */
  minHeight?: string;
}

interface ToolButton {
  key: string;
  icon: LucideIcon;
  label: string;
  active?: boolean;
  run: () => void;
  disabled?: boolean;
  /** Mirrored in right-to-left (directional icons such as undo / redo). */
  mirror?: boolean;
}

function useToolState(editor: Editor | null) {
  return useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e?.isActive('bold') ?? false,
      italic: e?.isActive('italic') ?? false,
      underline: e?.isActive('underline') ?? false,
      strike: e?.isActive('strike') ?? false,
      code: e?.isActive('code') ?? false,
      h2: e?.isActive('heading', { level: 2 }) ?? false,
      h3: e?.isActive('heading', { level: 3 }) ?? false,
      bullet: e?.isActive('bulletList') ?? false,
      ordered: e?.isActive('orderedList') ?? false,
      quote: e?.isActive('blockquote') ?? false,
      link: e?.isActive('link') ?? false,
      table: e?.isActive('table') ?? false,
      dir: (DIRECTION_TYPES.map((type) => e?.getAttributes(type)['dir'] as string | undefined).find(
        Boolean,
      ) ?? null) as TextDirection | null,
      canUndo: e?.can().undo() ?? false,
      canRedo: e?.can().redo() ?? false,
    }),
  });
}

/** Moves focus between toolbar buttons with the arrow keys, Home and End (WAI-ARIA toolbar pattern). */
function onToolbarKeyDown(event: KeyboardEvent<HTMLDivElement>, rtl: boolean) {
  const buttons = Array.from(
    event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not([disabled])'),
  );
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
  if (index < 0) return;
  const next = rtl ? 'ArrowLeft' : 'ArrowRight';
  const previous = rtl ? 'ArrowRight' : 'ArrowLeft';
  let target: number | undefined;
  if (event.key === next) target = (index + 1) % buttons.length;
  else if (event.key === previous) target = (index - 1 + buttons.length) % buttons.length;
  else if (event.key === 'Home') target = 0;
  else if (event.key === 'End') target = buttons.length - 1;
  if (target === undefined) return;
  event.preventDefault();
  buttons.forEach((button, i) => (button.tabIndex = i === target ? 0 : -1));
  buttons[target]?.focus();
}

function Separator() {
  return <span aria-hidden className="mx-0.5 h-6 w-px self-center bg-border" />;
}

function Tool({ tool, first }: { tool: ToolButton; first: boolean }) {
  const Icon = tool.icon;
  return (
    <button
      type="button"
      tabIndex={first ? 0 : -1}
      aria-label={tool.label}
      title={tool.label}
      aria-pressed={tool.active === undefined ? undefined : tool.active}
      disabled={tool.disabled}
      onMouseDown={(e) => e.preventDefault()}
      onClick={tool.run}
      className={cn(
        'inline-flex size-8 items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground disabled:opacity-40',
        tool.active && 'bg-accent text-accent-foreground',
      )}
    >
      <Icon aria-hidden className={cn('size-4', tool.mirror && 'rtl:-scale-x-100')} />
    </button>
  );
}

function TextTool({
  label,
  active,
  onClick,
  children,
}: {
  label: string;
  active?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      tabIndex={-1}
      title={label}
      aria-pressed={active}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      className={cn(
        'inline-flex h-8 min-w-8 items-center justify-center rounded-md px-1.5 text-xs font-semibold text-muted-foreground hover:bg-muted hover:text-foreground',
        active && 'bg-accent text-accent-foreground',
      )}
    >
      {/* The visible short text starts the accessible name, the full label completes it (WCAG 2.5.3 label in name). */}
      {children}
      <span className="sr-only"> ({label})</span>
    </button>
  );
}

const SAFE_LINK = /^(?:https?:\/\/|mailto:)/i;

/**
 * Rich-text editor (Tiptap, ADR 0161): bold, italic, underline, strike, code, headings, lists, quote, links, tables and the
 * direction of each paragraph. Controlled: `value` is HTML or legacy plain text, `onChange` receives HTML (or null when empty).
 * Loaded lazily through `RichTextEditor` in `rich-text-field.tsx`.
 */
export default function RichTextEditorImpl({
  value,
  onChange,
  id,
  disabled,
  className,
  minHeight = '8rem',
  ...aria
}: RichTextEditorProps) {
  const { t, i18n } = useTranslation();
  const uiDir: TextDirection = i18n.dir() === 'rtl' ? 'rtl' : 'ltr';
  const emitted = useRef<string | null | undefined>(value);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const [linkOpen, setLinkOpen] = useState(false);
  const [linkUrl, setLinkUrl] = useState('');
  const [linkError, setLinkError] = useState(false);
  const linkId = useId();

  const editor = useEditor({
    immediatelyRender: true,
    editable: !disabled,
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3, 4] },
        horizontalRule: false,
        codeBlock: false,
        link: {
          openOnClick: false,
          autolink: true,
          defaultProtocol: 'https',
          HTMLAttributes: { rel: 'noopener noreferrer', target: '_blank' },
          isAllowedUri: (url) => SAFE_LINK.test(url),
        },
      }),
      TableKit.configure({ table: { resizable: false } }),
      ParagraphDirection.configure({ defaultDirection: uiDir }),
    ],
    content: toEditorHtml(value),
    editorProps: {
      attributes: {
        ...(id ? { id } : {}),
        role: 'textbox',
        'aria-multiline': 'true',
        ...(aria['aria-label'] ? { 'aria-label': aria['aria-label'] } : {}),
        ...(aria['aria-invalid'] ? { 'aria-invalid': 'true' } : {}),
        ...(aria['aria-describedby'] ? { 'aria-describedby': aria['aria-describedby'] } : {}),
        dir: uiDir,
        class: 'rich-text rich-text-editing min-w-0 px-3 py-2 focus:outline-none',
        style: `min-height: ${minHeight}`,
      },
    },
    onUpdate: ({ editor: e }) => {
      const html = e.isEmpty ? null : e.getHTML();
      emitted.current = html;
      onChangeRef.current(html);
    },
  });

  // A new value from outside (form reset, AI draft accepted): load it without echoing it back.
  useEffect(() => {
    if (editor.isDestroyed || value === emitted.current) return;
    emitted.current = value;
    editor.commands.setContent(toEditorHtml(value), { emitUpdate: false });
  }, [editor, value]);

  useEffect(() => {
    editor.setEditable(!disabled);
  }, [editor, disabled]);

  const state = useToolState(editor);
  const chain = () => editor.chain().focus();

  const tools: (ToolButton | 'sep')[] = [
    {
      key: 'bold',
      icon: Bold,
      label: t('richText.bold'),
      active: state?.bold,
      run: () => chain().toggleBold().run(),
    },
    {
      key: 'italic',
      icon: Italic,
      label: t('richText.italic'),
      active: state?.italic,
      run: () => chain().toggleItalic().run(),
    },
    {
      key: 'underline',
      icon: Underline,
      label: t('richText.underline'),
      active: state?.underline,
      run: () => chain().toggleUnderline().run(),
    },
    {
      key: 'strike',
      icon: Strikethrough,
      label: t('richText.strike'),
      active: state?.strike,
      run: () => chain().toggleStrike().run(),
    },
    {
      key: 'code',
      icon: Code,
      label: t('richText.code'),
      active: state?.code,
      run: () => chain().toggleCode().run(),
    },
    'sep',
    {
      key: 'h2',
      icon: Heading2,
      label: t('richText.heading2'),
      active: state?.h2,
      run: () => chain().toggleHeading({ level: 2 }).run(),
    },
    {
      key: 'h3',
      icon: Heading3,
      label: t('richText.heading3'),
      active: state?.h3,
      run: () => chain().toggleHeading({ level: 3 }).run(),
    },
    {
      key: 'paragraph',
      icon: Pilcrow,
      label: t('richText.paragraph'),
      run: () => chain().setParagraph().run(),
    },
    'sep',
    {
      key: 'bullet',
      icon: List,
      label: t('richText.bulletList'),
      active: state?.bullet,
      run: () => chain().toggleBulletList().run(),
    },
    {
      key: 'ordered',
      icon: ListOrdered,
      label: t('richText.orderedList'),
      active: state?.ordered,
      run: () => chain().toggleOrderedList().run(),
    },
    {
      key: 'quote',
      icon: Quote,
      label: t('richText.quote'),
      active: state?.quote,
      run: () => chain().toggleBlockquote().run(),
    },
    'sep',
    {
      key: 'link',
      icon: Link2,
      label: t('richText.link'),
      active: state?.link,
      run: () => {
        setLinkUrl((editor.getAttributes('link')['href'] as string | undefined) ?? '');
        setLinkError(false);
        setLinkOpen((open) => !open);
      },
    },
    {
      key: 'table',
      icon: TableIcon,
      label: t('richText.insertTable'),
      active: state?.table,
      run: () => chain().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
    },
    'sep',
    {
      key: 'undo',
      icon: Undo2,
      label: t('richText.undo'),
      disabled: !state?.canUndo,
      mirror: true,
      run: () => chain().undo().run(),
    },
    {
      key: 'redo',
      icon: Redo2,
      label: t('richText.redo'),
      disabled: !state?.canRedo,
      mirror: true,
      run: () => chain().redo().run(),
    },
  ];

  const applyLink = () => {
    const url = linkUrl.trim();
    if (!url) {
      chain().extendMarkRange('link').unsetLink().run();
      setLinkOpen(false);
      return;
    }
    const href = /^[a-z][a-z0-9+.-]*:/i.test(url) ? url : `https://${url}`;
    if (!SAFE_LINK.test(href)) {
      setLinkError(true);
      return;
    }
    chain().extendMarkRange('link').setLink({ href }).run();
    setLinkOpen(false);
  };

  let firstButton = true;
  return (
    <div
      className={cn(
        'w-full rounded-md border border-input bg-card text-sm has-[[aria-invalid=true]]:border-danger focus-within:ring-2 focus-within:ring-ring',
        disabled && 'cursor-not-allowed opacity-50',
        className,
      )}
    >
      <div
        role="toolbar"
        aria-label={t('richText.toolbar')}
        aria-controls={id}
        onKeyDown={(e) => onToolbarKeyDown(e, uiDir === 'rtl')}
        className="flex flex-wrap items-center gap-0.5 border-b border-border px-1 py-1"
      >
        {tools.map((tool, index) => {
          if (tool === 'sep') return <Separator key={`sep-${index}`} />;
          const first = firstButton && !tool.disabled;
          if (first) firstButton = false;
          return <Tool key={tool.key} tool={tool} first={first} />;
        })}
        <Separator />
        <span role="group" aria-label={t('richText.direction')} className="inline-flex items-center gap-0.5">
          <TextTool
            label={t('richText.rtl')}
            active={state?.dir === 'rtl'}
            onClick={() => chain().setDirection('rtl').run()}
          >
            {t('richText.rtlShort')}
          </TextTool>
          <TextTool
            label={t('richText.ltr')}
            active={state?.dir === 'ltr'}
            onClick={() => chain().setDirection('ltr').run()}
          >
            {t('richText.ltrShort')}
          </TextTool>
          <TextTool
            label={t('richText.auto')}
            active={state?.dir === 'auto'}
            onClick={() => chain().setDirection('auto').run()}
          >
            {t('richText.autoShort')}
          </TextTool>
        </span>
        {state?.table && (
          <span
            role="group"
            aria-label={t('richText.tableTools')}
            className="inline-flex flex-wrap items-center gap-0.5"
          >
            <Separator />
            <TextTool label={t('richText.addRow')} onClick={() => chain().addRowAfter().run()}>
              {t('richText.addRowShort')}
            </TextTool>
            <TextTool label={t('richText.addColumn')} onClick={() => chain().addColumnAfter().run()}>
              {t('richText.addColumnShort')}
            </TextTool>
            <TextTool label={t('richText.deleteRow')} onClick={() => chain().deleteRow().run()}>
              {t('richText.deleteRowShort')}
            </TextTool>
            <TextTool label={t('richText.deleteColumn')} onClick={() => chain().deleteColumn().run()}>
              {t('richText.deleteColumnShort')}
            </TextTool>
            <TextTool label={t('richText.deleteTable')} onClick={() => chain().deleteTable().run()}>
              {t('richText.deleteTableShort')}
            </TextTool>
          </span>
        )}
      </div>
      {linkOpen && (
        <div className="flex flex-wrap items-center gap-2 border-b border-border px-2 py-2">
          <label htmlFor={linkId} className="text-xs font-medium">
            {t('richText.linkUrl')}
          </label>
          <input
            id={linkId}
            dir="ltr"
            type="url"
            value={linkUrl}
            aria-invalid={linkError || undefined}
            onChange={(e) => {
              setLinkUrl(e.target.value);
              setLinkError(false);
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                applyLink();
              } else if (e.key === 'Escape') {
                e.preventDefault();
                setLinkOpen(false);
                editor.commands.focus();
              }
            }}
            placeholder="https://"
            className="h-8 min-w-0 flex-1 rounded-md border border-input bg-card px-2 text-sm aria-invalid:border-danger"
          />
          <button
            type="button"
            onClick={applyLink}
            className="h-8 rounded-md bg-primary px-3 text-xs font-medium text-primary-foreground"
          >
            {t('richText.applyLink')}
          </button>
          <button
            type="button"
            onClick={() => {
              chain().extendMarkRange('link').unsetLink().run();
              setLinkOpen(false);
            }}
            className="h-8 rounded-md border border-input px-3 text-xs font-medium"
          >
            {t('richText.removeLink')}
          </button>
          {linkError && (
            <p role="alert" className="w-full text-xs text-danger">
              {t('richText.linkInvalid')}
            </p>
          )}
        </div>
      )}
      <EditorContent editor={editor} />
    </div>
  );
}
