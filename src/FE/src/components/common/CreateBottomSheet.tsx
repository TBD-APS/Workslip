import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { DOCUMENT_TYPES } from '../../features/create/documentTypes';
import { Can } from '../../providers/permissions';

type Props = {
  isOpen: boolean;
  onClose: () => void;
};

export const CreateBottomSheet = ({ isOpen, onClose }: Props) => {
  const navigate = useNavigate();
  const sheetRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!isOpen) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [isOpen, onClose]);

  const handleSelect = (path: string) => {
    onClose();
    navigate(path);
  };

  return (
    <div
      className={`create-sheet-overlay ${isOpen ? 'open' : ''}`}
      onClick={onClose}
    >
      <div
        ref={sheetRef}
        className={`create-sheet ${isOpen ? 'open' : ''}`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="create-sheet-drag-handle" />
        <h3 className="create-sheet-title">Opret</h3>
        <ul className="create-sheet-list" role="list">
          {DOCUMENT_TYPES.map((type) => {
            const Icon = type.icon;
            const disabled = type.status !== 'available';
            const content = (
              <>
                <span className="create-sheet-icon" aria-hidden="true">
                  <Icon size={20} />
                </span>
                <span className="create-sheet-body">
                  <span className="create-sheet-label">{type.label}</span>
                  <span className="create-sheet-description">{type.description}</span>
                </span>
              </>
            );

            const tile = disabled ? (
              <div className="create-sheet-tile create-sheet-tile--disabled" aria-disabled="true">
                {content}
              </div>
            ) : (
              <button
                type="button"
                className="create-sheet-tile"
                onClick={() => handleSelect(type.path)}
              >
                {content}
              </button>
            );

            if (type.permission) {
              return (
                <li key={type.id} className="create-sheet-item">
                  <Can permission={type.permission}>
                    {tile}
                  </Can>
                </li>
              );
            }

            return (
              <li key={type.id} className="create-sheet-item">
                {tile}
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
};
