import { Trash2 } from 'lucide-react';

type DeleteButtonProps = {
  onClick: () => void;
  disabled?: boolean;
  ariaLabel?: string;
  size?: number;
  title?: string;
};

export function DeleteButton({ onClick, disabled, ariaLabel = 'Slet', size = 18, title }: DeleteButtonProps) {
  return (
    <button
      type="button"
      className="btn-icon btn-icon-danger"
      onClick={(event) => {
        event.stopPropagation();
        onClick();
      }}
      disabled={disabled}
      aria-label={ariaLabel}
      title={title}
    >
      <Trash2 size={size} />
    </button>
  );
}
