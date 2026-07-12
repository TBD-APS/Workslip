import { formatDateLong } from '../../../lib/formatDate';

export type WorksheetDraft = {
  id?: string;
  userId: string;
  workDate: string;
  hours: string | number;
  sleptOnJob: boolean;
};

export type ActionMenuState = {
  worksheetId: string;
  top: number;
  right: number;
};

export type UserOption = { id: string; label: string; description?: string };

export type WorksheetUiState = {
  addDraft: WorksheetDraft;
  editDraft: WorksheetDraft | null;
  editingWorksheetId: string | null;
  openActionMenu: ActionMenuState | null;
  isAddOpen: boolean;
  formError: string | null;
};

export type WorksheetUiAction =
  | { type: 'missingEditingWorksheet' }
  | { type: 'closeActionMenu' }
  | { type: 'toggleActionMenu'; worksheetId: string; top: number; right: number }
  | { type: 'setFormError'; error: string | null }
  | { type: 'setAddDraft'; draft: WorksheetDraft }
  | { type: 'setEditDraft'; draft: WorksheetDraft | null }
  | { type: 'openAdd'; defaultUserId: string }
  | { type: 'cancelAdd'; defaultUserId: string }
  | { type: 'toggleEdit'; worksheetId: string; draft: WorksheetDraft }
  | { type: 'cancelEdit' }
  | { type: 'deleteStarted'; worksheetId: string }
  | { type: 'saveSucceeded'; worksheetId?: string; defaultUserId: string };

export function todayIso(): string {
  const now = new Date();
  return toDateIso(now);
}

export function toDateIso(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function fromDateIso(value: string): Date {
  const [year, month, day] = dateKey(value).split('-').map(Number);
  return new Date(year, month - 1, day);
}

export function dateKey(value: string): string {
  return value.slice(0, 10);
}

export function parseHours(value: number | string): number {
  return typeof value === 'number' ? value : Number(value.replace(',', '.'));
}

export function formatDate(value: string): string {
  return formatDateLong(value) ?? value;
}

export function defaultDraft(defaultUserId: string): WorksheetDraft {
  return {
    userId: defaultUserId,
    workDate: todayIso(),
    hours: '',
    sleptOnJob: false,
  };
}

type ValidatableEntry = { userId: string; workDate: string; id?: string; hours?: string | number; hoursWorked?: string | number };

function entryHours(e: ValidatableEntry): string | number {
  return e.hoursWorked ?? e.hours!;
}

export type WorksheetValidationError = { error: string };
export type WorksheetValidationSuccess = { hours: number };
export type WorksheetValidationResult = WorksheetValidationError | WorksheetValidationSuccess;

export function validateWorksheetDraft(
  draft: WorksheetDraft,
  existing: ValidatableEntry[],
  excludeId?: string,
): WorksheetValidationResult {
  if (!draft.userId) {
    return { error: 'Vælg en montør.' };
  }

  const hoursNumber = parseHours(draft.hours);
  if (!Number.isFinite(hoursNumber) || hoursNumber <= 0) {
    return { error: 'Timer skal være større end 0.' };
  }

  if (hoursNumber > 24) {
    return { error: 'Timer kan ikke overstige 24 på en dag.' };
  }

  const scaledHours = hoursNumber * 4;
  if (Math.abs(scaledHours - Math.round(scaledHours)) > 1e-9) {
     return { error: 'Timer skal angives i intervaller af 0,25.' };
  }

  const existingTotal = existing
    .filter(e => e.id !== excludeId)
    .filter(e => e.userId === draft.userId && dateKey(e.workDate) === dateKey(draft.workDate))
    .reduce((total, e) => total + parseHours(entryHours(e)), 0);

  if (!Number.isFinite(existingTotal) || existingTotal + hoursNumber > 24) {
    return { error: 'Montøren kan ikke registrere mere end 24 timer på samme dato.' };
  }

  return { hours: hoursNumber };
}

export function initialWorksheetUiState(defaultUserId: string, isAddOpen = false): WorksheetUiState {
  return {
    addDraft: defaultDraft(defaultUserId),
    editDraft: null,
    editingWorksheetId: null,
    openActionMenu: null,
    isAddOpen,
    formError: null,
  };
}

export function worksheetUiReducer(state: WorksheetUiState, action: WorksheetUiAction): WorksheetUiState {
  switch (action.type) {
    case 'missingEditingWorksheet':
      return { ...state, editingWorksheetId: null, editDraft: null, formError: null };
    case 'closeActionMenu':
      return { ...state, openActionMenu: null };
    case 'toggleActionMenu':
      return {
        ...state,
        openActionMenu: state.openActionMenu?.worksheetId === action.worksheetId
          ? null
          : { worksheetId: action.worksheetId, top: action.top, right: action.right },
      };
    case 'setFormError':
      return { ...state, formError: action.error };
    case 'setAddDraft':
      return { ...state, addDraft: action.draft };
    case 'setEditDraft':
      return { ...state, editDraft: action.draft };
    case 'openAdd':
      return {
        ...state,
        editingWorksheetId: null,
        editDraft: null,
        openActionMenu: null,
        addDraft: state.addDraft.userId || !action.defaultUserId
          ? state.addDraft
          : { ...state.addDraft, userId: action.defaultUserId },
        isAddOpen: true,
        formError: null,
      };
    case 'cancelAdd':
      return { ...state, addDraft: defaultDraft(action.defaultUserId), isAddOpen: false, formError: null };
    case 'toggleEdit':
      if (state.editingWorksheetId === action.worksheetId) {
        return { ...state, editingWorksheetId: null, editDraft: null, openActionMenu: null, formError: null };
      }
      return {
        ...state,
        editingWorksheetId: action.worksheetId,
        editDraft: action.draft,
        openActionMenu: null,
        isAddOpen: false,
        formError: null,
      };
    case 'cancelEdit':
      return { ...state, editingWorksheetId: null, editDraft: null, formError: null };
    case 'deleteStarted':
      if (state.editingWorksheetId !== action.worksheetId) {
        return { ...state, openActionMenu: null };
      }
      return { ...state, editingWorksheetId: null, editDraft: null, openActionMenu: null, formError: null };
    case 'saveSucceeded':
      if (action.worksheetId) {
        return { ...state, editingWorksheetId: null, editDraft: null, formError: null };
      }
      return {
        ...state,
        addDraft: defaultDraft(action.defaultUserId),
        isAddOpen: false,
        formError: null,
      };
    default:
      return state;
  }
}
