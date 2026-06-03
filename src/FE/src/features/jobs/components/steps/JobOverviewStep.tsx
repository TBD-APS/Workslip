import { FileText, MessageSquare } from 'lucide-react';
import type { useJobDetails } from '../../hooks/useJobDetails';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from '../JobDetailBlocks';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type JobOverviewStepProps = {
  details: JobDetailsState;
};

export function JobOverviewStep({ details }: JobOverviewStepProps) {
  return (
    <>
      <CustomerDetailsBlock
        form={details.form}
        reportNumberReadOnly={details.reportNumberReadOnly}
        assignment={{
          users: details.assignableUsers,
          assignedUserIds: details.assignedUserIds,
          assignmentStatus: details.assignmentStatus,
          isLoadingUsers: details.isLoadingUsers,
          onAssignedUsersChange: details.updateAssignedUsers,
        }}
        onCustomerChange={details.updateCustomer}
        onReportNumberChange={details.updateReportNumber}
      />
      <LinkedJobsBlock
        jobs={details.linkableJobs}
        linkedJobIds={details.linkedJobIds}
        saveStatus={details.linksStatus}
        isLoading={details.isLoadingJobs}
        onChange={details.updateLinkedJobs}
      />
      <TextAreaBlock
        icon={<FileText size={18} />}
        title="Opgavebeskrivelse"
        value={details.form.taskDescription}
        onChange={details.updateTaskDescription}
        placeholder="Beskriv opgaven..."
      />
      <TextAreaBlock
        icon={<MessageSquare size={18} />}
        title="Oplysninger til kunden/tekniske observationer"
        value={details.form.customerObservations}
        onChange={details.updateCustomerObservations}
        placeholder="Notér oplysninger til kunden eller tekniske observationer..."
      />
    </>
  );
}
