import { LegalContent } from './types';

export const cookiesContent: LegalContent = {
  title: 'Cookiepolitik',
  lastUpdated: '23. juli 2026',
  sections: [
    {
      heading: 'Hvad er cookies',
      content: [
        'Cookies er små filer, der gemmes på din enhed, når du besøger en hjemmeside. De bruges til at huske dine indstillinger og forbedre din oplevelse.',
      ],
    },
    {
      heading: 'Cookies vi bruger',
      content: [
        'Workslip bruger følgende lagringsteknologier:',
        '• localStorage — gemmer dit login-token og temaindstilling',
        '• sessionStorage — gemmer scrollpositioner, paginering og midlertidige auth-nøgler',
        '• HttpOnly cookies — sættes af vores backend til autentificering',
      ],
    },
    {
      heading: 'Tredjeparts cookies',
      content: [
        'Vi bruger følgende tredjepartstjenester, der kan sætte cookies:',
        '• Vercel Analytics — til at analysere sidevisninger og ydeevne',
        '• Microsoft Application Insights — til fejlsporing og overvågning',
        'Disse tjenester bruger ikke cookies til reklame eller sporing på tværs af hjemmesider.',
      ],
    },
    {
      heading: 'Sådan styrer du cookies',
      content: [
        'Du kan styre og slette cookies via din browsers indstillinger.',
        'Bemærk, at deaktivering af cookies kan påvirke funktionaliteten af Workslip.',
      ],
    },
  ],
};
