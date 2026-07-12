import type { CustomerSnapshotData } from '../../api/generated/models/customerSnapshotData';
import type { WorksheetDraft } from './components/worksheetUtils';

export type { WorksheetDraft };

export type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export type JobWorkForm = {
  categoryIds: string[];
  workKind: string;
  customWorkKind: string;
  controlPointSelections: Record<string, boolean>;
  irrelevantCategoryIds: string[];
  closureFlags: string[];
};

export type ReferenceCategory = {
  id: string;
  name: string;
  sortOrder: number | string;
  categories: Array<{
    id: string;
    name: string;
    sortOrder: number | string;
    controlPoints: Array<{
      id: string;
      name: string;
      description: string | null;
      sortOrder: number | string;
      isRequired: boolean;
    }>;
  }>;
};

export type ReferenceWorkKind = {
  normalizedLabel: string;
  label: string;
  requiresCustomWorkKind: boolean;
  sortOrder: number | string;
};


export type JobForm = {
  customerId: string | null;
  customerSnapshot: CustomerSnapshotData | null;
  editSnapshot: boolean;
  createCustomer: boolean;
  reportNumber: string;
  destinationAddress: string;
  destinationZipCode: string;
  destinationCity: string;
  taskDescription: string;
  customerObservations: string;
  technicalObservations: string;
  work: JobWorkForm;
  jobType: 'KLS' | 'Diverse' | 'Unknown';
  timesheets: WorksheetDraft[];
};


export type LinkableJob = {
  id: string;
  label: string;
  description: string;
};
