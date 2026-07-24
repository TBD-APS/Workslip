import type { ReactNode } from 'react';
import { ChevronLeft } from 'lucide-react';

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
          <button className="btn-icon" onClick={onClose} aria-label={`Tilbage fra ${title.toLowerCase()}`}>
            <ChevronLeft size={26} />
          </button>
          <div className="drawer-title">
            {icon}
            <h2>{title}</h2>
          </div>
        </div>

        <div className="drawer-content">
          {children}
        </div>
      </div>
    </>
  );
}
