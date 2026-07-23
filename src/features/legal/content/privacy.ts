import { LegalContent } from './types';

export const privacyContent: LegalContent = {
  title: 'Privatlivspolitik',
  lastUpdated: '23. juli 2026',
  sections: [
    {
      heading: 'Dataansvarlig',
      content: [
        'Workslip er dataansvarlig for behandling af dine personoplysninger.',
      ],
    },
    {
      heading: 'Hvilke data vi indsamler',
      content: [
        'Vi indsamler følgende personoplysninger:',
        '• Navn og e-mailadresse',
        '• Telefonnummer (valgfrit)',
        '• Microsoft Entra ID (til Single Sign-On)',
        '• Oplysninger om arbejdsopgaver, kunder og timesedler',
        '• Brugerlogfiler og auditlogs',
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
        '• Dit samtykke (hvor det er relevant)',
      ],
    },
    {
      heading: 'Deling af data med tredjeparter',
      content: [
        'Vi deler dine data med følgende tredjeparter:',
        '• Microsoft Entra ID — til Single Sign-On',
        '• Vercel — hosting og analyse',
        '• Microsoft Azure — backend hosting',
        '• Sentry — fejlrapportering',
        'Vi sælger ikke dine data til tredjeparter.',
      ],
    },
    {
      heading: 'Opbevaring og sletning',
      content: [
        'Dine data opbevares, så længe din konto er aktiv, eller som påkrævet ved lov.',
        'Du kan anmode om sletning af dine data ved at kontakte os.',
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
        'Hvis du har spørgsmål til vores privatlivspolitik eller ønsker at udøve dine rettigheder, bedes du kontakte os.',
      ],
    },
  ],
};
