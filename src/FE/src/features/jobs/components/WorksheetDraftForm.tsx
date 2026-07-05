import { Loader2, Users } from 'lucide-react';
import { Checkbox } from '../../../components/forms/Checkbox';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { CalendarPicker } from './CalendarPicker';
import type { WorksheetDraft, UserOption } from './worksheetUtils';

type WorksheetDraftFormProps = {
  title: string;
  draft: WorksheetDraft;
  userOptions: UserOption[];
  canPickUser: boolean;
  currentUserName: string;
  isLoadingUsers: boolean;
  isSaving: boolean;
  submitLabel: string;
  error?: string | null;
  onDraftChange: (draft: WorksheetDraft) => void;
  onSubmit: () => void;
  onCancel?: () => void;
};

export function WorksheetDraftForm({
  title,
  draft,
  userOptions,
  canPickUser,
  currentUserName,
  isLoadingUsers,
  isSaving,
  submitLabel,
  error,
  onDraftChange,
  onSubmit,
  onCancel,
}: WorksheetDraftFormProps) {
  const updateDraft = (patch: Partial<WorksheetDraft>) => onDraftChange({ ...draft, ...patch });

  return (
    <div className="worksheet-form worksheet-form--compact">
      <h4>{title}</h4>
      <div className="worksheet-form-grid worksheet-form-grid-main">
        <CalendarPicker value={draft.workDate} onChange={(workDate) => updateDraft({ workDate })} />

        {canPickUser ? (
          <MultiSelectDropdown
            label="Montør"
            placeholder="Vælg montør"
            emptyText="Ingen medarbejdere fundet"
            loadingText="Henter medarbejdere..."
            options={userOptions}
            selectedIds={draft.userId ? [draft.userId] : []}
            isLoading={isLoadingUsers}
            icon={<Users size={16} />}
            onChange={(ids) => updateDraft({ userId: ids.at(-1) ?? '' })}
          />
        ) : (
          <div className="form-group form-readonly">
            <span className="form-label">Montør</span>
            <span className="form-readonly-value">{currentUserName}</span>
          </div>
        )}
      </div>

      <div className="worksheet-form-grid worksheet-form-grid-hours">
        <div className="form-group">
          <label className="form-label" htmlFor={`${title}-worksheet-hours`}>Timer</label>
          <input
            id={`${title}-worksheet-hours`}
            className="form-input"
            type="number"
            min="0"
            max="24"
            step="0.25"
            inputMode="decimal"
            value={draft.hours}
            onChange={(e) => updateDraft({ hours: e.target.value })}
            placeholder="0"
          />
        </div>

        <div className="worksheet-overnight-wrapper">
          <Checkbox
            checked={draft.sleptOnJob}
            onChange={() => updateDraft({ sleptOnJob: !draft.sleptOnJob })}
            label="Udlæg"
          />
        </div>
      </div>

      {error && <p className="form-error-text">{error}</p>}

      <div className="worksheet-form-actions worksheet-form-actions--compact">
        <button
          type="button"
          className="btn btn-primary"
          onClick={onSubmit}
          disabled={isSaving}
        >
          {isSaving && <Loader2 className="animate-spin" size={16} />}
          <span>{isSaving ? 'Gemmer...' : submitLabel}</span>
        </button>
        {onCancel && (
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onCancel}
            disabled={isSaving}
          >
            Annuller
          </button>
        )}
      </div>
    </div>
  );
}
