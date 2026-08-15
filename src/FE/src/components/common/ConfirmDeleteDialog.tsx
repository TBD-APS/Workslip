import { useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';
import { useModalAccessibility } from './useModalAccessibility';

type ConfirmDeleteDialogProps = {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  onConfirm: () => void | Promise<void>;
  onClose: () => void;
};

export function ConfirmDeleteDialog({
  open,
  title,
  message,
  confirmLabel = 'Slet',
  onConfirm,
  onClose,
}: ConfirmDeleteDialogProps) {
  const [isDeleting, setIsDeleting] = useState(false);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open,
    onClose,
    initialFocusRef: cancelButtonRef,
  });

  if (!open) return null;

  const handleConfirm = async () => {
    setIsDeleting(true);
    try {
      await onConfirm();
    } finally {
      setIsDeleting(false);
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
            className="btn btn-danger"
            onClick={() => void handleConfirm()}
            disabled={isDeleting}
          >
            {isDeleting && <Loader2 className="animate-spin" size={16} aria-hidden="true" />}
            <span>{isDeleting ? 'Sletter...' : confirmLabel}</span>
          </button>
          <button
            ref={cancelButtonRef}
            type="button"
            className="btn btn-secondary"
            onClick={onClose}
            disabled={isDeleting}
          >
            Annuller
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
