import { useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Bolt, Check, ChevronLeft, ChevronRight, Loader2, ShieldCheck, Snowflake, X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useModalAccessibility } from '../../../components/common/useModalAccessibility';
import { useIsAdmin } from '../../../providers/permissions';
import { CustomerDetailsBlock, DestinationAddressBlock } from './JobDetailBlocks';
import {
  getDemoCompetencies,
  getDisciplineInstallationIds,
  getDisciplineLabel,
  getMissingDisciplines,
  isElectricalRefrigerationProfile,
  type ElectricalRefrigerationDiscipline,
} from '../electricalRefrigerationProfile';
import { useJobCreate } from '../hooks/useJobCreate';
import './ElectricalRefrigerationCreateDialog.css';

type Props = {
  isOpen: boolean;
  onClose: () => void;
};

type AssetDraft = {
  name: string;
  manufacturerModel: string;
  serialNumber: string;
};

const STEPS = ['Grundlag', 'Fagspor', 'Bemanding', 'Kontrol'] as const;

const TRACK_OPTIONS: Array<{
  id: string;
  label: string;
  description: string;
  disciplines: ElectricalRefrigerationDiscipline[];
}> = [
  { id: 'electrical', label: 'Kun EL', description: 'Elinstallation og verifikation', disciplines: ['electrical'] },
  { id: 'refrigeration', label: 'Kun KØL', description: 'Køl, F-gas og idriftsættelse', disciplines: ['refrigeration'] },
  { id: 'combined', label: 'EL + KØL', description: 'Ét samlet tværfagligt forløb', disciplines: ['electrical', 'refrigeration'] },
];

