import { useNavigate } from 'react-router-dom';
import { ArrowLeft, FileText, Loader2, MessageSquare, Save } from 'lucide-react';
import { useGetApiUsers } from '../../../api/generated/users/users';
import { useGetApiJobs } from '../../../api/generated/jobs/jobs';
import { useJobCreate } from '../hooks/useJobCreate';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from '../components/JobDetailBlocks';
import { getUserList, getLinkableJobs } from '../utils';

export const JobCreate = () => {
  const navigate = useNavigate();
  const { data: usersData } = useGetApiUsers();
  const { data: jobsData, isLoading: isLoadingJobs } = useGetApiJobs({ limit: 200 });
  const users = getUserList(usersData);
  const linkableJobs = getLinkableJobs(jobsData, undefined);

  const create = useJobCreate((jobId) => navigate(`/app/job/${jobId}`));

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon" onClick={() => navigate('/app')} aria-label="Tilbage">
          <ArrowLeft size={22} />
        </button>
        <div>
          <h2 className="detail-title">Ny sag</h2>
        </div>
      </div>

      <CustomerDetailsBlock
        form={create.form}
        assignment={{
          users,
          assignedUserIds: create.assignedUserIds,
          assignmentStatus: create.assignmentStatus,
          isLoadingUsers: false,
          onAssignedUsersChange: create.updateAssignedUsers,
        }}
        onCustomerChange={create.updateCustomer}
        onReportNumberChange={create.updateReportNumber}
      />

      <LinkedJobsBlock
        jobs={linkableJobs}
        linkedJobIds={create.linkedJobIds}
        saveStatus={create.linksStatus}
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
        title="Oplysninger til kunden/tekniske observationer"
        value={create.form.customerObservations}
        onChange={create.updateCustomerObservations}
        placeholder="Notér oplysninger til kunden eller tekniske observationer..."
      />

      <div className="step-nav">
        <div />
        <button
          className="step-nav-btn step-nav-btn-next"
          onClick={create.save}
          disabled={create.isSaving || !create.canSave}
        >
          {create.isSaving ? (
            <Loader2 className="animate-spin" size={18} />
          ) : (
            <Save size={18} />
          )}
          <span>{create.isSaving ? 'Gemmer...' : 'Gem'}</span>
        </button>
      </div>
    </div>
  );
};
