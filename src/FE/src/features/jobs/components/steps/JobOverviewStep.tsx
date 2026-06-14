import { FileText, MessageSquare, Wrench } from 'lucide-react';
import type { useJobDetails } from '../../hooks/useJobDetails';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from '../JobDetailBlocks';
import { useCan } from '../../../../providers/permissions';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type JobOverviewStepProps = {
  details: JobDetailsState;
};

export function JobOverviewStep({ details }: JobOverviewStepProps) {
  const canAssign = useCan('job:assign');
  return (
    <>
      <CustomerDetailsBlock
        form={details.form}
        reportNumberReadOnly={details.reportNumberReadOnly}
        {...(canAssign
          ? {
              assignment: {
                users: details.assignableUsers!,
                assignedUserIds: details.assignedUserIds,
                isLoadingUsers: details.isLoadingUsers,
                onAssignedUsersChange: details.updateAssignedUsers,
              },
            }
          : {
              readOnlyAssigned: details.job?.assignedUsers ?? [],
            })}
        onCustomerSelect={details.selectCustomer}
        onReportNumberChange={details.updateReportNumber}
      />
      <LinkedJobsBlock
        jobs={details.linkableJobs}
        linkedJobIds={details.linkedJobIds}
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
        title="Oplysninger til kunden"
        value={details.form.customerObservations}
        onChange={details.updateCustomerObservations}
        placeholder="Notér oplysninger til kunden..."
      />
      <TextAreaBlock
        icon={<Wrench size={18} />}
        title="Tekniske observationer"
        value={details.form.technicalObservations}
        onChange={details.updateTechnicalObservations}
        placeholder="Notér tekniske observationer..."
      />
    </>
  );
}
