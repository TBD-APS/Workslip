import { AlertCircle, CheckCircle2, Clock, FileCheck2, Loader2, ShieldCheck } from 'lucide-react';
import type { useJobDetails } from '../../hooks/useJobDetails';
import { Checkbox } from '../../../../components/forms/Checkbox';

type JobDetailsState = ReturnType<typeof useJobDetails>;

const NUMBER_FORMATTER = new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 });
const DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: '2-digit', month: '2-digit', year: 'numeric' });

type JobAttestationStepProps = {
  details: JobDetailsState;
  confirmed: boolean;
  onConfirmedChange: (confirmed: boolean) => void;
  onSubmitted: () => void;
};

type SummaryItemViewModel = { label: string; value: string };
type ObservationViewModel = { label: string; value: string };

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
  const sortedWorksheets = [...details.worksheets].sort((left, right) => right.workDate.localeCompare(left.workDate));
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
  const selectedInstallationTypeNames = job.work.installationTypes
    .map((installationType) => installationType.name)
    .filter(hasText);
  const summaryItems = compactSummaryItems([
    { label: 'Sag', value: job.reportNumber },
    { label: 'Kunde', value: job.customer.name },
    { label: 'Adresse', value: job.customer.address },
    { label: 'Kontakt', value: formatContact(job.customer.contactPerson, job.customer.phone) },
    { label: 'Opgavetype', value: formatWorkKind(job.work.workKind) },
    { label: 'Anlægstyper', value: selectedInstallationTypeNames.join(', ') },
    { label: 'Status', value: formatStatus(job.status) },
  ]);
  const observationItems = compactObservations([
    { label: 'Opgave', value: job.observations.taskDescription },
    { label: 'Kundeinfo', value: job.observations.customerObservations },
    { label: 'Teknisk', value: job.observations.technicalObservations },
    { label: 'Bemærkninger', value: job.work.remarks },
  ]);
  const totalHoursLabel = `${formatNumber(job.totalHours)} ${formatUnit(parseNullableNumber(job.totalHours), 'time', 'timer')}`;
  const totalOutlayValue = parseNullableNumber(job.totalOutlay);

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
            <span className="attestation-status-title">{isSubmitted ? 'Sagen er attesteret' : 'Klar til attestering'}</span>
            <span>
              {isSubmitted
                ? 'Backend har registreret sagen som indsendt.'
                : 'Tjek kun de valgte oplysninger, timesedler og kontrolpunkter før indsendelse.'}
            </span>
          </div>
        </div>
      </section>

      {summaryItems.length > 0 && (
        <section className="detail-section attestation-summary-section">
          <div className="section-header-row attestation-compact-header">
            <FileCheck2 size={18} />
            <h3>Valgte data</h3>
          </div>

          <div className="attestation-summary-grid compact">
            {summaryItems.map((item) => <SummaryItem key={item.label} label={item.label} value={item.value} />)}
          </div>
        </section>
      )}

      <section className="detail-section attestation-timesheet-section">
        <div className="attestation-timesheet-heading">
          <div className="section-header-row attestation-compact-header">
            <FileCheck2 size={18} />
            <h3>Timesedler</h3>
          </div>
          <div className="attestation-timesheet-totals" aria-label="Timeseddel totaler">
            <span>{totalHoursLabel}</span>
            {totalOutlayValue > 0 && <span>{formatNumber(totalOutlayValue)} {formatUnit(totalOutlayValue, 'udlæg', 'udlæg')}</span>}
          </div>
        </div>

        {sortedWorksheets.length === 0 ? (
          <p className="empty-state-text">Ingen timesedler registreret.</p>
        ) : (
          <ul className="attestation-timesheet-list">
            {sortedWorksheets.map((worksheet) => {
              const hours = parseNullableNumber(worksheet.hoursWorked);
              return (
                <li key={worksheet.id}>
                  <div className="attestation-timesheet-main">
                    <span className="attestation-timesheet-date">{formatDate(worksheet.workDate)}</span>
                    <span className="attestation-timesheet-user">{getUserName(worksheet.userId, details)}</span>
                  </div>
                  <div className="attestation-timesheet-hours">
                    <span className="attestation-timesheet-hours-value">{formatNumber(hours)}</span>
                    <span>{formatUnit(hours, 'time', 'timer')}</span>
                  </div>
                  {worksheet.sleptOnJob && <span className="attestation-timesheet-badge">Udlæg</span>}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      {observationItems.length > 0 && (
        <section className="detail-section attestation-observations-section compact">
          {observationItems.map((item) => (
            <div key={item.label} className="attestation-observation-block compact">
              <span>{item.label}</span>
              <p>{item.value}</p>
            </div>
          ))}
        </section>
      )}

      {(selectedControlPoints.length > 0 || irrelevantCategories.length > 0) && (
        <section className="detail-section attestation-control-section compact">
          <div className="section-header-row attestation-compact-header">
            <CheckCircle2 size={18} />
            <h3>Valgte kontrolpunkter</h3>
          </div>

          {selectedControlPoints.length > 0 && (
            <ul className="attestation-control-list compact">
              {selectedControlPoints.map((controlPoint) => (
                <li key={controlPoint.id}>
                  <span className="attestation-control-accent" aria-hidden="true" />
                  <span>{controlPoint.name}</span>
                  <small>{controlPoint.installationType} · {capitalize(controlPoint.category)}</small>
                </li>
              ))}
            </ul>
          )}

          {irrelevantCategories.length > 0 && (
            <div className="attestation-muted-list compact">
              <span>Markeret irrelevant</span>
              <p>{irrelevantCategories.map((item) => `${item.installationType} · ${capitalize(item.category)}`).join(', ')}</p>
            </div>
          )}
        </section>
      )}

      <section className="detail-section attestation-confirm-section">
        {details.submitJobFieldErrors.length > 0 && (
          <div className="validation-error attestation-validation-error">
            <AlertCircle size={18} />
            <div>
              <span className="attestation-validation-title">Sagen mangler oplysninger før attestering:</span>
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
    <div className="attestation-summary-item compact">
      <span className="attestation-summary-label">{label}</span>
      <span className="attestation-summary-value">{value}</span>
    </div>
  );
}

function compactSummaryItems(items: Array<{ label: string; value: string | null | undefined }>): SummaryItemViewModel[] {
  return items.flatMap((item) => hasText(item.value) ? [{ label: item.label, value: item.value.trim() }] : []);
}

function compactObservations(items: Array<{ label: string; value: string | null | undefined }>): ObservationViewModel[] {
  return items.flatMap((item) => hasText(item.value) ? [{ label: item.label, value: item.value.trim() }] : []);
}

function hasText(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function getUserName(userId: string, details: JobDetailsState) {
  return details.assignableUsers.find((user) => user.id === userId)?.displayName
    ?? details.job?.assignedUsers.find((user) => user.id === userId)?.displayName
    ?? userId;
}

function formatContact(contactPerson: string | null | undefined, phone: string | null | undefined) {
  return [contactPerson, phone].filter(hasText).join(' · ');
}

function formatNumber(value: number | string | null) {
  const numberValue = parseNullableNumber(value);
  return NUMBER_FORMATTER.format(numberValue);
}

function parseNullableNumber(value: number | string | null) {
  if (value === null) return 0;
  const numberValue = typeof value === 'number' ? value : Number(value.replace(',', '.'));
  return Number.isFinite(numberValue) ? numberValue : 0;
}

function formatUnit(value: number, singular: string, plural: string) {
  return Math.abs(value) === 1 ? singular : plural;
}

function formatDate(value: string) {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return DATE_FORMATTER.format(new Date(year, month - 1, day));
}

function formatWorkKind(workKind: { label?: string | null; customWorkKind?: string | null } | null) {
  if (!workKind) return '';
  return workKind.customWorkKind || workKind.label || '';
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
