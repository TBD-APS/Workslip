import { useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Loader2 } from 'lucide-react';
import { useModalAccessibility } from '../../../components/common/useModalAccessibility';

type ConfirmActionDialogProps = {
  action: 'approve' | 'reject' | 'undo-reject' | 'reopen' | 'withdraw';
  reportNumber: string;
  isPending: boolean;
  onConfirm: (reason?: string) => void;
  onClose: () => void;
};

const REJECTION_MARKER_PREFIX = '[workslip-rejection:';

const rejectionCategories = [
  { value: 'missing_required_data', label: 'Manglende data' },
  { value: 'incorrect_measurement_or_value', label: 'Forkert måling eller værdi' },
  { value: 'missing_photo_documentation', label: 'Manglende foto eller dokumentation' },
  { value: 'wrong_control_point', label: 'Forkert kontrolpunkt' },
  { value: 'incomplete_work', label: 'Arbejdet er ikke færdigt' },
  { value: 'duplicate_or_wrong_job', label: 'Forkert eller dubleret sag' },
  { value: 'system_or_ui_issue', label: 'System eller brugerflade' },
  { value: 'other', label: 'Andet' },
] as const;

export function ConfirmActionDialog({ action, reportNumber, isPending, onConfirm, onClose }: ConfirmActionDialogProps) {
  const [reason, setReason] = useState('');
  const [rejectionCategory, setRejectionCategory] = useState('');
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open: true,
    onClose,
    initialFocusRef: cancelButtonRef,
  });

  const isApprove = action === 'approve';
  const isReject = action === 'reject';
  const isUndoReject = action === 'undo-reject';
  const isReopen = action === 'reopen';
  const isWithdraw = action === 'withdraw';
  const requiresReason = isReject || isReopen;
  const title = isApprove
    ? 'Godkend sag'
    : isUndoReject
      ? 'Fortryd afvisning'
      : isReopen
        ? 'Genåbn godkendt sag'
        : isWithdraw
          ? 'Træk sag tilbage fra gennemsyn'
          : 'Afvis sag';
  const pendingLabel = isApprove ? 'Godkender...' : isReopen ? 'Genåbner...' : isWithdraw ? 'Trækker tilbage...' : 'Afviser...';
  const actionLabel = isApprove
    ? 'Godkend'
    : isUndoReject
      ? 'Fortryd afvisning'
      : isReopen
        ? 'Genåbn sag'
        : isWithdraw
          ? 'Træk tilbage'
          : 'Afvis';

  const handleConfirm = () => {
    if (isReject) {
      onConfirm(`${REJECTION_MARKER_PREFIX}${rejectionCategory}] ${reason.trim()}`);
      return;
    }

    onConfirm(reason);
  };

  const confirmButton = (
    <button
      id={isWithdraw ? 'job-report-withdraw-review-confirm' : undefined}
      type="button"
      className={isApprove ? 'btn btn-primary' : isReopen || isWithdraw ? 'btn btn-secondary' : 'btn btn-danger'}
      onClick={handleConfirm}
      disabled={isPending || (requiresReason && !reason.trim()) || (isReject && !rejectionCategory)}
    >
      {isPending && <Loader2 className="animate-spin" size={16} aria-hidden="true" />}
      <span>{isPending ? pendingLabel : actionLabel}</span>
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
        {isReopen ? (
          <p>
            Sagen <strong>{reportNumber}</strong> er godkendt og låst. Genåbning gør den redigerbar igen, og årsagen gemmes permanent i sagshistorikken.
          </p>
        ) : isWithdraw ? (
          <p>
            Sagen <strong>{reportNumber}</strong> trækkes tilbage fra gennemsyn, så du kan rette den og sende den til gennemsyn igen.
          </p>
        ) : (
          <p>
            Er du sikker på, du vil {isUndoReject ? 'fortryde afvisningen af' : isApprove ? 'godkende' : 'afvise'} sagen <strong>{reportNumber}</strong>?
          </p>
        )}

        {isReject && (
          <div className="form-group" style={{ marginTop: '1rem' }}>
            <label className="form-label" htmlFor="status-rejection-category">Årsagstype</label>
            <select
              id="status-rejection-category"
              className="form-input"
              value={rejectionCategory}
              onChange={(event) => setRejectionCategory(event.target.value)}
              disabled={isPending}
            >
              <option value="">Vælg årsagstype</option>
              {rejectionCategories.map((category) => (
                <option key={category.value} value={category.value}>{category.label}</option>
              ))}
            </select>
          </div>
        )}

        {requiresReason && (
          <div className="form-group" style={{ marginTop: '1rem' }}>
            <label className="form-label" htmlFor="status-reason">
              {isReopen ? 'Hvorfor skal sagen genåbnes?' : 'Kommentar til medarbejderen'}
            </label>
            <textarea
              id="status-reason"
              className="form-input form-textarea"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder={isReopen ? 'Beskriv hvad der skal ændres og hvorfor...' : 'Beskriv konkret hvad der skal rettes...'}
              rows={3}
            />
          </div>
        )}

        <div className="modal-actions modal-actions--double">
          {cancelButton}
          {confirmButton}
        </div>
      </div>
    </div>,
    document.getElementById('portal-root') ?? document.body,
  );
}
