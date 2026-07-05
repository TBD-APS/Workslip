import { formatDateLong } from '../../../lib/formatDate';
export type WorksheetDraft = {
  userId: string;
  workDate: string;
  hours: string;
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

export function initialWorksheetUiState(defaultUserId: string): WorksheetUiState {
  return {
    addDraft: defaultDraft(defaultUserId),
    editDraft: null,
    editingWorksheetId: null,
    openActionMenu: null,
    isAddOpen: false,
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
