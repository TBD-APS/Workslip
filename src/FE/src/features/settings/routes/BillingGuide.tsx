import {
  AlertTriangle,
  ArrowLeft,
  BadgeCheck,
  CalendarClock,
  CreditCard,
  FileText,
  Gauge,
  RefreshCcw,
  ShieldCheck,
  Users,
  WalletCards,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import './BillingGuide.css';

const guideSteps = [
  {
    icon: WalletCards,
    title: 'Se abonnementet',
    description:
      'Når selvbetjent fakturering er aktiveret, kan du se virksomhedens aktuelle plan, faktureringsperiode og hvilke Workslip-funktioner planen giver adgang til.',
  },
  {
    icon: Gauge,
    title: 'Følg forbruget',
    description:
      'Billing-overblikket viser forbrug med tydelige enheder og seneste opdatering. Manglende eller forsinkede målinger vises som ukendte — aldrig som nul.',
  },
  {
    icon: CreditCard,
    title: 'Administrer betaling sikkert',
    description:
      'Når betalingsoplysninger skal ændres, åbner Workslip et sikkert betalingsflow. Kort- og bankoplysninger gemmes ikke direkte i Workslip.',
  },
  {
    icon: FileText,
    title: 'Find fakturaer og kvitteringer',
    description:
      'Når billing er aktiveret, samles fakturaer og betalingsstatus ét sted, så Admin kan følge virksomhedens historik uden at lede i mails.',
  },
  {
    icon: CalendarClock,
    title: 'Skift eller opsig planen',
    description:
      'Planændringer viser, hvornår ændringen træder i kraft. En planlagt opsigelse fjerner ikke adgang før den aftalte slutdato.',
  },
  {
    icon: RefreshCcw,
    title: 'Kom videre efter en fejlet betaling',
    description:
      'Hvis en betaling fejler, bliver den først vist som en betalings- og recovery-status. Workslip lukker ikke automatisk adgangen på grund af en enkelt fejl eller en utilgængelig betalingsudbyder.',
  },
] as const;

const statusItems = [
  {
    label: 'Aktiv',
    tone: 'positive',
    text: 'Abonnementet kører normalt, og de tilknyttede funktioner er tilgængelige.',
  },
  {
    label: 'Kræver handling',
    tone: 'warning',
    text: 'Der er fx en betalingsfejl eller manglende betalingsoplysning. Admin får en tydelig næste handling.',
  },
  {
    label: 'Planlagt ændring',
    tone: 'neutral',
    text: 'En opgradering, nedgradering eller opsigelse er registreret og træder i kraft på en bestemt dato.',
  },
  {
    label: 'Begrænset',
    tone: 'critical',
    text: 'Adgangen er begrænset efter den gældende betalingspolitik. Status og recovery-mulighed skal altid være synlig for Admin.',
  },
] as const;

const faqs = [
  {
    question: 'Hvem kan ændre abonnement og betaling?',
    answer:
      'Når selvbetjent fakturering er aktiv, er det kun brugere med Admin-rettigheder, der kan ændre virksomhedens abonnement, betalingsopsætning og faktureringsdata.',
  },
  {
    question: 'Gemmer Workslip mine kortoplysninger?',
    answer:
      'Nej. Betalingsfølsomme oplysninger håndteres gennem et sikkert betalingsflow. Workslip viser kun ufølsom status og de oplysninger, der er nødvendige for at administrere abonnementet.',
  },
  {
    question: 'Hvad sker der, hvis en betaling fejler?',
    answer:
      'En betalingsfejl vises som en recovery-status med næste handling. En enkelt fejlet betaling eller midlertidig fejl hos betalingsudbyderen må ikke i sig selv deaktivere virksomheden.',
  },
  {
    question: 'Mister vi vores data, hvis abonnementet opsiges?',
    answer:
      'Nej, ikke som en direkte følge af selve betalingsopsigelsen. Abonnement og datalivscyklus er separate processer. Eventuel sletning eller eksport følger de aftalte regler for virksomhedens data.',
  },
  {
    question: 'Kan Workslip fakturere efter forbrug?',
    answer:
      'Workslip kan måle autoritativt forbrug som aktive pladser og datamængde. Kun målinger, der udtrykkeligt er knyttet til en plan eller prisregel, kan blive fakturerbare.',
  },
] as const;

export const BillingGuide = () => {
  const navigate = useNavigate();

  return (
    <div className="page-container billing-guide-page">
      <button
        type="button"
        className="btn-icon billing-guide-back"
        onClick={() => navigate('/app/settings')}
        aria-label="Tilbage til indstillinger"
      >
        <ArrowLeft size={20} aria-hidden="true" />
      </button>

      <header className="billing-guide-hero">
        <div className="billing-guide-hero-icon" aria-hidden="true">
          <WalletCards size={24} />
        </div>
        <div>
          <p className="billing-guide-eyebrow">Admin-guide</p>
          <h2>Betaling & abonnement</h2>
          <p className="subtitle">
            Sådan fungerer abonnement, forbrug, fakturaer og betaling i Workslip.
          </p>
        </div>
      </header>

      <div className="billing-guide-note" role="note">
        <BadgeCheck size={18} aria-hidden="true" />
        <div>
          <strong>Selvbetjent fakturering aktiveres trinvis</strong>
          <p>
            Guiden beskriver det samlede Workslip-flow. De konkrete betalingshandlinger bliver synlige, når selvbetjent fakturering er aktiveret for din organisation.
          </p>
        </div>
      </div>

      <section className="billing-guide-section" aria-labelledby="billing-guide-flow-title">
        <div className="billing-guide-section-heading">
          <h3 id="billing-guide-flow-title">Sådan hænger det sammen</h3>
          <p>Du skal ikke kende betalingsudbyderen for at administrere Workslip.</p>
        </div>

        <div className="billing-guide-step-list">
          {guideSteps.map((step, index) => {
            const Icon = step.icon;
            return (
              <article key={step.title} className="billing-guide-step">
                <div className="billing-guide-step-index" aria-hidden="true">{index + 1}</div>
                <div className="billing-guide-step-icon" aria-hidden="true">
                  <Icon size={19} />
                </div>
                <div className="billing-guide-step-copy">
                  <h4>{step.title}</h4>
                  <p>{step.description}</p>
                </div>
              </article>
            );
          })}
        </div>
      </section>

      <section className="billing-guide-section" aria-labelledby="billing-guide-usage-title">
        <div className="billing-guide-section-heading">
          <h3 id="billing-guide-usage-title">Hvad kan tælle som forbrug?</h3>
          <p>Workslip skelner mellem almindelig produktaktivitet og målinger, der kan bruges til abonnementet.</p>
        </div>

        <div className="billing-guide-usage-grid">
          <article className="billing-guide-info-card">
            <Users size={19} aria-hidden="true" />
            <div>
              <strong>Aktive pladser</strong>
              <p>Antal aktive brugere i organisationen.</p>
            </div>
          </article>
          <article className="billing-guide-info-card">
            <Gauge size={19} aria-hidden="true" />
            <div>
              <strong>Datamængde</strong>
              <p>Fx Docs-storage og andre tydeligt definerede datamålinger.</p>
            </div>
          </article>
          <article className="billing-guide-info-card">
            <ShieldCheck size={19} aria-hidden="true" />
            <div>
              <strong>Autoritative tal</strong>
              <p>Fakturering bygger på backend-data — ikke klik, browsertelemetry eller estimerede events.</p>
            </div>
          </article>
        </div>
      </section>

      <section className="billing-guide-section" aria-labelledby="billing-guide-status-title">
        <div className="billing-guide-section-heading">
          <h3 id="billing-guide-status-title">Betalingsstatus</h3>
          <p>Status skal fortælle både hvad der er sket, og hvad Admin kan gøre bagefter.</p>
        </div>

        <div className="billing-guide-status-list">
          {statusItems.map((item) => (
            <article key={item.label} className="billing-guide-status-item">
              <span className={`billing-guide-status-dot billing-guide-status-dot--${item.tone}`} aria-hidden="true" />
              <div>
                <strong>{item.label}</strong>
                <p>{item.text}</p>
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="billing-guide-section" aria-labelledby="billing-guide-security-title">
        <div className="billing-guide-security-card">
          <ShieldCheck size={22} aria-hidden="true" />
          <div>
            <h3 id="billing-guide-security-title">Betalingsoplysninger bliver hos betalingsflowet</h3>
            <p>
              Workslip opbevarer ikke rå kortnumre, CVC eller bankoplysninger. Når en betalingsmetode skal oprettes eller ændres, håndteres de følsomme oplysninger i et sikkert, eksternt betalingsflow, mens Workslip kun modtager den nødvendige status tilbage.
            </p>
          </div>
        </div>
      </section>

      <section className="billing-guide-section" aria-labelledby="billing-guide-faq-title">
        <div className="billing-guide-section-heading">
          <h3 id="billing-guide-faq-title">Ofte stillede spørgsmål</h3>
        </div>

        <div className="billing-guide-faq-list">
          {faqs.map((faq) => (
            <details key={faq.question} className="billing-guide-faq">
              <summary>{faq.question}</summary>
              <p>{faq.answer}</p>
            </details>
          ))}
        </div>
      </section>

      <div className="billing-guide-support" role="note">
        <AlertTriangle size={18} aria-hidden="true" />
        <div>
          <strong>Ser noget forkert ud?</strong>
          <p>
            Undlad at prøve den samme betaling eller planændring mange gange. Workslip skal kunne genkende gentagne forsøg og bevare den senest kendte abonnementstilstand, mens fejlen undersøges.
          </p>
        </div>
      </div>
    </div>
  );
};
