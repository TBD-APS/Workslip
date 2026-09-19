import type { LegalContent } from './types';

export const cookiesContent: LegalContent = {
  title: 'Cookie- og lagringspolitik',
  lastUpdated: '10. september 2026',
  sections: [
    {
      heading: 'Hvad er cookies',
      content: [
        'Workslip gemmer et kortlivet adgangstoken i localStorage og bruger en nødvendig, HttpOnly-beskyttet sessionscookie til automatisk og sikker fornyelse. Sessionscookiens indhold kan ikke læses af frontend-kode.',
        'sessionStorage bruges til midlertidige værdier som søgning, sortering, paginering og scrollposition. Temaindstilling gemmes i localStorage.',
      ],
    },
    {
      heading: 'Cookies vi bruger',
      content: [
        'Workslip bruger følgende lagringsteknologier:',
        '• nødvendig sessionscookie — roterende login-session med en absolut levetid på højst 14 dage',
        '• localStorage — kortlivet authToken, userEmail, tema og en kortvarig reauth-markør',
        '• sessionStorage — søgning, sortering, paginering og scrollpositioner',
      ],
    },
    {
      heading: 'Tredjeparts cookies',
      content: [
        'Når de er aktiveret i den konkrete production-deployment, bruges Vercel Analytics, Vercel Speed Insights og Microsoft Application Insights til analyse, fejl og ydeevne.',
        'Application Insights er konfigureret med cookie-brug deaktiveret og uden automatisk AJAX-, fetch-, exception- og route-tracking. Workslip sender dog selv tekniske hændelser som route, statuskode og correlation ID.',
        'Ved Microsoft Entra-login kan Microsofts egne login- og sikkerhedscookies bruges på Microsofts login-domæne. De kontrolleres af Microsoft.',
      ],
    },
    {
      heading: 'Sådan styrer du cookies',
      content: [
        'Du kan slette cookies, localStorage og sessionStorage via browserens webstedsdata eller logge ud. Logout tilbagekalder også den serverbaserede session.',
        'Blokering af lagring kan forhindre login, PWA-funktioner, tema, scrollpositioner eller andre dele af brugeroplevelsen i at fungere.',
      ],
    },
  ],
};
