import { FileText, MessageSquare, Wrench, FileCheck2, CheckCircle2, X, Save, Loader2 } from 'lucide-react';
import { AssignmentBlock, CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from './JobDetailBlocks';
import { ControlPointsStep } from './steps/ControlPointsStep';
import { JobCompletionStep } from './steps/JobCompletionStep';
import { JobWorksheetsStep } from './steps/JobWorksheetsStep';
import { WorkCategoryStep } from './steps/WorkCategoryStep';
import type { CompletedJobDetailsState } from './completedJobTypes';

type CompletedJobEditFormProps = {
  details: CompletedJobDetailsState;
  onCancel: () => void;
  onSave: () => void;
};

export function CompletedJobEditForm({ details, onCancel, onSave }: CompletedJobEditFormProps) {
  if (!details.job) return null;

  return (
    <>
      <CustomerDetailsBlock
        form={details.form}
        customerSnapshot={details.form.customerSnapshot}
        editSnapshot={details.form.editSnapshot}
        onCustomerSelect={details.selectCustomer}
        onSnapshotFieldChange={details.updateSnapshotField}
        onEditSnapshotChange={details.updateEditSnapshot}
        showEditCheckbox={true}
      />
      <AssignmentBlock assignment={{
          users: details.assignableUsers!,
          assignedUserIds: details.assignedUserIds,
          isLoadingUsers: details.isLoadingUsers,
          onAssignedUsersChange: details.updateAssignedUsers,
        }} />

      <LinkedJobsBlock
        jobs={details.linkableJobs}
        linkedJobIds={details.linkedJobIds}
        isLoading={details.isLoadingJobs}
        onChange={details.updateLinkedJobs}
      />

      <section className="detail-section attestation-summary-section">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Observationer og noter</h3>
        </div>
        <TextAreaBlock
          icon={<FileText size={18} />}
          title="Opgave"
          value={details.form.taskDescription}
          onChange={details.updateTaskDescription}
          placeholder="Beskriv opgaven..."
        />
        <div className="form-divider" />
        <TextAreaBlock
          icon={<MessageSquare size={18} />}
          title="Kundeinfo"
          value={details.form.customerObservations}
          onChange={details.updateCustomerObservations}
          placeholder="Notér oplysninger til kunden..."
        />
        <div className="form-divider" />
        <TextAreaBlock
          icon={<Wrench size={18} />}
          title="Teknisk"
          value={details.form.technicalObservations}
          onChange={details.updateTechnicalObservations}
          placeholder="Notér tekniske observationer..."
        />
      </section>

      <section className="detail-section">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Opgavetype</h3>
        </div>
        <WorkCategoryStep
          form={details.form}
          referenceData={details.referenceData}
          isLoading={details.isLoadingReferenceData}
          onCategoriesChange={details.updateWorkCategories}
          onWorkKindChange={details.updateWorkKind}
          onCustomWorkKindChange={details.updateCustomWorkKind}
          mode="work-kind"
        />
      </section>

      <section className="detail-section">
        <div className="section-header-row attestation-compact-header">
          <CheckCircle2 size={18} />
          <h3>Anlægstyper og kontrolpunkter</h3>
        </div>
        <div className="detail-section-spacer">
          <WorkCategoryStep
            form={details.form}
            referenceData={details.referenceData}
            isLoading={details.isLoadingReferenceData}
            onCategoriesChange={details.updateWorkCategories}
            onWorkKindChange={details.updateWorkKind}
            onCustomWorkKindChange={details.updateCustomWorkKind}
            mode="categories"
          />
        </div>
        <ControlPointsStep
          form={details.form}
          referenceData={details.referenceData}
          onToggleControlPoint={details.toggleControlPoint}
          onToggleCategoryIrrelevant={details.toggleCategoryIrrelevant}
        />
      </section>

      <JobWorksheetsStep
        jobId={details.job.id}
        worksheets={details.worksheets}
        totalHours={details.job.totalHours}
        totalOutlay={details.job.totalOutlay}
        assignableUsers={details.assignableUsers!}
        isLoadingUsers={details.isLoadingUsers}
        isSaving={details.isSavingWorksheet}
        isDeleting={details.isDeletingWorksheet}
        onUpsert={details.upsertWorksheet}
        onDelete={details.deleteWorksheet}
        variant="list"
      />

      <JobCompletionStep
        form={details.form}
        referenceData={details.referenceData}
        isLoading={details.isLoadingReferenceData}
        onClosureFlagsChange={details.updateClosureFlags}
        worksheetCount={details.worksheets.length}
      />

      <div className="edit-form-bottom-actions">
        <button className="btn btn-secondary edit-form-bottom-btn" type="button" onClick={onCancel} disabled={details.saveStatus === 'saving'}>
          <X size={18} />
          Annuller
        </button>
        <button className="btn btn-primary edit-form-bottom-btn edit-form-save-btn" type="button" onClick={onSave} disabled={details.saveStatus === 'saving'}>
          {details.saveStatus === 'saving' ? <Loader2 size={18} className="spin" /> : <Save size={18} />}
          {details.saveStatus === 'saving' ? 'Gemmer...' : 'Gem ændringer'}
        </button>
      </div>
    </>
  );
}
