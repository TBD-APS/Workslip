import { AlertCircle, CheckCircle2, Clock, FileCheck2, Loader2, ShieldCheck } from 'lucide-react';
import type { useJobDetails } from '../../hooks/useJobDetails';
import { Checkbox } from '../../../../components/forms/Checkbox';

type JobDetailsState = ReturnType<typeof useJobDetails>;

const NUMBER_FORMATTER = new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 });

type JobAttestationStepProps = {
  details: JobDetailsState;
  confirmed: boolean;
  onConfirmedChange: (confirmed: boolean) => void;
  onSubmitted: () => void;
};

export function JobAttestationStep({
  details,
  confirmed,
  onConfirmedChange,
  onSubmitted,
}: JobAttestationStepProps) {
  const job = details.job;
  if (!job) return null;

  const isSubmitted = job.status === 'Submitted';
  const isSavingDraft = details.saveStatus === 'saving';
  const worksheetCount = details.worksheets.length;
  const selectedControlPoints = job.work.installationTypes.flatMap((installationType) =>
    installationType.categories.flatMap((category) =>
      category.controlPoints
        .filter((controlPoint) => controlPoint.isChecked)
        .map((controlPoint) => ({
          id: controlPoint.id,
          installationType: installationType.name,
          category: category.name,
          name: controlPoint.name,
        })),
    ),
  );
  const irrelevantCategories = job.work.installationTypes.flatMap((installationType) =>
    installationType.categories
      .filter((category) => category.isIrrelevant)
      .map((category) => ({
        id: `${installationType.id}-${category.id}`,
        installationType: installationType.name,
        category: category.name,
      })),
  );

  const handleSubmit = async () => {
    try {
      await details.submitJob();
      onSubmitted();
    } catch {
      return;
    }
  };

  return (
    <>
      <section className="detail-section attestation-hero-section">
        <div className="section-header-row">
          <ShieldCheck size={18} />
          <h3>Digital attestering</h3>
        </div>

        <div className={isSubmitted ? 'attestation-status submitted' : 'attestation-status'}>
          {isSubmitted ? <CheckCircle2 size={20} /> : <Clock size={20} />}
          <div>
            <strong>{isSubmitted ? 'Sagen er attesteret' : 'Klar til attestering'}</strong>
            <span>
              {isSubmitted
                ? 'Backend har registreret sagen som indsendt.'
                : 'Gennemgå opsummeringen og bekræft, at oplysningerne er korrekte.'}
            </span>
          </div>
        </div>
      </section>

      <section className="detail-section attestation-summary-section">
        <div className="section-header-row">
          <FileCheck2 size={18} />
          <h3>Opsummering</h3>
        </div>

        <div className="attestation-summary-grid">
          <SummaryItem label="Sagsnummer" value={job.reportNumber || 'Ikke angivet'} />
          <SummaryItem label="Kunde" value={job.customer.name || 'Ikke angivet'} />
          <SummaryItem label="Adresse" value={job.customer.address || 'Ikke angivet'} />
          <SummaryItem label="Kontakt" value={job.customer.contactPerson || job.customer.phone || 'Ikke angivet'} />
          <SummaryItem label="Arbejdstype" value={formatWorkKind(job.work.workKind)} />
          <SummaryItem label="Kategorier" value={job.work.installationTypes.map((type) => type.name).join(', ') || 'Ikke valgt'} />
          <SummaryItem label="Arbejdssedler" value={`${worksheetCount} ${worksheetCount === 1 ? 'arbejdsseddel' : 'arbejdssedler'}`} />
          <SummaryItem label="Timer" value={`${formatNumber(job.totalHours)} timer`} />
          <SummaryItem label="Udlæg" value={`${formatNumber(job.totalOutlay)} dage`} />
          <SummaryItem label="Status" value={formatStatus(job.status)} />
        </div>
      </section>

      <section className="detail-section attestation-observations-section">
        <div className="attestation-observation-block">
          <span>Opgavebeskrivelse</span>
          <p>{job.observations.taskDescription || 'Ingen opgavebeskrivelse.'}</p>
        </div>
        <div className="attestation-observation-block">
          <span>Oplysninger til kunden</span>
          <p>{job.observations.customerObservations || 'Ingen oplysninger til kunden.'}</p>
        </div>
        <div className="attestation-observation-block">
          <span>Tekniske observationer</span>
          <p>{job.observations.technicalObservations || 'Ingen tekniske observationer.'}</p>
        </div>
      </section>

      <section className="detail-section attestation-control-section">
        <div className="section-header-row">
          <CheckCircle2 size={18} />
          <h3>Kontrolpunkter</h3>
        </div>

        {selectedControlPoints.length === 0 ? (
          <p className="empty-state-text">Ingen kontrolpunkter valgt.</p>
        ) : (
          <ul className="attestation-control-list">
            {selectedControlPoints.map((controlPoint) => (
              <li key={controlPoint.id}>
                <CheckCircle2 size={15} />
                <div>
                  <strong>{controlPoint.name}</strong>
                  <span>{controlPoint.installationType} · {capitalize(controlPoint.category)}</span>
                </div>
              </li>
            ))}
          </ul>
        )}

        {irrelevantCategories.length > 0 && (
          <div className="attestation-muted-list">
            <span>Markeret irrelevant:</span>
            <p>{irrelevantCategories.map((item) => `${item.installationType} · ${capitalize(item.category)}`).join(', ')}</p>
          </div>
        )}
      </section>

      <section className="detail-section attestation-confirm-section">
        {details.submitJobFieldErrors.length > 0 && (
          <div className="validation-error attestation-validation-error">
            <AlertCircle size={18} />
            <div>
              <strong>Sagen mangler oplysninger før attestering:</strong>
              <ul>
                {details.submitJobFieldErrors.map((error) => (
                  <li key={`${error.field}-${error.message}`}>{error.message}</li>
                ))}
              </ul>
            </div>
          </div>
        )}

        <Checkbox
          checked={confirmed || isSubmitted}
          disabled={isSubmitted || details.isSubmittingJob || isSavingDraft}
          label="Jeg bekræfter, at sagen er gennemgået og klar til indsendelse"
          description="Attestering registreres som indsendt sag med den aktuelle bruger hos backend."
          onChange={() => onConfirmedChange(!confirmed)}
        />

        <button
          type="button"
          className="btn btn-primary attestation-submit-button"
          onClick={handleSubmit}
          disabled={!confirmed || isSubmitted || details.isSubmittingJob || isSavingDraft}
        >
          {details.isSubmittingJob || isSavingDraft ? <Loader2 className="animate-spin" size={16} /> : <ShieldCheck size={16} />}
          <span>{isSavingDraft ? 'Gemmer...' : details.isSubmittingJob ? 'Indsender...' : isSubmitted ? 'Attesteret' : 'Attestér og indsend'}</span>
        </button>
      </section>
    </>
  );
}

function SummaryItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="attestation-summary-item">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function formatNumber(value: number | string | null) {
  if (value === null) return '0';
  const numberValue = typeof value === 'number' ? value : Number(value.replace(',', '.'));
  if (!Number.isFinite(numberValue)) return '0';
  return NUMBER_FORMATTER.format(numberValue);
}

function formatWorkKind(workKind: { label?: string | null; customWorkKind?: string | null } | null) {
  if (!workKind) return 'Ikke valgt';
  return workKind.customWorkKind || workKind.label || 'Ikke valgt';
}

function formatStatus(status: string) {
  const labels: Record<string, string> = {
    Draft: 'Kladde',
    Submitted: 'Indsendt',
    InReview: 'Under review',
    Approved: 'Godkendt',
    Rejected: 'Afvist',
    Archived: 'Arkiveret',
  };
  return labels[status] ?? status;
}

function capitalize(value: string) {
  if (!value) return value;
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}
