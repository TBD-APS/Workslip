import { FileText, MessageSquare, Wrench } from 'lucide-react';
import type { useJobCreate } from '../../hooks/useJobCreate';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock, AssignmentBlock, DestinationAddressBlock } from '../JobDetailBlocks';
import type { LinkableJob } from '../../types';
import { useIsAdmin } from '../../../../providers/permissions';

type JobCreateState = ReturnType<typeof useJobCreate>;

type CreateOverviewStepProps = {
  create: JobCreateState;
  linkableJobs: LinkableJob[];
  isLoadingJobs: boolean;
};

export function CreateOverviewStep({ create, linkableJobs, isLoadingJobs }: CreateOverviewStepProps) {
  const isAdmin = useIsAdmin();
  return (
    <>
      <DestinationAddressBlock
        value={create.form.destinationAddress}
        onChange={create.updateDestinationAddress}
        required={isAdmin}
        error={create.fieldErrors.destinationAddress}
      />

      <CustomerDetailsBlock
        form={create.form}
        customerSnapshot={create.form.customerSnapshot}
        editSnapshot={create.form.editSnapshot}
        createCustomer={create.form.createCustomer}
        onCreateCustomerChange={create.updateCreateCustomer}
        hasCustomerChanges={create.hasCustomerChanges}
        onCustomerSelect={create.selectCustomer}
        onCreateNewCustomer={create.createNewCustomer}
        onSnapshotFieldChange={create.updateSnapshotField}
        onEditSnapshotChange={create.updateEditSnapshot}
        showEditCheckbox={true}
        fieldErrors={create.fieldErrors}
      />


      <AssignmentBlock
        assignment={{
          users: create.assignableUsers,
          assignedUserIds: create.assignedUserIds,
          isLoadingUsers: create.isLoadingUsers,
          onAssignedUsersChange: create.updateAssignedUsers,
        }}
        isEditing={true}
      />

      <LinkedJobsBlock
        jobs={linkableJobs}
        linkedJobIds={create.linkedJobIds}
        isLoading={isLoadingJobs}
        onChange={create.updateLinkedJobs}
      />

      <TextAreaBlock
        icon={<FileText size={18} />}
        title="Opgavebeskrivelse"
        value={create.form.taskDescription}
        onChange={create.updateTaskDescription}
        placeholder="Beskriv opgaven..."
      />

      <TextAreaBlock
        icon={<MessageSquare size={18} />}
        title="Oplysninger til kunden"
        value={create.form.customerObservations}
        onChange={create.updateCustomerObservations}
        placeholder="Notér oplysninger til kunden..."
      />

      <TextAreaBlock
        icon={<Wrench size={18} />}
        title="Tekniske observationer"
        value={create.form.technicalObservations}
        onChange={create.updateTechnicalObservations}
        placeholder="Notér tekniske observationer..."
      />
    </>
  );
}
