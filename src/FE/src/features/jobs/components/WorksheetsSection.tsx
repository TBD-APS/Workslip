import { Plus, Timer } from 'lucide-react';
import type { WorksheetResponse } from '../../../api/generated/models';
import { formatNumber, formatUnit } from '../../../lib/formatUtils';
import type { WorksheetDraft, ActionMenuState, UserOption } from './worksheetUtils';
import { WorksheetDraftForm } from './WorksheetDraftForm';
import { WorksheetList } from './WorksheetList';
import type { MouseEvent } from 'react';

type WorksheetsSectionProps = {
  sortedWorksheets: WorksheetResponse[];
  displayNameFor: (userId: string) => string;
  userOptions: UserOption[];
  canPickUser: boolean;
  currentUserName: string;
  addDraft: WorksheetDraft;
  editDraft: WorksheetDraft | null;
  editingWorksheetId: string | null;
  openActionMenu: ActionMenuState | null;
  isAddOpen: boolean;
  isLoadingUsers: boolean;
  isSaving: boolean;
  formError: string | null;
  totalHoursValue: number;
  totalOutlayValue: number;
  isScrollableList: boolean;
  isDetailList: boolean;
  onToggleActionMenu: (event: MouseEvent<HTMLButtonElement>, worksheetId: string) => void;
  onEditDraftChange: (draft: WorksheetDraft) => void;
  onSaveEdit: (draft: WorksheetDraft, worksheetId: string) => void;
  onCancelEdit: () => void;
  onOpenAddForm: () => void;
  onAddDraftChange: (draft: WorksheetDraft) => void;
  onSaveAdd: (draft: WorksheetDraft) => void;
  onCancelAdd: () => void;
};

export function WorksheetsSection({
  sortedWorksheets,
  displayNameFor,
  userOptions,
  canPickUser,
  currentUserName,
  addDraft,
  editDraft,
  editingWorksheetId,
  openActionMenu,
  isAddOpen,
  isLoadingUsers,
  isSaving,
  formError,
  totalHoursValue,
  totalOutlayValue,
  isScrollableList,
  isDetailList,
  onToggleActionMenu,
  onEditDraftChange,
  onSaveEdit,
  onCancelEdit,
  onOpenAddForm,
  onAddDraftChange,
  onSaveAdd,
  onCancelAdd,
}: WorksheetsSectionProps) {
  return (
    <section className="detail-section worksheet-list-section">
      <div className="section-header-row attestation-compact-header">
        <Timer size={18} />
        <h3>Timesedler</h3>
      </div>

      {(!editingWorksheetId || sortedWorksheets.length === 0) && !isAddOpen && (
        <button
          type="button"
          className={'btn btn-secondary worksheet-add-trigger worksheet-add-trigger-cta'}
          onClick={onOpenAddForm}
        >
          <Plus size={16} />
          <span>Tilføj timeseddel</span>
        </button>
      )}

      {!editingWorksheetId && isAddOpen && (
        <WorksheetDraftForm
          title="Ny timeseddel"
          draft={addDraft}
          userOptions={userOptions}
          canPickUser={canPickUser}
          currentUserName={currentUserName}
          isLoadingUsers={isLoadingUsers}
          isSaving={isSaving}
          submitLabel="Tilføj"
          error={formError}
          onDraftChange={onAddDraftChange}
          onSubmit={() => onSaveAdd(addDraft)}
          onCancel={onCancelAdd}
        />
      )}

      {sortedWorksheets.length === 0 ? (
        <p className="empty-state-text">Ingen timesedler registreret.</p>
      ) : (
        <WorksheetList
          sortedWorksheets={sortedWorksheets}
          displayNameFor={displayNameFor}
          userOptions={userOptions}
          canPickUser={canPickUser}
          currentUserName={currentUserName}
          editDraft={editDraft}
          editingWorksheetId={editingWorksheetId}
          openActionMenu={openActionMenu}
          isLoadingUsers={isLoadingUsers}
          isSaving={isSaving}
          formError={formError}
          isScrollableList={isScrollableList}
          isDetailList={isDetailList}
          onToggleActionMenu={onToggleActionMenu}
          onEditDraftChange={onEditDraftChange}
          onSaveEdit={onSaveEdit}
          onCancelEdit={onCancelEdit}
        />
      )}

      <div className="worksheet-list-totals" aria-label="Timeseddel totaler">
        <span><strong>{formatNumber(totalHoursValue)}</strong> {formatUnit(totalHoursValue, 'time', 'timer')}</span>
        {totalOutlayValue > 0 && (
          <span><strong>{formatNumber(totalOutlayValue)}</strong> {formatUnit(totalOutlayValue, 'dag', 'dage')}</span>
        )}
      </div>
    </section>
  );
}
