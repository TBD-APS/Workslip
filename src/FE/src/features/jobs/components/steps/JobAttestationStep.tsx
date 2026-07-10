import { AlertCircle, CheckCircle2, FileCheck2, ShieldCheck } from 'lucide-react';
import { JobStatus } from '../../../../api/generated/models/jobStatus';
import type { useJobDetails } from '../../hooks/useJobDetails';
import { formatNumber, formatUnit, parseNullableNumber, capitalize } from '../../../../lib/formatUtils';
import { formatDateLong } from '../../../../lib/formatDate';

type JobDetailsState = ReturnType<typeof useJobDetails>;

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
    { label: 'Adresse (destination)', value: job.destinationAddress },
    { label: 'Kunde', value: job.customerSnapshot.name },
    { label: 'Adresse', value: job.customerSnapshot.address },
    { label: 'Kontakt', value: formatContact(job.customerSnapshot.contactPerson, job.customerSnapshot.phone, job.customerSnapshot.email) },
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

            {selectedClosureFlags.length > 0 && (
        <section className="detail-section attestation-control-section compact">
          <div className="section-header-row attestation-compact-header">
            <CheckCircle2 size={18} />
            <h3>Status</h3>
          </div>
          <ul className="attestation-control-list compact">
            {selectedClosureFlags.map((flag) => (
              <li key={flag.id}>
                <span>{flag.label}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="detail-section worksheet-list-section">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Timesedler</h3>
        </div>

        {sortedWorksheets.length === 0 ? (
          <p className="empty-state-text">Ingen timesedler registreret.</p>
        ) : (
          <ul className="worksheet-list worksheet-list--detail">
            {sortedWorksheets.map((worksheet) => {
              const hours = parseNullableNumber(worksheet.hoursWorked);
              const userName = getUserName(worksheet.userId, details);
              return (
                <li key={worksheet.id} className="worksheet-list-item worksheet-list-item--detail">
                  <div className="worksheet-list-item-main worksheet-list-item-main--detail">
                    <span className="worksheet-list-item-title" title={userName}>{userName}</span>
                    <span className="worksheet-list-item-subtitle worksheet-list-item-subtitle--detail">{formatDateLong(worksheet.workDate) ?? ''}</span>
                  </div>

                  <div className="worksheet-list-item-meta">
                    <div className="worksheet-list-item-badge">
                      <strong>{formatNumber(hours)}</strong>
                      <span>{formatUnit(hours, 'time', 'timer')}</span>
                    </div>
                    {worksheet.sleptOnJob && <span className="worksheet-list-item-tag">Udlæg</span>}
                  </div>
                </li>
              );
            })}
          </ul>
        )}

        {(totalHoursLabel || totalOutlayLabel) && (
          <div className="worksheet-list-totals" aria-label="Timeseddel totaler">
            {totalHoursLabel}
            {totalOutlayLabel}
          </div>
        )}
      </section>

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
                  <span>{controlPoint.installationType} · {capitalize(controlPoint.category)} · {controlPoint.name}</span>
                </li>
              ))}
            </ul>
          )}

          {irrelevantCategories.length > 0 && (
            <div className="attestation-irrelevant-block">
              <span className="attestation-irrelevant-label">Irrelevant</span>
              <ul className="attestation-control-list compact">
                {irrelevantCategories.map((item) => (
                  <li key={item.id}>
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
                <ShieldCheck size={18} />
                <span>Attestér og indsend</span>
              </button>
            </div>
          )}
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

function formatWorkKind(workKind: { label?: string | null; customWorkKind?: string | null } | null) {
  if (!workKind) return '';
  return workKind.customWorkKind || workKind.label || '';
}
