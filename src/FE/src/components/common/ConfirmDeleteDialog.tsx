import { ConfirmDialog } from './ConfirmDialog';

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
  return (
    <ConfirmDialog
      open={open}
      title={title}
      message={message}
      confirmLabel={confirmLabel}
      pendingLabel="Sletter..."
      variant="danger"
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );
}
