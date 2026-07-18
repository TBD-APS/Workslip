import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';

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

  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

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
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={title}
      >
        <h3>{title}</h3>
        <p>{message}</p>

        <div className="modal-actions">
          <button
            type="button"
            className="btn btn-danger"
            onClick={() => void handleConfirm()}
            disabled={isDeleting}
          >
            {isDeleting && <Loader2 className="animate-spin" size={16} />}
            <span>{isDeleting ? 'Sletter...' : confirmLabel}</span>
          </button>
          <button
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
