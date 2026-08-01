import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JobOverviewStep } from './JobOverviewStep';

vi.mock('../../../../providers/permissions', () => ({
  useCan: () => false,
}));

vi.mock('../JobDetailBlocks', () => ({
  DestinationAddressBlock: ({ readOnly }: { readOnly?: boolean }) => (
    <input aria-label="Adresse (destination)" readOnly={readOnly} />
  ),
  CustomerDetailsBlock: () => null,
  AssignmentBlock: () => null,
  LinkedJobsBlock: () => null,
  TextAreaBlock: () => null,
}));

const baseForm = {
  customerId: null,
  customerSnapshot: null,
  editSnapshot: false,
  createCustomer: false,
  reportNumber: '123',
  destinationAddress: 'Testvej 1',
  destinationZipCode: '8000',
  destinationCity: 'Aarhus C',
  jobType: 'KLS',
  taskDescription: '',
  customerObservations: '',
  technicalObservations: '',
};

function detailsState(isAdmin: boolean) {
  return {
    isAdmin,
    form: { ...baseForm },
    assignableUsers: [],
    assignedUserIds: [],
    isLoadingUsers: false,
    job: { assignedUsers: [] },
    linkableJobs: [],
    linkedJobIds: [],
    isLoadingJobs: false,
    updateDestinationAddress: vi.fn(),
    updateDestinationZipCode: vi.fn(),
    updateDestinationCity: vi.fn(),
    updateCreateCustomer: vi.fn(),
    selectCustomer: vi.fn(),
    updateSnapshotField: vi.fn(),
    updateEditSnapshot: vi.fn(),
    updateAssignedUsers: vi.fn(),
    updateLinkedJobs: vi.fn(),
    updateTaskDescription: vi.fn(),
    updateCustomerObservations: vi.fn(),
    updateTechnicalObservations: vi.fn(),
  } as never;
}

afterEach(cleanup);

describe('JobOverviewStep destination permissions', () => {
  it('makes destination readonly for users and editable for admins', () => {
    const { rerender } = render(<JobOverviewStep details={detailsState(false)} />);

    expect(screen.getByRole('textbox', { name: 'Adresse (destination)' })).toHaveAttribute('readonly');

    rerender(<JobOverviewStep details={detailsState(true)} />);

    expect(screen.getByRole('textbox', { name: 'Adresse (destination)' })).not.toHaveAttribute('readonly');
  });
});
