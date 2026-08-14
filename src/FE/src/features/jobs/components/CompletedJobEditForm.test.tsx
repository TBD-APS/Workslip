import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { CompletedJobDetailsState } from './completedJobTypes';

vi.mock('./JobDetailBlocks', () => ({
  CustomerDetailsBlock: () => <div>customer-details</div>,
  DestinationAddressBlock: () => <div>destination-address</div>,
  AssignmentBlock: () => <div>assignment</div>,
  LinkedJobsBlock: () => <div>linked-jobs</div>,
  TextAreaBlock: ({ title }: { title: string }) => <div>{`textarea:${title}`}</div>,
}));

vi.mock('./steps/ControlPointsStep', () => ({
  ControlPointsStep: () => <div>control-points</div>,
}));

vi.mock('./steps/JobCompletionStep', () => ({
  JobCompletionStep: () => <div>job-completion</div>,
}));

vi.mock('./steps/JobWorksheetsStep', () => ({
  JobWorksheetsStep: () => <div>worksheets</div>,
}));

vi.mock('./steps/WorkCategoryStep', () => ({
  WorkCategoryStep: ({ mode }: { mode: string }) => <div>{`work-category:${mode}`}</div>,
}));

import { CompletedJobEditForm } from './CompletedJobEditForm';

function createDetails(jobType: 'KLS' | 'Diverse'): CompletedJobDetailsState {
  return {
    job: {
      id: 'job-1',
      jobType,
      totalHours: 7.5,
      totalOutlay: 0,
    },
    form: {
      destinationAddress: 'Testvej 1',
      destinationZipCode: '8000',
      destinationCity: 'Aarhus C',
      taskDescription: 'Testopgave',
      customerSnapshot: null,
      editSnapshot: false,
      createCustomer: false,
      customerObservations: '',
      technicalObservations: '',
    },
    assignableUsers: [],
    assignedUserIds: [],
    isLoadingUsers: false,
    linkableJobs: [],
    linkedJobIds: [],
    isLoadingJobs: false,
    referenceData: null,
    isLoadingReferenceData: false,
    worksheets: [],
    saveStatus: 'idle',
    isSavingWorksheet: false,
    isDeletingWorksheet: false,
    updateDestinationAddress: vi.fn(),
    updateDestinationZipCode: vi.fn(),
    updateDestinationCity: vi.fn(),
    updateTaskDescription: vi.fn(),
    updateCreateCustomer: vi.fn(),
    selectCustomer: vi.fn(),
    updateSnapshotField: vi.fn(),
    updateEditSnapshot: vi.fn(),
    updateAssignedUsers: vi.fn(),
    updateLinkedJobs: vi.fn(),
    updateCustomerObservations: vi.fn(),
    updateTechnicalObservations: vi.fn(),
    updateWorkCategories: vi.fn(),
    updateWorkKind: vi.fn(),
    updateCustomWorkKind: vi.fn(),
    toggleControlPoint: vi.fn(),
    toggleCategoryIrrelevant: vi.fn(),
    upsertWorksheet: vi.fn(),
    deleteWorksheet: vi.fn(),
    updateClosureFlags: vi.fn(),
  } as unknown as CompletedJobDetailsState;
}

describe('CompletedJobEditForm', () => {
  it('shows only fields backed by the simple-job flow for Diverse jobs', () => {
    render(<CompletedJobEditForm details={createDetails('Diverse')} onCancel={vi.fn()} onSave={vi.fn()} />);

    expect(screen.getByText('destination-address')).toBeInTheDocument();
    expect(screen.getByText('textarea:Opgavebeskrivelse')).toBeInTheDocument();
    expect(screen.getByText('worksheets')).toBeInTheDocument();

    expect(screen.queryByText('customer-details')).not.toBeInTheDocument();
    expect(screen.queryByText('assignment')).not.toBeInTheDocument();
    expect(screen.queryByText('linked-jobs')).not.toBeInTheDocument();
    expect(screen.queryByText('textarea:Oplysninger til kunden')).not.toBeInTheDocument();
    expect(screen.queryByText('textarea:Skriv en kommentar til sagen')).not.toBeInTheDocument();
    expect(screen.queryByText('work-category:work-kind')).not.toBeInTheDocument();
    expect(screen.queryByText('work-category:categories')).not.toBeInTheDocument();
    expect(screen.queryByText('control-points')).not.toBeInTheDocument();
    expect(screen.queryByText('job-completion')).not.toBeInTheDocument();
  });

  it('preserves the existing KLS edit fields with the current note labels', () => {
    render(<CompletedJobEditForm details={createDetails('KLS')} onCancel={vi.fn()} onSave={vi.fn()} />);

    expect(screen.getByText('customer-details')).toBeInTheDocument();
    expect(screen.getByText('assignment')).toBeInTheDocument();
    expect(screen.getByText('linked-jobs')).toBeInTheDocument();
    expect(screen.getByText('textarea:Opgavebeskrivelse')).toBeInTheDocument();
    expect(screen.getByText('textarea:Oplysninger til kunden')).toBeInTheDocument();
    expect(screen.getByText('textarea:Skriv en kommentar til sagen')).toBeInTheDocument();
    expect(screen.getByText('work-category:work-kind')).toBeInTheDocument();
    expect(screen.getByText('work-category:categories')).toBeInTheDocument();
    expect(screen.getByText('control-points')).toBeInTheDocument();
    expect(screen.getByText('job-completion')).toBeInTheDocument();
    expect(screen.getByText('worksheets')).toBeInTheDocument();

    expect(screen.queryByText('destination-address')).not.toBeInTheDocument();
  });
});
