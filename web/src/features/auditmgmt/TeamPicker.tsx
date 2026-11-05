import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Input } from '@/ui';
import { useAuditTeams, type AuditTeamOption } from './api';
import { useNames } from './components';

/**
 * Engagement team picker: search as you type by team name or number (server search through the Teams contract, 20 matches at a time).
 * Lists only teams the user may read. Shows the chosen team's number and name; clearing sets null.
 */
export function AuditTeamPicker({
  value,
  onChange,
  testId,
}: {
  value: AuditTeamOption | null;
  onChange: (team: AuditTeamOption | null) => void;
  testId?: string;
}) {
  const { t } = useTranslation('auditmgmt');
  const names = useNames();
  const listId = useId();
  const [text, setText] = useState('');
  const [debounced, setDebounced] = useState('');
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [text]);
  const results = useAuditTeams(debounced, open);
  const label = (team: AuditTeamOption) =>
    `${team.number ?? ''} ${names.text(team.nameAr, team.nameEn)}`.trim();

  if (value && !open) {
    return (
      <div className="flex items-center gap-2" data-testid={testId}>
        <span className="min-w-0 flex-1 truncate text-sm">
          <bdi>{label(value)}</bdi>
        </span>
        <Button size="sm" variant="ghost" onClick={() => setOpen(true)}>
          {t('teamPicker.change')}
        </Button>
        <Button size="sm" variant="ghost" onClick={() => onChange(null)}>
          {t('teamPicker.clear')}
        </Button>
      </div>
    );
  }

  return (
    <div className="relative" data-testid={testId}>
      <Input
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-label={t('teamPicker.search')}
        placeholder={t('teamPicker.search')}
        value={text}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setText(e.target.value);
          setOpen(true);
        }}
      />
      {open && (
        <ul
          id={listId}
          role="listbox"
          className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-md border bg-popover p-1 shadow-md"
        >
          {(results.data ?? []).map((team) => (
            <li key={team.id} role="option" aria-selected={team.id === value?.id}>
              <button
                type="button"
                className="w-full rounded px-2 py-1.5 text-start text-sm hover:bg-muted"
                onClick={() => {
                  onChange(team);
                  setText('');
                  setOpen(false);
                }}
              >
                <bdi>{label(team)}</bdi>
              </button>
            </li>
          ))}
          {results.data?.length === 0 && (
            <li className="px-2 py-1.5 text-sm text-muted-foreground">{t('teamPicker.none')}</li>
          )}
          <li>
            <button
              type="button"
              className="w-full px-2 py-1 text-start text-xs text-muted-foreground"
              onClick={() => setOpen(false)}
            >
              {t('teamPicker.close')}
            </button>
          </li>
        </ul>
      )}
    </div>
  );
}
