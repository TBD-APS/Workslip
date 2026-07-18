import { MoreHorizontal } from 'lucide-react';
import type { WorksheetResponse } from '../../../api/generated/models';
import { formatNumber, formatUnit, abbreviateName } from '../../../lib/formatUtils';
import type { WorksheetDraft, ActionMenuState, UserOption } from './worksheetUtils';
import { parseHours, formatDate } from './worksheetUtils';
import { WorksheetDraftForm } from './WorksheetDraftForm';
import type { MouseEvent } from 'react';

type WorksheetListProps = {
  sortedWorksheets: WorksheetResponse[];
  displayNameFor: (userId: string) => string;
  userOptions: UserOption[];
  canPickUser: boolean;
  currentUserName: string;
  editDraft: WorksheetDraft | null;
  editingWorksheetId: string | null;
  openActionMenu: ActionMenuState | null;
  isLoadingUsers: boolean;
  isSaving: boolean;
  formError: string | null;
  isScrollableList: boolean;
  isDetailList: boolean;
  onToggleActionMenu: (event: MouseEvent<HTMLButtonElement>, worksheetId: string) => void;
  onEditDraftChange: (draft: WorksheetDraft) => void;
  onSaveEdit: (draft: WorksheetDraft, worksheetId: string) => void;
  onCancelEdit: () => void;
};

export function WorksheetList({
  sortedWorksheets,
  displayNameFor,
  userOptions,
  canPickUser,
  currentUserName,
  editDraft,
  editingWorksheetId,
  openActionMenu,
  isLoadingUsers,
  isSaving,
  formError,
  isScrollableList,
  isDetailList,
  onToggleActionMenu,
  onEditDraftChange,
  onSaveEdit,
  onCancelEdit,
}: WorksheetListProps) {
  return (
    <ul className={`worksheet-list ${isScrollableList ? 'worksheet-list--scrollable' : ''} ${isDetailList ? 'worksheet-list--detail' : ''} ${editingWorksheetId ? 'expanded' : ''}`}>
      {sortedWorksheets.map((worksheet) => {
        const assigneeName = abbreviateName(displayNameFor(worksheet.userId));
        const isEditing = editingWorksheetId === worksheet.id && editDraft;

        return (
          <li key={worksheet.id} className={`worksheet-list-item ${isDetailList ? 'worksheet-list-item--detail' : ''} ${isEditing ? 'worksheet-list-item--editing is-selected' : ''}`}>
            {!isEditing && (
              isDetailList ? (
                <>
                  <div className="worksheet-list-item-main worksheet-list-item-main--detail">
                    <span className="worksheet-list-item-title" title={assigneeName}>{assigneeName}</span>
                    <span className="worksheet-list-item-subtitle worksheet-list-item-subtitle--detail">{formatDate(worksheet.workDate)}</span>
                  </div>

                  <div className="worksheet-list-item-meta">
                    <div className="worksheet-list-item-badge">
                      <strong>{formatNumber(parseHours(worksheet.hoursWorked))}</strong>
                      <span>{formatUnit(parseHours(worksheet.hoursWorked), 'time', 'timer')}</span>
                    </div>
                    {worksheet.sleptOnJob && <span className="worksheet-list-item-tag">Udlæg</span>}
                  </div>

                  <div className="worksheet-list-item-actions worksheet-list-item-actions--detail">
                    <div className="worksheet-actions-menu-root">
                      <button
                        type="button"
                        className="btn-icon"
                        onClick={(event) => onToggleActionMenu(event, worksheet.id)}
                        aria-label="Åbn handlinger for timeseddel"
                        aria-expanded={openActionMenu?.worksheetId === worksheet.id}
                        title="Handlinger"
                      >
                        <MoreHorizontal size={18} />
                      </button>
                    </div>
                  </div>
                </>
              ) : (
                <>
                  <div className="worksheet-list-item-main">
                    <span className="worksheet-list-item-title" title={assigneeName}>{assigneeName}</span>
                    <span className="worksheet-list-item-subtitle">{formatDate(worksheet.workDate)}</span>
                  </div>

                  <div className="worksheet-list-item-metrics">
                    <div className="worksheet-list-item-badge">
                      <strong>{formatNumber(parseHours(worksheet.hoursWorked))}</strong>
                      <span>{formatUnit(parseHours(worksheet.hoursWorked), 'time', 'timer')}</span>
                    </div>
                    {worksheet.sleptOnJob && <span className="worksheet-list-item-tag">Udlæg</span>}
                    <div className="worksheet-list-item-actions">
                      <div className="worksheet-actions-menu-root">
                        <button
                          type="button"
                          className="btn-icon"
                          onClick={(event) => onToggleActionMenu(event, worksheet.id)}
                          aria-label="Åbn handlinger for timeseddel"
                          aria-expanded={openActionMenu?.worksheetId === worksheet.id}
                          title="Handlinger"
                        >
                          <MoreHorizontal size={18} />
                        </button>
                      </div>
                    </div>
                  </div>
                </>
              )
            )}

            {isEditing && (
              <div className="worksheet-list-item-edit">
                <WorksheetDraftForm
                  title="Rediger timeseddel"
                  draft={editDraft}
                  userOptions={userOptions}
                  canPickUser={canPickUser}
                  currentUserName={currentUserName}
                  isLoadingUsers={isLoadingUsers}
                  isSaving={isSaving}
                  submitLabel="Gem"
                  error={formError}
                  onDraftChange={onEditDraftChange}
                  onSubmit={() => onSaveEdit(editDraft, worksheet.id)}
                  onCancel={onCancelEdit}
                />
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
