import * as ToastPrimitive from '@radix-ui/react-toast';
import { CheckCircle2, CircleAlert, Info, X } from 'lucide-react';
import { createContext, use, useCallback, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/cn';

type ToastTone = 'success' | 'error' | 'info';

interface ToastMessage {
  id: number;
  title: string;
  description?: string | undefined;
  tone: ToastTone;
}

type ShowToast = (toast: Omit<ToastMessage, 'id' | 'tone'> & { tone?: ToastTone }) => void;

const ToastContext = createContext<ShowToast | null>(null);

const toneStyles: Record<ToastTone, { className: string; icon: typeof Info }> = {
  success: { className: 'border-success/40', icon: CheckCircle2 },
  error: { className: 'border-danger/40', icon: CircleAlert },
  info: { className: 'border-info/40', icon: Info },
};

/** Toast host. Errors are announced assertively; others politely. Swipe direction follows the reading direction. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation();
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const show = useCallback<ShowToast>((toast) => {
    setToasts((current) => [
      ...current,
      { id: Date.now() + Math.random(), tone: toast.tone ?? 'success', ...toast },
    ]);
  }, []);
  const dismiss = (id: number) => setToasts((current) => current.filter((toast) => toast.id !== id));
  const rtl = i18n.dir() === 'rtl';

  return (
    <ToastContext value={show}>
      <ToastPrimitive.Provider swipeDirection={rtl ? 'left' : 'right'} duration={5000}>
        {children}
        {toasts.map((toast) => {
          const { className, icon: Icon } = toneStyles[toast.tone];
          return (
            <ToastPrimitive.Root
              key={toast.id}
              type={toast.tone === 'error' ? 'foreground' : 'background'}
              onOpenChange={(open) => !open && dismiss(toast.id)}
              className={cn(
                'flex items-start gap-3 rounded-lg border bg-card p-4 text-card-foreground shadow-lg',
                className,
              )}
            >
              <Icon
                className={cn(
                  'mt-0.5 size-5 shrink-0',
                  toast.tone === 'error'
                    ? 'text-danger'
                    : toast.tone === 'info'
                      ? 'text-info'
                      : 'text-success',
                )}
                aria-hidden
              />
              <div className="flex flex-1 flex-col gap-1">
                <ToastPrimitive.Title className="text-sm font-semibold">{toast.title}</ToastPrimitive.Title>
                {toast.description && (
                  <ToastPrimitive.Description className="text-sm text-muted-foreground">
                    {toast.description}
                  </ToastPrimitive.Description>
                )}
              </div>
              <ToastPrimitive.Close
                className="rounded p-1 text-muted-foreground hover:text-foreground"
                aria-label={t('actions.close')}
              >
                <X className="size-4" aria-hidden />
              </ToastPrimitive.Close>
            </ToastPrimitive.Root>
          );
        })}
        <ToastPrimitive.Viewport className="fixed end-0 bottom-0 z-[60] flex w-full max-w-sm flex-col gap-2 p-4 outline-none" />
      </ToastPrimitive.Provider>
    </ToastContext>
  );
}

export function useToast(): ShowToast {
  const show = use(ToastContext);
  if (!show) throw new Error('useToast must be used inside <ToastProvider>');
  return show;
}
