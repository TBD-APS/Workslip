import { useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';
import { useModalAccessibility } from '../../../components/common/useModalAccessibility';

type ConfirmActionDialogProps = {
  action: 'approve' | 'reject' | 'undo-reject';
  reportNumber: string;
  isPending: boolean;
  onConfirm: (rejectionNote?: string) => void;
  onClose: () => void;
};

export function ConfirmActionDialog({ action, reportNumber, isPending, onConfirm, onClose }: ConfirmActionDialogProps) {
  const [rejectionNote, setRejectionNote] = useState('');
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open: true,
    onClose,
    initialFocusRef: cancelButtonRef,
  });

  const isApprove = action === 'approve';
  const isUndoReject = action === 'undo-reject';
  const title = isApprove ? 'Godkend sag' : isUndoReject ? 'Fortryd afvisning' : 'Afvis sag';
  const confirmButton = (
    <button
      type="button"
      className={isApprove ? 'btn btn-primary' : 'btn btn-danger'}
      onClick={() => onConfirm(rejectionNote)}
      disabled={isPending || (action === 'reject' && !rejectionNote.trim())}
    >
      {isPending && <Loader2 className="animate-spin" size={16} aria-hidden="true" />}
      <span>{isPending ? (isApprove ? 'Godkender...' : 'Afviser...') : (isApprove ? 'Godkend' : isUndoReject ? 'Fortryd afvisning' : 'Afvis')}</span>
    </button>
  );
  const cancelButton = (
    <button
      ref={cancelButtonRef}
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
        ref={dialogRef}
        className="modal-card"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <h3 id={titleId}>{title}</h3>
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
              onChange={(event) => setRejectionNote(event.target.value)}
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
