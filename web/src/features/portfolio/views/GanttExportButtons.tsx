import { FileDown, ImageDown } from 'lucide-react';
import { useState, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, exportGanttPdf, exportGanttPng, useToast } from '@/ui';

/** PNG / PDF download of a rendered Gantt (built in the browser, docs/design/portfolio.md §36). */
export function GanttExportButtons({
  svgRef,
  fileName,
  disabled,
}: {
  svgRef: RefObject<SVGSVGElement | null>;
  fileName: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation('portfolioViews');
  const toast = useToast();
  const [busy, setBusy] = useState<'png' | 'pdf' | null>(null);
  const run = async (kind: 'png' | 'pdf') => {
    const svg = svgRef.current;
    if (!svg) return;
    setBusy(kind);
    try {
      if (kind === 'png') await exportGanttPng(svg, fileName);
      else await exportGanttPdf(svg, fileName);
    } catch {
      toast({ tone: 'error', title: t('gantt.exportFailed') });
    } finally {
      setBusy(null);
    }
  };
  return (
    <div className="flex flex-wrap gap-2">
      <Button
        variant="outline"
        size="sm"
        disabled={disabled}
        loading={busy === 'png'}
        data-testid="gantt-export-png"
        onClick={() => void run('png')}
      >
        <ImageDown aria-hidden />
        {t('gantt.exportPng')}
      </Button>
      <Button
        variant="outline"
        size="sm"
        disabled={disabled}
        loading={busy === 'pdf'}
        data-testid="gantt-export-pdf"
        onClick={() => void run('pdf')}
      >
        <FileDown aria-hidden />
        {t('gantt.exportPdf')}
      </Button>
    </div>
  );
}
