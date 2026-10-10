import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Check, CircleAlert, Image as ImageIcon, ShieldCheck } from 'lucide-react';
import { useNavigate, useParams } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { formatDateLong, formatDateTime } from '../../../lib/formatDate';
import {
  createAuditorFinding,
  getAuditorImageBlob,
  getAuditorReport,
  updateAuditorFinding,
  type AuditorFinding,
  type AuditorImageInfo,
} from '../api';
import '../auditor.css';

const findingLabels: Record<AuditorFinding['category'], string> = {
  A: 'Afvigelse',
  An: 'Anmærkning',
  Anb: 'Anbefaling',
  IR: 'Ikke relevant',
};

const findingStatusLabels: Record<AuditorFinding['status'], string> = {
  Open: 'Åben',
  AwaitingEvidence: 'Afventer dokumentation',
  ReadyForVerification: 'Klar til verificering',
  Closed: 'Lukket',
};

const eventLabels: Record<string, string> = {
  ReportOpened: 'Rapport åbnet af auditor',
  FindingCreated: 'Fund oprettet',
  FindingUpdated: 'Fund opdateret',
  FindingClosed: 'Fund verificeret og lukket',
  CompanyEvidenceAdded: 'Virksomheden tilføjede dokumentation',
};

function AuditorImage({ organizationId, jobId, image }: { organizationId: string; jobId: string; image: AuditorImageInfo }) {
  const imageQuery = useQuery({
    queryKey: ['auditor-image', organizationId, jobId, image.id],
    queryFn: () => getAuditorImageBlob(organizationId, jobId, image.id),
  });
  const [objectUrl, setObjectUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!imageQuery.data) {
      setObjectUrl(null);
      return undefined;
    }
    const nextUrl = URL.createObjectURL(imageQuery.data);
    setObjectUrl(nextUrl);
    return () => URL.revokeObjectURL(nextUrl);
  }, [imageQuery.data]);

  if (imageQuery.isLoading) {
    return <div className="auditor-empty">Henter billede…</div>;
  }
  if (imageQuery.isError || !objectUrl) {
    return <div className="auditor-empty">Billedet kunne ikke hentes.</div>;
  }

  return <img src={objectUrl} alt={`Dokumentationsbillede fra ${formatDateTime(image.createdAt) ?? 'sagen'}`} />;
}

