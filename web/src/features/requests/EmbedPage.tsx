import { useState } from 'react';
import { embedRequestRoute } from '@/app/router';
import { LanguageToggle } from '@/layout/TopBar';
import type { FrameworkRequest } from './api';
import { parsePrefill } from './formLogic';
import { RequestWizard, SubmittedPanel } from './WizardPage';

/**
 * Chrome-less submit page for the organisation's intranet (docs/design/requests.md §2.7): no sidebar or top bar, the same wizard and
 * rules; the server only serves it for types flagged embeddable and frames it from the allowed origins.
 */
export function EmbedRequestPage() {
  const { typeCode } = embedRequestRoute.useParams();
  const { values } = embedRequestRoute.useSearch();
  const [submitted, setSubmitted] = useState<FrameworkRequest>();
  const [round, setRound] = useState(0);
  return (
    <main className="min-h-screen bg-background p-4 text-foreground sm:p-6">
      <div className="mx-auto flex max-w-4xl flex-col gap-4">
        <div className="flex justify-end">
          <LanguageToggle />
        </div>
        {submitted ? (
          <SubmittedPanel
            request={submitted}
            onAnother={() => {
              setSubmitted(undefined);
              setRound((r) => r + 1);
            }}
          />
        ) : (
          <RequestWizard
            key={round}
            embedded
            typeCode={typeCode}
            prefill={parsePrefill(values)}
            onSubmitted={setSubmitted}
          />
        )}
      </div>
    </main>
  );
}
