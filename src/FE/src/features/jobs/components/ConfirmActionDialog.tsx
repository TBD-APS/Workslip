import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';

type ConfirmActionDialogProps = {
  action: 'approve' | 'reject' | 'undo-reject';
  reportNumber: string;
  isPending: boolean;
  onConfirm: (rejectionNote?: string) => void;
  onClose: () => void;
};

export function ConfirmActionDialog({ action, reportNumber, isPending, onConfirm, onClose }: ConfirmActionDialogProps) {
  const [rejectionNote, setRejectionNote] = useState('');

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const isApprove = action === 'approve';
  const isUndoReject = action === 'undo-reject';
  const confirmButton = (
    <button
      type="button"
      className={isApprove ? 'btn btn-primary' : 'btn btn-danger'}
      onClick={() => onConfirm(rejectionNote)}
      disabled={isPending || (action === 'reject' && !rejectionNote.trim())}
    >
      {isPending && <Loader2 className="animate-spin" size={16} />}
      <span>{isPending ? (isApprove ? 'Godkender...' : 'Afviser...') : (isApprove ? 'Godkend' : isUndoReject ? 'Fortryd afvisning' : 'Afvis')}</span>
    </button>
  );
  const cancelButton = (
    <button
      type="button"
      className="btn btn-secondary"
      onClick={onClose}
      disabled={isPending}
    >
      Annuller
    </button>
  );

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={isApprove ? 'Godkend sag' : isUndoReject ? 'Fortryd afvisning' : 'Afvis sag'}
      >
        <h3>{isApprove ? 'Godkend sag' : isUndoReject ? 'Fortryd afvisning' : 'Afvis sag'}</h3>
        <p>
          Er du sikker på, du vil {isUndoReject ? 'fortryde afvisningen af' : isApprove ? 'godkende' : 'afvise'} sagen <strong>{reportNumber}</strong>?
        </p>

        {action === 'reject' && (
          <div className="form-group" style={{ marginTop: '1rem' }}>
            <label className="form-label" htmlFor="rejection-note">Begrundelse for afvisning</label>
            <textarea
              id="rejection-note"
              className="form-input form-textarea"
              value={rejectionNote}
              onChange={(e) => setRejectionNote(e.target.value)}
              placeholder="Angiv årsagen til afvisningen..."
              rows={3}
            />
          </div>
        )}

        <div className="modal-actions modal-actions--double">
          {isApprove ? (
            <>
              {cancelButton}
              {confirmButton}
            </>
          ) : (
            <>
              {confirmButton}
              {cancelButton}
            </>
          )}
        </div>
      </div>
    </div>,
    document.getElementById('portal-root') ?? document.body,
  );
}
