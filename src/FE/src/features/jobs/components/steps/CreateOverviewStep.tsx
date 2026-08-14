import { FileText, MessageSquare, Wrench } from 'lucide-react';
import type { useJobCreate } from '../../hooks/useJobCreate';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock, AssignmentBlock, DestinationAddressBlock } from '../JobDetailBlocks';
import type { LinkableJob } from '../../types';
import { useIsAdmin } from '../../../../providers/permissions';
import { useAuth } from '../../../../providers/useAuth';

type JobCreateState = ReturnType<typeof useJobCreate>;

type CreateOverviewStepProps = {
  create: JobCreateState;
  linkableJobs: LinkableJob[];
  isLoadingJobs: boolean;
};

export function CreateOverviewStep({ create, linkableJobs, isLoadingJobs }: CreateOverviewStepProps) {
  const isAdmin = useIsAdmin();
  const { user } = useAuth();
  
  // For non-admins, show the current user as assigned since they can't use the dropdown
  const readOnlyAssigned = !isAdmin && user?.id && create.assignedUserIds.includes(user.id)
    ? [{ id: user.id, displayName: user.displayName }]
    : undefined;

  const isSimpleJob = create.form.jobType === 'Diverse';

  return (
    <>
      <DestinationAddressBlock
        value={create.form.destinationAddress}
        zipCode={create.form.destinationZipCode}
        city={create.form.destinationCity}
        onChange={create.updateDestinationAddress}
        onZipCodeChange={create.updateDestinationZipCode}
        onCityChange={create.updateDestinationCity}
        error={create.fieldErrors.destinationAddress}
      />

      {!isSimpleJob && (
        <>
          <CustomerDetailsBlock
            form={create.form}
            customerSnapshot={create.form.customerSnapshot}
            editSnapshot={create.form.editSnapshot}
            createCustomer={create.form.createCustomer}
            onCreateCustomerChange={isAdmin ? create.updateCreateCustomer : undefined}
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
            readOnlyAssigned={readOnlyAssigned}
            isEditing={true}
          />

          {isAdmin && create.assignedUserIds.length > 1 && (
            <section className="detail-section">
              <label className="attestation-confirm-row">
                <span className="attestation-confirm-copy">
                  <span className="attestation-confirm-label">Opret en kopi af sagen til hver medarbejder</span>
                  <span className="attestation-confirm-description">
                    Hver medarbejder får sin egen sag, som udfyldes og godkendes separat.
                  </span>
                </span>
                <input
                  type="checkbox"
                  checked={create.duplicatePerAssignedUser}
                  onChange={(event) => create.updateDuplicatePerAssignedUser(event.target.checked)}
                />
              </label>
            </section>
          )}

          <LinkedJobsBlock
            jobs={linkableJobs}
            linkedJobIds={create.linkedJobIds}
            isLoading={isLoadingJobs}
            onChange={create.updateLinkedJobs}
          />
        </>
      )}

      <TextAreaBlock
        icon={<FileText size={18} />}
        title="Opgavebeskrivelse"
        value={create.form.taskDescription}
        onChange={create.updateTaskDescription}
        placeholder="Beskriv opgaven..."
      />

      {!isSimpleJob && (
        <>
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
      )}
    </>
  );
}
