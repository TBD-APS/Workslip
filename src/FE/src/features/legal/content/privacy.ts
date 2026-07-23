import type { LegalContent } from './types';

export const privacyContent: LegalContent = {
  title: 'Privatlivspolitik',
  lastUpdated: '23. juli 2026',
  sections: [
    {
      heading: 'Dataansvarlig',
      content: [
        'Den organisation, der inviterer dig og administrerer din konto, bestemmer normalt formål og hjælpemidler for arbejdsdata og fungerer typisk som dataansvarlig. Workslip leverer i den behandling platformen som databehandler efter aftale.',
        'Workslip kan samtidig være selvstændigt dataansvarlig for oplysninger, der er nødvendige for drift, sikkerhed, support, fakturering og dokumentation. Den konkrete rolle og kontaktperson fremgår af organisationens aftale med Workslip.',
      ],
    },
    {
      heading: 'Hvilke data vi indsamler',
      content: [
        'Vi indsamler følgende personoplysninger:',
        '• Navn og e-mailadresse',
        '• Telefonnummer (valgfrit)',
        '• Microsoft Entra ID-oplysninger og bruger-ID (til login)',
        '• Oplysninger om arbejdsopgaver, kunder og timesedler',
        '• Tekniske oplysninger, request- og correlation-ID, fejl, sikkerhedshændelser og auditlogs',
      ],
    },
    {
      heading: 'Formålet med databehandlingen',
      content: [
        'Vi behandler dine data for at:',
        '• Levere og forbedre Workslip-tjenesten',
        '• Autentificere og administrere din adgang',
        '• Give dig adgang til arbejdsopgaver og kundeoplysninger',
        '• Sikre sporbarhed gennem auditlogs',
      ],
    },
    {
      heading: 'Grundlaget for behandlingen (GDPR)',
      content: [
        'Vi behandler dine data på baggrund af:',
        '• Udførelse af kontrakten mellem dig og din arbejdsgiver',
        '• Vores legitime interesse i at levere tjenesten',
        '• En konkret retlig forpligtelse eller legitim interesse, hvor det er relevant. Samtykke bruges kun, hvor det faktisk er indhentet og nødvendigt.',
      ],
    },
    {
      heading: 'Deling af data med tredjeparter',
      content: [
        'Vi deler dine data med følgende tredjeparter:',
        '• Microsoft Entra ID — til Single Sign-On',
        '• Vercel — hosting, Analytics og Speed Insights, hvis aktiveret',
        '• Microsoft Azure — backend hosting',
        '• Microsoft Application Insights — brugerhandlinger, API-afhængigheder, fejl og ydeevne, hvis aktiveret',
        'Vi sælger ikke personoplysninger. Leverandørernes roller, datalokationer og eventuelle overførsler uden for EU/EØS skal fremgå af de relevante aftaler; vi lover ikke EU/EØS-lagring uden dokumentation.',
      ],
    },
    {
      heading: 'Opbevaring og sletning',
      content: [
        'Opbevaringsperioden afhænger af datatypen, organisationens instruktioner, aftalen med Workslip og eventuelle lovkrav. En deaktiveret konto betyder derfor ikke nødvendigvis, at historiske arbejds- eller auditoplysninger slettes med det samme.',
        'Vi sletter eller anonymiserer oplysninger, når formålet og eventuelle dokumentations- eller lovkrav ikke længere kræver dem.',
      ],
    },
    {
      heading: 'Dine rettigheder',
      content: [
        'Ifølge GDPR har du ret til:',
        '• Indsigtsret — se hvilke data vi har om dig',
        '• Berigtigelse — få rettet forkerte data',
        '• Sletning — få slettet dine data',
        '• Begrænsning — begrænse behandlingen af dine data',
        '• Dataportabilitet — få dine data i et struktureret format',
        '• Indsigelse — gøre indsigelse mod behandlingen',
      ],
    },
    {
      heading: 'Kontaktinformation',
      content: [
        'Hvis behandlingen sker på vegne af din organisation, skal rettighedsanmodninger normalt sendes til organisationen som dataansvarlig. Du kan klage til Datatilsynet, hvis du mener, at dine oplysninger behandles i strid med reglerne.',
        'Politikken skal suppleres med Workslips juridiske virksomhedsnavn, adresse, e-mailadresse og eventuel databeskyttelsesrådgiver, før den anvendes som formel GDPR-oplysning.',
      ],
    },
  ],
};
