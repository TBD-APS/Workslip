import type { useJobDetailsState } from '../hooks/useJobDetails';

export type CompletedJobDetailsState = ReturnType<typeof useJobDetailsState>;

export type SelectedControlPoint = {
  id: string;
  installationType: string;
  category: string;
  name: string;
};

export type IrrelevantCategory = {
  id: string;
  installationType: string;
  category: string;
};
