import { FileText, MessageSquare, Wrench } from 'lucide-react';
import type { useJobCreate } from '../../hooks/useJobCreate';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from '../JobDetailBlocks';
import type { LinkableJob } from '../../types';

type JobCreateState = ReturnType<typeof useJobCreate>;

type CreateOverviewStepProps = {
  create: JobCreateState;
  linkableJobs: LinkableJob[];
  isLoadingJobs: boolean;
};

export function CreateOverviewStep({ create, linkableJobs, isLoadingJobs }: CreateOverviewStepProps) {
  return (
    <>
      <CustomerDetailsBlock
        form={create.form}
        onCustomerSelect={create.selectCustomer}
        onReportNumberChange={create.updateReportNumber}
        assignment={{
          users: create.assignableUsers,
          assignedUserIds: create.assignedUserIds,
          isLoadingUsers: create.isLoadingUsers,
          onAssignedUsersChange: create.updateAssignedUsers,
        }}
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
