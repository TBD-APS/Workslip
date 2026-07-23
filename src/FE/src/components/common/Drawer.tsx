import type { ReactNode } from 'react';
import { X } from 'lucide-react';

type DrawerProps = {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  ariaLabel?: string;
  icon?: ReactNode;
  className?: string;
  children: ReactNode;
};

export function Drawer({ isOpen, onClose, title, ariaLabel, icon, className, children }: DrawerProps) {
  return (
    <>
      <div
        className={`drawer-overlay ${isOpen ? 'open' : ''}`}
        onClick={onClose}
      />
      <div
        className={`drawer${className ? ` ${className}` : ''} ${isOpen ? 'open' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel ?? title}
      >
        <div className="drawer-header">
          <div className="drawer-title">
            {icon}
            <h2>{title}</h2>
          </div>
          <button className="btn-icon" onClick={onClose} aria-label={`Luk ${title.toLowerCase()}`}>
            <X size={24} />
          </button>
        </div>

        <div className="drawer-content">
          {children}
        </div>
      </div>
    </>
  );
}
