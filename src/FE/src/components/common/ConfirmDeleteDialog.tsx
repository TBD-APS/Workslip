import { ConfirmDialog } from './ConfirmDialog';

type ConfirmDeleteDialogProps = {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  dialogId?: string;
  confirmId?: string;
  cancelId?: string;
  onConfirm: () => void | Promise<void>;
  onClose: () => void;
};

export function ConfirmDeleteDialog({
  open,
  title,
  message,
  confirmLabel = 'Slet',
  dialogId = 'confirm-delete-dialog',
  confirmId = 'confirm-delete-confirm',
  cancelId = 'confirm-delete-cancel',
  onConfirm,
  onClose,
}: ConfirmDeleteDialogProps) {
  return (
    <ConfirmDialog
      open={open}
      title={title}
      message={message}
      confirmLabel={confirmLabel}
      pendingLabel="Sletter..."
      variant="danger"
      dialogId={dialogId}
      confirmId={confirmId}
      cancelId={cancelId}
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );
}
