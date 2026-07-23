import type { LegalContent } from './types';

export const cookiesContent: LegalContent = {
  title: 'Cookie- og lagringspolitik',
  lastUpdated: '23. juli 2026',
  sections: [
    {
      heading: 'Hvad er cookies',
      content: [
        'Workslip-frontend’en bruger ikke cookies til at gemme login. Login-tokenet gemmes i localStorage. Det er ikke en cookie og har ikke HttpOnly-beskyttelse.',
        'sessionStorage bruges til midlertidige værdier som søgning, sortering, paginering og scrollposition. Temaindstilling gemmes i localStorage.',
      ],
    },
    {
      heading: 'Cookies vi bruger',
      content: [
        'Workslip bruger følgende lagringsteknologier:',
        '• localStorage — authToken, userEmail, tema og en kortvarig reauth-markør',
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
        'Du kan slette localStorage og sessionStorage via browserens webstedsdata eller logge ud. Sletning af authToken logger dig ud.',
        'Blokering af lagring kan forhindre login, PWA-funktioner, tema, scrollpositioner eller andre dele af brugeroplevelsen i at fungere.',
      ],
    },
  ],
};
