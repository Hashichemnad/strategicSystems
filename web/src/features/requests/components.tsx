import {
  Archive,
  Award,
  BookOpen,
  Briefcase,
  Calendar,
  ClipboardList,
  File,
  FileText,
  Inbox,
  Lightbulb,
  Mail,
  Scale,
  Shield,
  Users,
  Wrench,
  type LucideIcon,
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import { pickText } from '@/lib/format';
import { StatusChip } from '@/ui';
import { statusTone } from './api';
import type { FieldMessage } from './formLogic';

/** Icons a request type may name (lucide names); unknown names fall back to a document. */
export const typeIcons: Record<string, LucideIcon> = {
  file: File,
  'file-text': FileText,
  archive: Archive,
  lightbulb: Lightbulb,
  'clipboard-list': ClipboardList,
  inbox: Inbox,
  briefcase: Briefcase,
  shield: Shield,
  users: Users,
  calendar: Calendar,
  'book-open': BookOpen,
  award: Award,
  scale: Scale,
  wrench: Wrench,
  mail: Mail,
};

export function TypeIcon({ name, className }: { name: string | null | undefined; className?: string }) {
  const Icon = typeIcons[name ?? ''] ?? FileText;
  return <Icon className={className} aria-hidden />;
}

/** Translation of a code known only at run time (status, event action, field type…), with a fallback. */
export function useDynamicT() {
  const { t } = useTranslation('requests');
  const dynamic = t as unknown as (key: string, options?: Record<string, unknown>) => string;
  return (key: string, fallback?: string, options?: Record<string, unknown>) =>
    dynamic(key, { defaultValue: fallback ?? key, ...(options ?? {}) });
}

export function RequestStatusChip({ status }: { status: string }) {
  const dt = useDynamicT();
  return <StatusChip tone={statusTone[status] ?? 'neutral'}>{dt(`status.${status}`, status)}</StatusChip>;
}

/** Messages of errors that are not about one form field. */
export function MessageList({ messages }: { messages: FieldMessage[] }) {
  const { t: tc } = useTranslation();
  if (messages.length === 0) return null;
  return (
    <ul className="list-disc ps-5">
      {messages.map((m, i) => (
        <li key={i}>{translateMessageKey(tc, m.messageKey, m.params)}</li>
      ))}
    </ul>
  );
}

export function typeName(
  language: string,
  type: { nameAr: string | null; nameEn: string | null; code?: string },
) {
  return pickText(language, type.nameAr, type.nameEn) || (type.code ?? '');
}