export const AuditorReportDetail = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { organizationId = '', jobId = '' } = useParams();
  const [category, setCategory] = useState<AuditorFinding['category']>('An');
  const [description, setDescription] = useState('');
  const [reference, setReference] = useState('');
  const [dueAt, setDueAt] = useState('');
  const [formError, setFormError] = useState<string | null>(null);

  const reportQueryKey = useMemo(() => ['auditor-report', organizationId, jobId], [organizationId, jobId]);
  const reportQuery = useQuery({
    queryKey: reportQueryKey,
    queryFn: () => getAuditorReport(organizationId, jobId),
    enabled: Boolean(organizationId && jobId),
  });

  const createFindingMutation = useMutation({
    mutationFn: () => createAuditorFinding(organizationId, jobId, {
      category,
      description: description.trim(),
      reference: reference.trim() || null,
      dueAt: dueAt ? new Date(dueAt).toISOString() : null,
    }),
    onSuccess: async () => {
      setDescription('');
      setReference('');
      setDueAt('');
      setFormError(null);
      await queryClient.invalidateQueries({ queryKey: reportQueryKey });
      await queryClient.invalidateQueries({ queryKey: ['auditor-organizations'] });
    },
    onError: () => setFormError('Fundet kunne ikke gemmes. Kontroller oplysningerne og prøv igen.'),
  });

  const updateFindingMutation = useMutation({
    mutationFn: ({ finding, status }: { finding: AuditorFinding; status: AuditorFinding['status'] }) =>
      updateAuditorFinding(organizationId, jobId, finding.id, {
        status,
        description: null,
        dueAt: finding.dueAt,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: reportQueryKey });
      await queryClient.invalidateQueries({ queryKey: ['auditor-organizations'] });
    },
  });

  if (!organizationId || !jobId) {
    return <div className="page-container"><ErrorState message="Rapporten kunne ikke identificeres." /></div>;
  }

  if (reportQuery.isLoading) {
    return <div className="page-container"><div className="auditor-empty">Henter dokumentation…</div></div>;
  }

  if (reportQuery.isError || !reportQuery.data) {
    return (
      <div className="page-container">
        <ErrorState message="Rapporten kunne ikke hentes, eller du har ikke længere adgang til organisationen." onRetry={() => void reportQuery.refetch()} />
      </div>
    );
  }

  const report = reportQuery.data;
  const totalHours = report.worksheets.reduce((sum, worksheet) => sum + Number(worksheet.hoursWorked), 0);
  const installationNames = report.installations.map((installation) => installation.name).join(', ');

  return (
    <div id="auditor-report-detail" className="page-container auditor-page auditor-detail" data-testid="auditor-report-detail">
      <button type="button" className="auditor-back-link" onClick={() => navigate('/app/auditor')}>
        <ArrowLeft size={16} aria-hidden="true" /> Tilbage til audit
      </button>

      <header className="auditor-detail__hero">
        <div>
          <p className="auditor-page__eyebrow">{report.organizationName} · CVR {report.organizationCvr}</p>
          <h1>SAG-{(report.reportNumber || report.id.slice(0, 4)).toUpperCase()}</h1>
          <p className="auditor-page__subtitle">
            {report.customerName || 'Ukendt kunde'} · {report.destinationAddress || report.customerAddress || 'Adresse ikke angivet'}
          </p>
        </div>
        <span className="auditor-count">{report.findings.filter((finding) => finding.status !== 'Closed').length} åbne fund</span>
      </header>

      <div className="auditor-detail__grid">
        <section className="auditor-section" aria-labelledby="auditor-trace-heading">
          <p className="auditor-section__eyebrow">Dokumentationskæde</p>
          <h2 id="auditor-trace-heading">Sporbarhed</h2>
          <dl className="auditor-kv">
            <dt>Anlægstype</dt><dd>{installationNames || '—'}</dd>
            <dt>Rapportdato</dt><dd>{formatDateLong(report.reportDate) ?? '—'}</dd>
            <dt>Indsendt</dt><dd>{formatDateTime(report.submittedAt) ?? '—'}</dd>
            <dt>Indsendt af</dt><dd>{report.submittedBy || '—'}</dd>
            <dt>Udført af</dt><dd>{report.assignedUsers.map((person) => person.displayName).join(', ') || '—'}</dd>
            <dt>Registrerede timer</dt><dd>{totalHours ? `${totalHours} t` : '—'}</dd>
          </dl>
          <p className="auditor-muted">
            Workslip har endnu ikke et særskilt felt for kontrollant. Derfor vises indsenderen ikke som “kontrolleret af”.
          </p>
        </section>

        <section className="auditor-section" aria-labelledby="auditor-task-heading">
          <p className="auditor-section__eyebrow">Opgaven</p>
          <h2 id="auditor-task-heading">Arbejde og observationer</h2>
          <dl className="auditor-kv">
            <dt>Opgavetype</dt><dd>{report.jobType || '—'}</dd>
            <dt>Opgavebeskrivelse</dt><dd>{report.taskDescription || '—'}</dd>
            <dt>Kundeobservationer</dt><dd>{report.customerObservations || '—'}</dd>
            <dt>Tekniske observationer</dt><dd>{report.technicalObservations || '—'}</dd>
            <dt>Bemærkninger</dt><dd>{report.remarks || '—'}</dd>
          </dl>
        </section>
      </div>

      <section className="auditor-section" aria-labelledby="auditor-controls-heading">
        <div className="auditor-section__header">
          <div>
            <p className="auditor-section__eyebrow">Slutkontrol / verifikation</p>
            <h2 id="auditor-controls-heading">Kontrolpunkter</h2>
          </div>
          <span className="auditor-count">{report.installations.length} anlæg</span>
        </div>
        {report.installations.length === 0 ? (
          <div className="auditor-empty">Ingen Vand/Afløb-kontrolpunkter er registreret.</div>
        ) : report.installations.map((installation) => (
          <article key={installation.id} className="auditor-control-category">
            <h3>{installation.name}</h3>
            {installation.categories.map((controlCategory) => (
              <div key={controlCategory.id} className="auditor-control-category">
                <div className="auditor-section__header">
                  <strong>{controlCategory.name}</strong>
                  {controlCategory.isIrrelevant ? <span className="auditor-count">Ikke relevant</span> : null}
                </div>
                <ul className="auditor-control-list">
                  {controlCategory.controlPoints.map((point) => (
                    <li key={point.id} className="auditor-control-point">
                      <span>{point.name}{point.isRequired ? ' *' : ''}</span>
                      <span className="auditor-muted">
                        {point.isChecked ? <><Check size={15} aria-hidden="true" /> Udført</> : 'Ikke markeret'}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </article>
        ))}
      </section>

      <div className="auditor-detail__grid">
        <section className="auditor-section" aria-labelledby="auditor-hours-heading">
          <p className="auditor-section__eyebrow">Udførelse</p>
          <h2 id="auditor-hours-heading">Timer og medarbejdere</h2>
          {report.worksheets.length === 0 ? (
            <div className="auditor-empty">Ingen timer registreret.</div>
          ) : (
            <ul className="auditor-timeline">
              {report.worksheets.map((worksheet) => (
                <li key={worksheet.id}>
                  <span>{worksheet.userName}</span>
                  <span className="auditor-muted">{formatDateLong(worksheet.workDate) ?? '—'} · {worksheet.hoursWorked} t</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="auditor-section" aria-labelledby="auditor-images-heading">
          <div className="auditor-section__header">
            <div>
              <p className="auditor-section__eyebrow">Bilag</p>
              <h2 id="auditor-images-heading">Billeder</h2>
            </div>
            <ImageIcon size={20} aria-hidden="true" />
          </div>
          {report.images.length === 0 ? (
            <div className="auditor-empty">Ingen billeder på sagen.</div>
          ) : (
            <div className="auditor-image-grid">
              {report.images.map((image) => (
                <AuditorImage key={image.id} organizationId={organizationId} jobId={jobId} image={image} />
              ))}
            </div>
          )}
        </section>
      </div>

      <section className="auditor-section" aria-labelledby="auditor-findings-heading">
        <div className="auditor-section__header">
          <div>
            <p className="auditor-section__eyebrow">Auditorens vurdering</p>
            <h2 id="auditor-findings-heading">Fund og opfølgning</h2>
          </div>
          <ShieldCheck size={20} aria-hidden="true" />
        </div>

        {report.findings.length === 0 ? (
          <div className="auditor-empty">Der er endnu ikke registreret auditor-fund på sagen.</div>
        ) : (
          <div className="auditor-finding-list" data-testid="auditor-finding-list">
            {report.findings.map((finding) => (
              <article key={finding.id} id={`auditor-finding-${finding.id}`} className="auditor-finding" data-testid={`auditor-finding-${finding.id}`}>
                <div className="auditor-finding__header">
                  <strong>{findingLabels[finding.category]} ({finding.category})</strong>
                  <span className="auditor-count">{findingStatusLabels[finding.status]}</span>
                </div>
                <p>{finding.description}</p>
                {finding.reference ? <p className="auditor-muted">Reference: {finding.reference}</p> : null}
                <p className="auditor-muted">
                  Oprettet {formatDateTime(finding.createdAt) ?? '—'} af {finding.createdBy || 'auditor'}
                  {finding.dueAt ? ` · Frist ${formatDateLong(finding.dueAt) ?? '—'}` : ''}
                </p>
                {finding.companyEvidence ? (
                  <div className="auditor-control-category">
                    <strong>Virksomhedens dokumentation</strong>
                    <p>{finding.companyEvidence}</p>
                    <p className="auditor-muted">
                      {finding.companyEvidenceBy || 'Virksomheden'} · {formatDateTime(finding.companyEvidenceAt) ?? '—'}
                    </p>
                  </div>
                ) : null}
                {finding.status === 'Closed' ? (
                  <p className="auditor-muted">Verificeret af {finding.verifiedBy || 'auditor'} · {formatDateTime(finding.verifiedAt) ?? '—'}</p>
                ) : (
                  <div className="auditor-detail__actions">
                    <button
                      type="button"
                      className="btn btn-secondary"
                      disabled={updateFindingMutation.isPending}
                      onClick={() => updateFindingMutation.mutate({ finding, status: 'AwaitingEvidence' })}
                    >
                      Afventer dokumentation
                    </button>
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={updateFindingMutation.isPending}
                      onClick={() => updateFindingMutation.mutate({ finding, status: 'Closed' })}
                    >
                      Verificér og luk
                    </button>
                  </div>
                )}
              </article>
            ))}
          </div>
        )}

        <form
          id="auditor-finding-form"
          className="auditor-finding-form"
          onSubmit={(event) => {
            event.preventDefault();
            if (!description.trim()) {
              setFormError('Skriv en beskrivelse af fundet.');
              return;
            }
            setFormError(null);
            createFindingMutation.mutate();
          }}
          data-testid="auditor-finding-form"
        >
          <div className="auditor-finding-form__row">
            <label>
              Kategori
              <select value={category} onChange={(event) => setCategory(event.target.value as AuditorFinding['category'])}>
                <option value="A">Afvigelse (A)</option>
                <option value="An">Anmærkning (An)</option>
                <option value="Anb">Anbefaling (Anb)</option>
                <option value="IR">Ikke relevant (IR)</option>
              </select>
            </label>
            <label>
              Frist
              <input type="datetime-local" value={dueAt} onChange={(event) => setDueAt(event.target.value)} />
            </label>
          </div>
          <label>
            Reference til kontrolpunkt eller dokument
            <input id="auditor-finding-reference" value={reference} maxLength={500} onChange={(event) => setReference(event.target.value)} placeholder="Valgfri reference" />
          </label>
          <label>
            Beskrivelse
            <textarea id="auditor-finding-description" value={description} maxLength={4000} onChange={(event) => setDescription(event.target.value)} placeholder="Beskriv det konkrete fund og hvad der skal følges op på." />
          </label>
          {formError ? <p className="auditor-muted" role="alert"><CircleAlert size={15} aria-hidden="true" /> {formError}</p> : null}
          <div className="auditor-detail__actions">
            <button id="auditor-finding-submit" type="submit" className="btn btn-primary" disabled={createFindingMutation.isPending}>
              {createFindingMutation.isPending ? 'Gemmer…' : 'Opret fund'}
            </button>
          </div>
        </form>
      </section>

      <section className="auditor-section" aria-labelledby="auditor-events-heading">
        <p className="auditor-section__eyebrow">Sporbarhed</p>
        <h2 id="auditor-events-heading">Auditor-hændelser</h2>
        {report.auditEvents.length === 0 ? (
          <div className="auditor-empty">Ingen tidligere auditor-hændelser på sagen.</div>
        ) : (
          <ul className="auditor-timeline" data-testid="auditor-event-timeline">
            {report.auditEvents.map((event) => (
              <li key={event.id}>
                <span>{eventLabels[event.eventType] ?? event.eventType}</span>
                <span className="auditor-muted">{event.actorName || 'Ukendt bruger'} · {formatDateTime(event.createdAt) ?? '—'}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
};
