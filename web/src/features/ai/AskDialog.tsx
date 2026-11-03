import { Link } from '@tanstack/react-router';
import { Send, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { pickText } from '@/lib/format';
import { Button, Dialog, DialogContent, StatusBanner, Textarea } from '@/ui';
import { AiBadge } from './AiDraft';
import { useAiFeature, useAsk, type AskAnswer } from './api';

interface Turn {
  role: 'user' | 'assistant';
  text: string;
  answer?: AskAnswer;
}

/** "Ask the system" (SPEC §0.5): questions over the data the user may see, through read-only tools run with the user's permissions. */
export function AskButton() {
  const { t } = useTranslation('ai');
  const enabled = useAiFeature('ask');
  const [open, setOpen] = useState(false);
  if (!enabled) return null;
  return (
    <>
      <Button
        variant="ghost"
        onClick={() => setOpen(true)}
        aria-label={t('ask.title')}
        data-testid="ask-button"
      >
        <Sparkles aria-hidden />
        <span className="hidden sm:inline">{t('ask.button')}</span>
      </Button>
      <AskDialog open={open} onOpenChange={setOpen} />
    </>
  );
}

function AskDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t, i18n } = useTranslation('ai');
  const { t: tc } = useTranslation();
  const ask = useAsk();
  const [turns, setTurns] = useState<Turn[]>([]);
  const [question, setQuestion] = useState('');
  const send = () => {
    const text = question.trim();
    if (!text) return;
    const history = turns.map((turn) => ({ role: turn.role, text: turn.text }));
    setTurns((current) => [...current, { role: 'user', text }]);
    setQuestion('');
    ask.mutate(
      { question: text, history },
      {
        onSuccess: (answer) =>
          setTurns((current) => [...current, { role: 'assistant', text: answer.answer, answer }]),
      },
    );
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={t('ask.title')} description={t('ask.description')} className="max-w-2xl">
        <div className="flex flex-col gap-3">
          <div
            className="flex max-h-96 min-h-32 flex-col gap-3 overflow-y-auto rounded-md border bg-muted/30 p-3"
            aria-live="polite"
          >
            {turns.length === 0 && <p className="text-sm text-muted-foreground">{t('ask.empty')}</p>}
            {turns.map((turn, index) => (
              <div
                key={index}
                className={cn(
                  'flex max-w-[85%] flex-col gap-1 rounded-lg p-3 text-sm',
                  turn.role === 'user'
                    ? 'self-end bg-primary text-primary-foreground'
                    : 'self-start border bg-card',
                )}
              >
                {turn.role === 'assistant' && <AiBadge label={t('generated')} className="self-start" />}
                <p className="whitespace-pre-wrap" dir="auto">
                  {turn.text}
                </p>
                {turn.answer && turn.answer.references.length > 0 && (
                  <div className="mt-1 flex flex-col gap-1">
                    <span className="text-xs text-muted-foreground">{t('ask.references')}</span>
                    {turn.answer.references.map((reference) => (
                      <Link
                        key={reference.id}
                        to={reference.link}
                        className="text-xs text-primary hover:underline"
                        onClick={() => onOpenChange(false)}
                      >
                        {reference.number ? `${reference.number} · ` : ''}
                        {pickText(i18n.language, reference.titleAr, reference.titleEn)}
                      </Link>
                    ))}
                  </div>
                )}
                {turn.answer && turn.answer.toolCalls.length > 0 && (
                  <span className="text-xs text-muted-foreground">
                    {t('ask.tools', {
                      tools: [...new Set(turn.answer.toolCalls.map((c) => c.tool))].join(', '),
                    })}
                  </span>
                )}
              </div>
            ))}
            {ask.isPending && <p className="text-sm text-muted-foreground">{t('ask.thinking')}</p>}
          </div>
          {ask.isError && <StatusBanner tone="danger" title={errorMessage(tc, ask.error)} />}
          <form
            className="flex items-end gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              send();
            }}
          >
            <Textarea
              className="min-h-16 flex-1"
              dir="auto"
              maxLength={1000}
              value={question}
              placeholder={t('ask.placeholder')}
              aria-label={t('ask.title')}
              onChange={(event) => setQuestion(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault();
                  send();
                }
              }}
            />
            <Button type="submit" loading={ask.isPending} disabled={!question.trim()}>
              <Send className="rtl:-scale-x-100" aria-hidden />
              {t('ask.send')}
            </Button>
          </form>
          {turns.length > 0 && (
            <Button variant="link" className="self-start px-0" onClick={() => setTurns([])}>
              {t('ask.clear')}
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