export function ElectricalRefrigerationCreateDialog({ isOpen, onClose }: Props) {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const [currentStep, setCurrentStep] = useState(0);
  const [disciplines, setDisciplines] = useState<ElectricalRefrigerationDiscipline[]>([]);
  const [asset, setAsset] = useState<AssetDraft>({ name: '', manufacturerModel: '', serialNumber: '' });
  const closeButtonRef = useRef<HTMLButtonElement>(null);

  const create = useJobCreate((jobIds) => {
    onClose();
    navigate(`/app/job/${jobIds[0]}`, { state: { from: '/app' } });
  });

  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open: isOpen,
    onClose: () => {
      if (!create.isSaving) onClose();
    },
    initialFocusRef: closeButtonRef,
  });

  const selectedUsers = useMemo(
    () => create.assignableUsers.filter((user) => create.assignedUserIds.includes(user.id)),
    [create.assignableUsers, create.assignedUserIds],
  );
  const missingDisciplines = useMemo(
    () => getMissingDisciplines(selectedUsers, disciplines),
    [selectedUsers, disciplines],
  );
  const selectedInstallationTypes = useMemo(() => (
    create.referenceData?.installationTypes.filter((type) => create.form.work.categoryIds.includes(type.id)) ?? []
  ), [create.form.work.categoryIds, create.referenceData]);
  const selectedWorkKind = create.referenceData?.workKinds.find(
    (kind) => kind.normalizedLabel === create.form.work.workKind,
  );

  const availableWorkKinds = useMemo(() => {
    const workKinds = create.referenceData?.workKinds ?? [];
    if (disciplines.length === 2) {
      return workKinds.filter((kind) => (
        kind.normalizedLabel === 'ElectricalAndRefrigeration'
        || kind.normalizedLabel === 'ServiceOther'
      ));
    }
    if (disciplines[0] === 'electrical') {
      return workKinds.filter((kind) => (
        kind.normalizedLabel.startsWith('Electrical')
        || kind.normalizedLabel === 'ServiceOther'
      ));
    }
    if (disciplines[0] === 'refrigeration') {
      return workKinds.filter((kind) => (
        kind.normalizedLabel.startsWith('Refrigeration')
        || kind.normalizedLabel === 'ServiceOther'
      ));
    }
    return [];
  }, [create.referenceData, disciplines]);

  const technicalObservationSnapshot = useMemo(() => {
    const competencySnapshot = selectedUsers.flatMap((user) => (
      getDemoCompetencies(user.id)
        .filter((competency) => disciplines.includes(competency.discipline))
        .map((competency) => `${user.displayName}: ${competency.label} (${competency.scope}, gyldig til ${competency.validUntil})`)
    ));

    return [
      asset.name ? `Anlæg: ${asset.name}` : null,
      asset.manufacturerModel ? `Fabrikat/model: ${asset.manufacturerModel}` : null,
      asset.serialNumber ? `Serienummer: ${asset.serialNumber}` : null,
      disciplines.length > 0 ? `Fagspor: ${disciplines.map(getDisciplineLabel).join(' + ')}` : null,
      competencySnapshot.length > 0 ? `Kompetencer: ${competencySnapshot.join('; ')}` : null,
    ].filter(Boolean).join('\n');
  }, [asset, disciplines, selectedUsers]);

  if (!isOpen) return null;

  const selectTrack = (nextDisciplines: ElectricalRefrigerationDiscipline[]) => {
    setDisciplines(nextDisciplines);
    create.updateWorkCategories(getDisciplineInstallationIds(create.referenceData, nextDisciplines));

    const defaultWorkKind = nextDisciplines.length === 2
      ? 'ElectricalAndRefrigeration'
      : nextDisciplines[0] === 'electrical'
        ? 'ElectricalInstallation'
        : 'RefrigerationCommissioning';
    create.updateWorkKind(defaultWorkKind);
  };

  const toggleEmployee = (userId: string) => {
    const next = create.assignedUserIds.includes(userId)
      ? create.assignedUserIds.filter((id) => id !== userId)
      : [...create.assignedUserIds, userId];
    create.updateAssignedUsers(next);
  };

  const canContinue = currentStep === 0
    ? create.canSave && asset.name.trim().length > 0 && create.form.taskDescription.trim().length > 0
    : currentStep === 1
      ? disciplines.length > 0 && create.form.work.workKind.length > 0
      : currentStep === 2
        ? create.assignedUserIds.length > 0 && missingDisciplines.length === 0
        : true;

  const goNext = () => {
    if (!canContinue) return;
    setCurrentStep((step) => Math.min(step + 1, STEPS.length - 1));
  };

  const submit = () => {
    if (!canContinue || missingDisciplines.length > 0) return;
    create.saveWithWork({ technicalObservations: technicalObservationSnapshot });
  };

  const content = (() => {
    if (create.isLoadingReferenceData) {
      return <div className="er-create-state"><Loader2 className="animate-spin" size={24} /> Henter EL/KØL-opsætning…</div>;
    }

    if (!isElectricalRefrigerationProfile(create.referenceData)) {
      return (
        <div id="er-create-profile-error" className="er-create-state er-create-state--error" role="alert">
          EL/KØL-opsætningen er ikke tilgængelig for denne filial.
        </div>
      );
    }

    if (currentStep === 0) {
      return (
        <div id="er-create-step-foundation" className="er-create-step">
          <div className="er-create-section-heading">
            <h3>Grundlag for opgaven</h3>
            <p>Vælg kunden og registrér det anlæg, arbejdet vedrører.</p>
          </div>
          <DestinationAddressBlock
            value={create.form.destinationAddress}
            zipCode={create.form.destinationZipCode}
            city={create.form.destinationCity}
            onChange={create.updateDestinationAddress}
            onZipCodeChange={create.updateDestinationZipCode}
            onCityChange={create.updateDestinationCity}
            error={create.fieldErrors.destinationAddress}
          />
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
            showEditCheckbox
            fieldErrors={create.fieldErrors}
          />
          <section className="detail-section er-asset-section">
            <div className="er-create-section-heading">
              <h3>Anlæg</h3>
              <p>Stamdata følger sagen og indgår i den tekniske dokumentation.</p>
            </div>
            <div className="er-field-grid">
              <label className="form-group er-field-grid--wide">
                <span className="form-label">Anlægsnavn<span className="required-asterisk">*</span></span>
                <input
                  id="er-asset-name"
                  className="form-input"
                  value={asset.name}
                  onChange={(event) => setAsset((current) => ({ ...current, name: event.target.value }))}
                  placeholder="Fx varmepumpe ved produktion 1"
                />
              </label>
              <label className="form-group">
                <span className="form-label">Fabrikat/model</span>
                <input
                  id="er-asset-model"
                  className="form-input"
                  value={asset.manufacturerModel}
                  onChange={(event) => setAsset((current) => ({ ...current, manufacturerModel: event.target.value }))}
                  placeholder="Fx Panasonic Aquarea"
                />
              </label>
              <label className="form-group">
                <span className="form-label">Serienummer</span>
                <input
                  id="er-asset-serial-number"
                  className="form-input"
                  value={asset.serialNumber}
                  onChange={(event) => setAsset((current) => ({ ...current, serialNumber: event.target.value }))}
                  placeholder="Serienummer"
                />
              </label>
              <label className="form-group er-field-grid--wide">
                <span className="form-label">Opgavebeskrivelse<span className="required-asterisk">*</span></span>
                <textarea
                  id="er-task-description"
                  className="form-textarea"
                  value={create.form.taskDescription}
                  onChange={(event) => create.updateTaskDescription(event.target.value)}
                  placeholder="Beskriv arbejdet, der skal udføres…"
                  rows={3}
                />
              </label>
            </div>
          </section>
        </div>
      );
    }

    if (currentStep === 1) {
      return (
        <div id="er-create-step-disciplines" className="er-create-step">
          <div className="er-create-section-heading">
            <h3>Vælg fagspor</h3>
            <p>Workslip aktiverer kun de kontroller og krav, der matcher sagen.</p>
          </div>
          <div className="er-track-grid">
            {TRACK_OPTIONS.map((option) => {
              const isSelected = option.disciplines.length === disciplines.length
                && option.disciplines.every((discipline) => disciplines.includes(discipline));
              return (
                <button
                  id={`er-track-${option.id}`}
                  key={option.id}
                  type="button"
                  className={`er-track-card${isSelected ? ' selected' : ''}`}
                  aria-pressed={isSelected}
                  onClick={() => selectTrack(option.disciplines)}
                >
                  <span className="er-track-card-icons" aria-hidden="true">
                    {option.disciplines.includes('electrical') && <Bolt size={22} />}
                    {option.disciplines.length === 2 && <span>+</span>}
                    {option.disciplines.includes('refrigeration') && <Snowflake size={22} />}
                  </span>
                  <strong>{option.label}</strong>
                  <span>{option.description}</span>
                  {isSelected && <Check className="er-track-check" size={18} aria-hidden="true" />}
                </button>
              );
            })}
          </div>

          {disciplines.length > 0 && (
            <section className="detail-section er-work-kind-section">
              <div className="er-create-section-heading">
                <h3>Opgavetype</h3>
                <p>Opgavetypen bruges til at vælge det rigtige dokumentationsforløb.</p>
              </div>
              <div className="er-work-kind-list">
                {availableWorkKinds.map((workKind) => {
                  const isSelected = create.form.work.workKind === workKind.normalizedLabel;
                  return (
                    <label
                      id={`er-work-kind-${workKind.normalizedLabel}`}
                      key={workKind.normalizedLabel}
                      className={`er-work-kind-option${isSelected ? ' selected' : ''}`}
                    >
                      <input
                        type="radio"
                        name="er-work-kind"
                        value={workKind.normalizedLabel}
                        checked={isSelected}
                        onChange={(event) => create.updateWorkKind(event.target.value)}
                      />
                      <span>{workKind.label}</span>
                    </label>
                  );
                })}
              </div>
            </section>
          )}
        </div>
      );
    }

    if (currentStep === 2) {
      return (
        <div id="er-create-step-staffing" className="er-create-step">
          <div className="er-create-section-heading">
            <h3>Bemanding og kompetencer</h3>
            <p>Vælg medarbejdere, der tilsammen dækker alle aktive fagspor.</p>
          </div>
          <div className="er-employee-list">
            {create.assignableUsers.map((user) => {
              const competencies = getDemoCompetencies(user.id);
              const relevantCompetencies = competencies.filter((competency) => disciplines.includes(competency.discipline));
              const isSelected = create.assignedUserIds.includes(user.id);
              return (
                <button
                  id={`er-employee-${user.id}`}
                  key={user.id}
                  type="button"
                  className={`er-employee-card${isSelected ? ' selected' : ''}`}
                  aria-pressed={isSelected}
                  onClick={() => toggleEmployee(user.id)}
                >
                  <span className="er-employee-selection" aria-hidden="true">{isSelected && <Check size={16} />}</span>
                  <span className="er-employee-copy">
                    <strong>{user.displayName}</strong>
                    {relevantCompetencies.length > 0 ? (
                      <span className="er-competency-list">
                        {relevantCompetencies.map((competency) => (
                          <span key={`${user.id}-${competency.discipline}`} className="er-competency-badge">
                            {competency.label} · gyldig til {competency.validUntil}
                          </span>
                        ))}
                      </span>
                    ) : (
                      <span className="er-competency-missing">Ingen registreret kompetence til det valgte fagspor</span>
                    )}
                  </span>
                </button>
              );
            })}
          </div>
          {missingDisciplines.length > 0 && (
            <div id="er-missing-competencies" className="er-competency-warning" role="status">
              <ShieldCheck size={18} aria-hidden="true" />
              <span>Der mangler kompetencedækning for {missingDisciplines.map(getDisciplineLabel).join(' og ')}.</span>
            </div>
          )}
        </div>
      );
    }

    const controlPointCount = selectedInstallationTypes.reduce((total, type) => (
      total + type.categories.reduce((categoryTotal, category) => categoryTotal + category.controlPoints.length, 0)
    ), 0);

    return (
      <div id="er-create-step-review" className="er-create-step">
        <div className="er-create-section-heading">
          <h3>Kontrollér opsætningen</h3>
          <p>Sagen oprettes med separate EL-/KØL-kontroller i det eksisterende Workslip-flow.</p>
        </div>
        <div className="er-review-grid">
          <section>
            <span>Opgave</span>
            <strong>{create.form.taskDescription}</strong>
            <small>{asset.name}{asset.manufacturerModel ? ` · ${asset.manufacturerModel}` : ''}</small>
          </section>
          <section>
            <span>Fagspor</span>
            <strong>{disciplines.map(getDisciplineLabel).join(' + ')}</strong>
            <small>{selectedWorkKind?.label}</small>
          </section>
          <section>
            <span>Bemanding</span>
            <strong>{selectedUsers.map((user) => user.displayName).join(' + ')}</strong>
            <small>Kompetencer valideret for sagen</small>
          </section>
          <section>
            <span>Dokumentationspakke</span>
            <strong>{controlPointCount} kontrolpunkter</strong>
            <small>{selectedInstallationTypes.map((type) => type.name).join(' + ')}</small>
          </section>
        </div>
        <div className="er-requirements-list">
          {selectedInstallationTypes.map((installationType) => (
            <div key={installationType.id} className="er-requirement-track">
              <strong>{installationType.name}</strong>
              <ul>
                {installationType.categories.map((category) => (
                  <li key={category.id}>{category.name} · {category.controlPoints.length} kontrolpunkter</li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </div>
    );
  })();

  return createPortal(
    <div className="er-create-backdrop">
      <div
        id="electrical-refrigeration-create-dialog"
        ref={dialogRef}
        className="er-create-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="er-create-title"
        tabIndex={-1}
      >
        <header className="er-create-header">
          <div>
            <span className="er-create-eyebrow">EL &amp; KØL</span>
            <h2 id="er-create-title">Opret ny opgave</h2>
          </div>
          <button
            id="er-create-close"
            ref={closeButtonRef}
            type="button"
            className="btn-icon"
            onClick={onClose}
            disabled={create.isSaving}
            aria-label="Luk oprettelse"
          >
            <X size={21} />
          </button>
        </header>

        <ol className="er-create-progress" aria-label="Oprettelsestrin">
          {STEPS.map((step, index) => (
            <li key={step} className={index === currentStep ? 'active' : index < currentStep ? 'complete' : ''}>
              <span>{index < currentStep ? <Check size={15} /> : index + 1}</span>
              <strong>{step}</strong>
            </li>
          ))}
        </ol>

        <div className="er-create-content">{content}</div>

        <footer className="er-create-actions">
          <button
            id="er-create-back"
            type="button"
            className="btn btn-secondary"
            onClick={() => setCurrentStep((step) => Math.max(0, step - 1))}
            disabled={currentStep === 0 || create.isSaving}
          >
            <ChevronLeft size={18} /> Tilbage
          </button>
          {currentStep < STEPS.length - 1 ? (
            <button
              id="er-create-next"
              type="button"
              className="btn btn-primary"
              onClick={goNext}
              disabled={!canContinue || create.isSaving}
            >
              Næste <ChevronRight size={18} />
            </button>
          ) : (
            <button
              id="er-create-submit"
              type="button"
              className="btn btn-primary"
              onClick={submit}
              disabled={!canContinue || create.isSaving}
            >
              {create.isSaving ? <Loader2 className="animate-spin" size={18} /> : <Check size={18} />}
              {create.isSaving ? 'Opretter…' : 'Opret sag'}
            </button>
          )}
        </footer>
      </div>
    </div>,
    document.body,
  );
}
