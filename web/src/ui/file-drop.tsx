import { Upload } from 'lucide-react';
import { useId, useState, type DragEvent } from 'react';
import { cn } from '@/lib/cn';

interface FileDropProps {
  label: string;
  hint?: string;
  accept: string;
  file: File | null;
  onFile: (file: File | null) => void;
  disabled?: boolean;
}

/** Drop zone + file picker (one file). The server re-checks type and size; this only helps the user. */
export function FileDrop({ label, hint, accept, file, onFile, disabled }: FileDropProps) {
  const id = useId();
  const [over, setOver] = useState(false);
  const onDrop = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault();
    setOver(false);
    if (!disabled) onFile(event.dataTransfer.files[0] ?? null);
  };
  return (
    <label
      htmlFor={id}
      onDragOver={(event) => {
        event.preventDefault();
        setOver(true);
      }}
      onDragLeave={() => setOver(false)}
      onDrop={onDrop}
      className={cn(
        'flex cursor-pointer flex-col items-center gap-2 rounded-lg border-2 border-dashed p-6 text-center transition-colors',
        over ? 'border-primary bg-accent' : 'border-input hover:bg-muted/50',
        disabled && 'cursor-not-allowed opacity-50',
      )}
    >
      <Upload className="size-6 text-muted-foreground" aria-hidden />
      <span className="text-sm font-medium">{file ? file.name : label}</span>
      {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
      <input
        id={id}
        type="file"
        accept={accept}
        // The visible text turns into the file name; the control keeps its purpose as its name.
        aria-label={label}
        disabled={disabled}
        className="sr-only"
        onChange={(event) => onFile(event.target.files?.[0] ?? null)}
      />
    </label>
  );
}
