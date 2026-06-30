import { createPortal } from 'react-dom';
import { Pencil, Trash2 } from 'lucide-react';
import type { WorksheetResponse } from '../../../api/generated/models';
import type { ActionMenuState } from './worksheetUtils';

type WorksheetActionMenuPortalProps = {
  openActionMenu: ActionMenuState | null;
  openActionWorksheet: WorksheetResponse | null;
  isDeleting: boolean;
  canDelete: boolean;
  onStartEdit: (worksheet: WorksheetResponse) => void;
  onDelete: (worksheet: WorksheetResponse) => void;
};

export function WorksheetActionMenuPortal({
  openActionMenu,
  openActionWorksheet,
  isDeleting,
  canDelete,
  onStartEdit,
  onDelete,
}: WorksheetActionMenuPortalProps) {
  if (!openActionMenu || !openActionWorksheet) return null;

  return createPortal(
    <div
      className="worksheet-actions-menu"
      role="menu"
      style={{ top: openActionMenu.top, right: openActionMenu.right }}
    >
      <button type="button" role="menuitem" onClick={() => onStartEdit(openActionWorksheet)}>
        <Pencil size={15} />
        <span>Rediger</span>
      </button>
      {canDelete && (
        <button
          type="button"
          className="danger"
          role="menuitem"
          onClick={() => onDelete(openActionWorksheet)}
          disabled={isDeleting}
        >
          <Trash2 size={15} />
          <span>Slet</span>
        </button>
      )}
    </div>,
    document.body,
  );
}
