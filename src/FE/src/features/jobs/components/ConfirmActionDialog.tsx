import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';

type ConfirmActionDialogProps = {
  action: 'approve' | 'reject';
  reportNumber: string;
  isPending: boolean;
  onConfirm: () => void;
  onClose: () => void;
};

export function ConfirmActionDialog({ action, reportNumber, isPending, onConfirm, onClose }: ConfirmActionDialogProps) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const isApprove = action === 'approve';

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={isApprove ? 'Godkend sag' : 'Afvis sag'}
      >
        <h3>{isApprove ? 'Godkend sag' : 'Afvis sag'}</h3>
        <p>
          Er du sikker på, du vil {isApprove ? 'godkende' : 'afvise'} sagen <strong>{reportNumber}</strong>?
        </p>

        <div className="modal-actions modal-actions--double">
          <button
            type="button"
            className={isApprove ? 'btn btn-primary' : 'btn btn-danger'}
            onClick={onConfirm}
            disabled={isPending}
          >
            {isPending && <Loader2 className="animate-spin" size={16} />}
            <span>{isPending ? (isApprove ? 'Godkender...' : 'Afviser...') : (isApprove ? 'Godkend' : 'Afvis')}</span>
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onClose}
            disabled={isPending}
          >
            Annuller
          </button>
        </div>
      </div>
    </div>,
    document.getElementById('portal-root') ?? document.body,
  );
}
