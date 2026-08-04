import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';

type ConfirmAction = 'submit' | 'approve' | 'reject' | 'undo-reject';

type ConfirmActionDialogProps = {
  action: ConfirmAction;
  reportNumber: string;
  isPending: boolean;
  onConfirm: (rejectionNote?: string) => void;
  onClose: () => void;
};

const ACTION_COPY: Record<ConfirmAction, { title: string; confirm: string; pending: string }> = {
  submit: {
    title: 'Attestér og indsend sag',
    confirm: 'Attestér og indsend',
    pending: 'Indsender...',
  },
  approve: {
    title: 'Godkend sag',
    confirm: 'Godkend',
    pending: 'Godkender...',
  },
  reject: {
    title: 'Afvis sag',
    confirm: 'Afvis',
    pending: 'Afviser...',
  },
  'undo-reject': {
    title: 'Fortryd afvisning',
    confirm: 'Fortryd afvisning',
    pending: 'Fortryder...',
  },
};

export function ConfirmActionDialog({ action, reportNumber, isPending, onConfirm, onClose }: ConfirmActionDialogProps) {
  const [rejectionNote, setRejectionNote] = useState('');
  const copy = ACTION_COPY[action];
  const isPositiveAction = action === 'submit' || action === 'approve';

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isPending) onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [isPending, onClose]);

  const requestClose = () => {
    if (!isPending) onClose();
  };

  const confirmButton = (
    <button
      type="button"
      className={isPositiveAction ? 'btn btn-primary' : 'btn btn-danger'}
      onClick={() => onConfirm(rejectionNote)}
      disabled={isPending || (action === 'reject' && !rejectionNote.trim())}
    >
      {isPending && <Loader2 className="animate-spin" size={16} />}
      <span>{isPending ? copy.pending : copy.confirm}</span>
    </button>
  );
  const cancelButton = (
    <button
      type="button"
      className="btn btn-secondary"
      onClick={requestClose}
      disabled={isPending}
    >
      Annuller
    </button>
  );

  return createPortal(
    <div className="modal-backdrop" onClick={requestClose}>
      <div
        className="modal-card"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-label={copy.title}
      >
        <h3>{copy.title}</h3>
        {action === 'submit' ? (
          <p>
            Er du sikker på, du vil attestere og indsende sagen <strong>{reportNumber}</strong>? Indsendelsen kan ikke fortrydes.
          </p>
        ) : (
          <p>
            Er du sikker på, du vil {action === 'undo-reject' ? 'fortryde afvisningen af' : action === 'approve' ? 'godkende' : 'afvise'} sagen <strong>{reportNumber}</strong>?
          </p>
        )}

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
          {isPositiveAction ? (
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
