import { useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';
import { useModalAccessibility } from './useModalAccessibility';

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  pendingLabel?: string;
  cancelLabel?: string;
  variant?: 'primary' | 'danger';
  onConfirm: () => void | Promise<void>;
  onClose: () => void;
};

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  pendingLabel = confirmLabel,
  cancelLabel = 'Annuller',
  variant = 'primary',
  onConfirm,
  onClose,
}: ConfirmDialogProps) {
  const [isPending, setIsPending] = useState(false);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open,
    onClose,
    initialFocusRef: cancelButtonRef,
  });

  if (!open) return null;

  const handleConfirm = async () => {
    setIsPending(true);
    try {
      await onConfirm();
    } finally {
      setIsPending(false);
    }
  };

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        ref={dialogRef}
        className="modal-card"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <h3 id={titleId}>{title}</h3>
        <p>{message}</p>
        <div className="modal-actions">
          <button
            type="button"
            className={variant === 'danger' ? 'btn btn-danger' : 'btn btn-primary'}
            onClick={() => void handleConfirm()}
            disabled={isPending}
          >
            {isPending && <Loader2 className="animate-spin" size={16} aria-hidden="true" />}
            <span>{isPending ? pendingLabel : confirmLabel}</span>
          </button>
          <button
            ref={cancelButtonRef}
            type="button"
            className="btn btn-secondary"
            onClick={onClose}
            disabled={isPending}
          >
            {cancelLabel}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
