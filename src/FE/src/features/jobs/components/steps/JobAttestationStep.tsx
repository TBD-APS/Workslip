import { AlertCircle, CheckCircle2, Clock, FileCheck2, Loader2, ShieldCheck } from 'lucide-react';
import { JobStatus } from '../../../../api/generated/models/jobStatus';
import type { useJobDetails } from '../../hooks/useJobDetails';

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

  const isInReview = job.status === JobStatus.InReview;
  const isSavingDraft = details.saveStatus === 'saving';
  const confirmationDisabled = isInReview || details.isSubmittingJob || isSavingDraft;
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
    { label: 'Kontakt', value: formatContact(job.customer.contactPerson, job.customer.phone, job.customer.email) },
    { label: 'Opgavetype', value: formatWorkKind(job.work.workKind) },
    { label: 'Anlægstyper', value: selectedInstallationTypeNames.join(', ') },
  ]);
  const observationItems = compactObservations([
    { label: 'Opgave', value: job.observations.taskDescription },
    { label: 'Kundeinfo', value: job.observations.customerObservations },
    { label: 'Teknisk', value: job.observations.technicalObservations },
    { label: 'Bemærkninger', value: job.work.remarks },
  ]);
  const totalHoursValue = parseNullableNumber(job.totalHours);
  const totalHoursLabel = (
    <span><strong>{formatNumber(job.totalHours)}</strong> {formatUnit(totalHoursValue, 'time', 'timer')}</span>
  );
  const totalOutlayValue = parseNullableNumber(job.totalOutlay);
  const totalOutlayLabel = totalOutlayValue > 0 ? (
    <span><strong>{formatNumber(totalOutlayValue)}</strong> {formatUnit(totalOutlayValue, 'udlæg', 'udlæg')}</span>
  ) : null;
  const selectedClosureFlags = job.work.closureFlags ?? [];

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

        <div className={isInReview ? 'attestation-status submitted' : 'attestation-status'}>
          {isInReview ? <CheckCircle2 size={20} /> : <Clock size={20} />}
          <div>
            <span className="attestation-status-title">{isInReview ? 'Sagen er attesteret' : 'Klar til attestering'}</span>
            <span>
              {isInReview
                ? 'Backend har registreret sagen som indsendt.'
                : 'Tjek de valgte oplysninger, timesedler og kontrolpunkter før indsendelse.'}
            </span>
          </div>
        </div>
      </section>

      {(summaryItems.length > 0 || observationItems.length > 0) && (
        <section className="detail-section attestation-summary-section">
          <div className="section-header-row attestation-compact-header">
            <FileCheck2 size={18} />
            <h3>Information</h3>
          </div>

          {summaryItems.length > 0 && (
            <dl className="attestation-data-list">
              {summaryItems.map((item) => (
                <div key={item.label} className="attestation-data-pair">
                  <dt>{item.label}</dt>
                  <dd>{item.value}</dd>
                </div>
              ))}
            </dl>
          )}

          {observationItems.length > 0 && (
            <div className="attestation-observations-list">
              {observationItems.map((item) => (
                <div key={item.label} className="attestation-data-pair observation">
                  <dt>{item.label}</dt>
                  <dd>{item.value}</dd>
                </div>
              ))}
            </div>
          )}
        </section>
      )}

      <section className="detail-section attestation-timesheet-section">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Timesedler</h3>
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
                    <span className="attestation-timesheet-hours-unit">{formatUnit(hours, 'time', 'timer')}</span>
                  </div>
                  {worksheet.sleptOnJob && <span className="attestation-timesheet-badge">Udlæg</span>}
                </li>
              );
            })}
          </ul>
        )}

        {(totalHoursLabel || totalOutlayLabel) && (
          <div className="attestation-timesheet-totals" aria-label="Timeseddel totaler">
            {totalHoursLabel}
            {totalOutlayLabel}
          </div>
        )}
      </section>

      {selectedClosureFlags.length > 0 && (
        <section className="detail-section attestation-control-section compact">
          <div className="section-header-row attestation-compact-header">
            <CheckCircle2 size={18} />
            <h3>Afslutning</h3>
          </div>
          <ul className="attestation-control-list compact">
            {selectedClosureFlags.map((flag) => (
              <li key={flag.id}>
                <span className="attestation-control-accent" aria-hidden="true" />
                <span>{flag.label}</span>
              </li>
            ))}
          </ul>
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
            <div className="attestation-irrelevant-block">
              <span className="attestation-irrelevant-label">Markeret irrelevant</span>
              <ul className="attestation-control-list compact">
                {irrelevantCategories.map((item) => (
                  <li key={item.id} className="attestation-control-list-item-muted">
                    <span className="attestation-control-accent muted" aria-hidden="true" />
                    <span>{item.installationType} · {capitalize(item.category)}</span>
                  </li>
                ))}
              </ul>
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

        <div className="attestation-confirm-card">
          <div className="attestation-confirm-card-header">
            <ShieldCheck size={20} className="attestation-confirm-card-icon" aria-hidden="true" />
            <div className="attestation-confirm-card-heading">
              <h3 className="attestation-confirm-card-title">Bekræft og indsend</h3>
              <p className="attestation-confirm-card-subtitle">
                Når du attesterer, registreres sagen som indsendt hos kontoret med den aktuelle bruger.
              </p>
            </div>
          </div>

          <label className={`attestation-confirm-row${confirmed || isInReview ? ' confirmed' : ''}${confirmationDisabled ? ' disabled' : ''}`}>
            <span className="attestation-confirm-copy">
              <span className="attestation-confirm-label">
                Jeg bekræfter, at sagen er gennemgået og klar til indsendelse
              </span>
              <span className="attestation-confirm-description">
                Attestering kan ikke fortrydes efter indsendelse.
              </span>
            </span>
            <input
              type="checkbox"
              checked={confirmed || isInReview}
              disabled={confirmationDisabled}
              onChange={(event) => onConfirmedChange(event.target.checked)}
            />
          </label>

          {isInReview ? (
            <div className="attestation-submitted-badge" role="status" aria-live="polite">
              <CheckCircle2 size={20} aria-hidden="true" />
              <div>
                <span className="attestation-submitted-badge-title">Sagen er attesteret</span>
                <span className="attestation-submitted-badge-subtitle">
                  Status er opdateret hos backend. Du kan ikke indsende sagen igen.
                </span>
              </div>
            </div>
          ) : (
            <div className="attestation-submit-row">
              <button
                type="button"
                className={confirmed ? 'btn btn-primary attestation-submit-button' : 'btn attestation-submit-button attestation-submit-button-locked'}
                onClick={handleSubmit}
                disabled={!confirmed || confirmationDisabled}
                title={!confirmed ? 'Bekræft først at sagen er gennemgået' : undefined}
                aria-disabled={!confirmed || confirmationDisabled}
              >
                {details.isSubmittingJob || isSavingDraft ? (
                  <Loader2 className="animate-spin" size={18} />
                ) : (
                  <ShieldCheck size={18} />
                )}
                <span>
                  {isSavingDraft
                    ? 'Gemmer...'
                    : details.isSubmittingJob
                      ? 'Indsender...'
                      : 'Attestér og indsend'}
                </span>
              </button>
            </div>
          )}
        </div>
      </section>
    </>
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
  return details.assignableUsers?.find((user) => user.id === userId)?.displayName
    ?? details.job?.assignedUsers.find((user) => user.id === userId)?.displayName
    ?? userId;
}

function formatContact(
  contactPerson: string | null | undefined,
  phone: string | null | undefined,
  email: string | null | undefined,
) {
  return [contactPerson, phone, email].filter(hasText).join(' · ');
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

function capitalize(value: string) {
  if (!value) return value;
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}
