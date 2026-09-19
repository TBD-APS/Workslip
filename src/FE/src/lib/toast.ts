import { toast, type ExternalToast } from 'sonner';

type ToastId = string | number;

const activeErrors = new Map<ToastId, number>();

function messageKey(message: string): string {
  return `err:${message}`;
}

function shakeClass(count: number): string {
  return count % 2 === 0 ? 'toast-shake toast-shake-alt' : 'toast-shake';
}

function forget(id: ToastId): void {
  activeErrors.delete(id);
}

/**
 * Error toast with de-duplication. If an identical error message (or one with the
 * same explicit id) is already visible, no new toast is stacked. Instead the
 * existing toast is nudged with a small shake so the user notices it again.
 */
function error(message: string, options?: ExternalToast): ToastId {
  const id: ToastId = options?.id ?? messageKey(message);
  const isDuplicate = activeErrors.has(id);
  const nextCount = (activeErrors.get(id) ?? 0) + 1;
  activeErrors.set(id, nextCount);

  const classes = [options?.className, isDuplicate ? shakeClass(nextCount) : undefined]
    .filter(Boolean)
    .join(' ');

  return toast.error(message, {
    ...options,
    id,
    className: classes || undefined,
    onDismiss: (t) => {
      forget(id);
      options?.onDismiss?.(t);
    },
    onAutoClose: (t) => {
      forget(id);
      options?.onAutoClose?.(t);
    },
  });
}

function success(message: string, options?: ExternalToast): ToastId {
  return toast.success(message, options);
}

function info(message: string, options?: ExternalToast): ToastId {
  return toast.info(message, options);
}

function warning(message: string, options?: ExternalToast): ToastId {
  return toast.warning(message, options);
}

function message(msg: string, options?: ExternalToast): ToastId {
  return toast(msg, options);
}

function dismiss(id?: ToastId): void {
  if (id === undefined) {
    activeErrors.clear();
  } else {
    forget(id);
  }
  toast.dismiss(id);
}

export const notify = {
  error,
  success,
  info,
  warning,
  message,
  dismiss,
};
