import { useState } from 'react';
import { cn } from '@/lib/cn';

interface AvatarProps {
  name: string;
  src?: string | undefined;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

const sizes = { sm: 'size-8 text-xs', md: 'size-10 text-sm', lg: 'size-24 text-2xl' } as const;

/** Photo, or the initials of the name when there is no photo (or it fails to load). */
export function Avatar({ name, src, size = 'md', className }: AvatarProps) {
  const [failed, setFailed] = useState(false);
  const initials =
    name
      .split(/\s+/u)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part.charAt(0))
      .join('')
      .toUpperCase() || '?';
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-accent font-semibold text-accent-foreground',
        sizes[size],
        className,
      )}
    >
      {src && !failed ? (
        <img src={src} alt={name} className="size-full object-cover" onError={() => setFailed(true)} />
      ) : (
        <span aria-hidden>{initials}</span>
      )}
      {(!src || failed) && <span className="sr-only">{name}</span>}
    </span>
  );
}
