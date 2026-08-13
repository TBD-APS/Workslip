import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { CreateOverviewStep } from './CreateOverviewStep';
import { JobOverviewStep } from './JobOverviewStep';

const { permissionState } = vi.hoisted(() => ({
  permissionState: { isAdmin: false },
}));

vi.mock('../../../../providers/permissions', () => ({
  useIsAdmin: () => permissionState.isAdmin,
  useCan: () => permissionState.isAdmin,
}));

vi.mock('../../../../providers/useAuth', () => ({
  useAuth: () => ({
    user: {
      id: 'user-1',
      displayName: 'Testbruger',
      role: permissionState.isAdmin ? 'Admin' : 'User',
    },
  }),
}));

vi.mock('../JobDetailBlocks', () => ({
  DestinationAddressBlock: () => null,
  CustomerDetailsBlock: ({
    onCreateCustomerChange,
  }: {
    onCreateCustomerChange?: (value: boolean) => void;
  }) =>
    onCreateCustomerChange ? (
      <label>
        Opret kunde
        <input type="checkbox" />
      </label>
    ) : null,
  AssignmentBlock: () => null,
  LinkedJobsBlock: () => null,
  TextAreaBlock: () => null,
}));

const baseForm = {
  customerId: null,
  customerSnapshot: null,
  editSnapshot: true,
  createCustomer: false,
  reportNumber: '',
  destinationAddress: '',
  destinationZipCode: '',
  destinationCity: '',
  jobType: 'KLS',
  taskDescription: '',
  customerObservations: '',
  technicalObservations: '',
};

function createState(assignedUserIds = ['user-1']) {
  return {
    form: { ...baseForm },
    assignedUserIds,
    duplicatePerAssignedUser: false,
    assignableUsers: [],
    isLoadingUsers: false,
    linkedJobIds: [],
    fieldErrors: {},
    updateDestinationAddress: vi.fn(),
    updateDestinationZipCode: vi.fn(),
    updateDestinationCity: vi.fn(),
    updateCreateCustomer: vi.fn(),
    selectCustomer: vi.fn(),
    createNewCustomer: vi.fn(),
    updateSnapshotField: vi.fn(),
    updateEditSnapshot: vi.fn(),
    updateAssignedUsers: vi.fn(),
    updateDuplicatePerAssignedUser: vi.fn(),
    updateLinkedJobs: vi.fn(),
    updateTaskDescription: vi.fn(),
    updateCustomerObservations: vi.fn(),
    updateTechnicalObservations: vi.fn(),
  } as never;
}

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

afterEach(() => {
  cleanup();
  permissionState.isAdmin = false;
});

describe('create-customer option visibility', () => {
  it('hides the option from users and preserves it for admins when creating a job', () => {
    const { rerender } = render(
      <CreateOverviewStep create={createState()} linkableJobs={[]} isLoadingJobs={false} />,
    );

    expect(screen.queryByRole('checkbox', { name: 'Opret kunde' })).not.toBeInTheDocument();

    permissionState.isAdmin = true;
    rerender(<CreateOverviewStep create={createState()} linkableJobs={[]} isLoadingJobs={false} />);

    expect(screen.getByRole('checkbox', { name: 'Opret kunde' })).toBeInTheDocument();
  });

  it('hides the option from users and preserves it for admins when editing a job', () => {
    const { rerender } = render(<JobOverviewStep details={detailsState(false)} />);

    expect(screen.queryByRole('checkbox', { name: 'Opret kunde' })).not.toBeInTheDocument();

    rerender(<JobOverviewStep details={detailsState(true)} />);

    expect(screen.getByRole('checkbox', { name: 'Opret kunde' })).toBeInTheDocument();
  });
});

describe('duplicate-per-assignee option', () => {
  it('lets an admin opt in when more than one employee is selected', () => {
    permissionState.isAdmin = true;
    const create = createState(['user-1', 'user-2']);

    render(<CreateOverviewStep create={create} linkableJobs={[]} isLoadingJobs={false} />);

    const checkbox = screen.getByRole('checkbox', {
      name: /Opret en kopi af sagen til hver medarbejder/,
    });
    fireEvent.click(checkbox);

    expect(create.updateDuplicatePerAssignedUser).toHaveBeenCalledWith(true);
    expect(screen.getByText(/udfyldes og godkendes separat/)).toBeInTheDocument();
  });

  it('stays hidden for one assignee and for non-admins', () => {
    permissionState.isAdmin = true;
    const { rerender } = render(
      <CreateOverviewStep create={createState(['user-1'])} linkableJobs={[]} isLoadingJobs={false} />,
    );
    expect(screen.queryByRole('checkbox', {
      name: /Opret en kopi af sagen til hver medarbejder/,
    })).not.toBeInTheDocument();

    permissionState.isAdmin = false;
    rerender(
      <CreateOverviewStep create={createState(['user-1', 'user-2'])} linkableJobs={[]} isLoadingJobs={false} />,
    );
    expect(screen.queryByRole('checkbox', {
      name: /Opret en kopi af sagen til hver medarbejder/,
    })).not.toBeInTheDocument();
  });
});
