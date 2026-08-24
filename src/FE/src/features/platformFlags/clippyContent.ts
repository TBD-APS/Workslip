import type { ClippyReaction } from './clippyController';

export type ClippyBubbleCopy = {
  headline: string;
  body: string;
};

type ContextRule = {
  matches: (pathname: string) => boolean;
  copy: ClippyBubbleCopy;
};

const DEFAULT_COPY: ClippyBubbleCopy = {
  headline: 'Hvad driller?',
  body: 'Jeg holder mig i hjørnet, til du kalder.',
};

const REACTION_COPY: Partial<Record<ClippyReaction, ClippyBubbleCopy>> = {
  attention: {
    headline: 'Herovre.',
    body: 'Det er den her, jeg mener.',
  },
  success: {
    headline: 'Sådan.',
    body: 'Den sad. Jeg smutter tilbage i hjørnet.',
  },
  warning: {
    headline: 'Der er noget her.',
    body: 'Kig på det felt, jeg peger på.',
  },
  thinking: {
    headline: 'Jeg kigger.',
    body: 'Ét øjeblik. Jeg prøver at finde det rigtige sted.',
  },
};

const CONTEXT_RULES: ContextRule[] = [
  {
    matches: (pathname) => /^\/app\/job\/new\/?$/.test(pathname),
    copy: {
      headline: 'Ny sag på vej?',
      body: 'Jeg holder øje med felterne og peger, hvis noget mangler.',
    },
  },
  {
    matches: (pathname) => /^\/app\/job\//.test(pathname),
    copy: {
      headline: 'Sagen er åben.',
      body: 'Hvis et felt driller, peger jeg på det i stedet for at råbe.',
    },
  },
  {
    matches: (pathname) => /^\/app\/(timer|worksheets)\/?/.test(pathname),
    copy: {
      headline: 'Timer uden bøvl.',
      body: 'Jeg holder øje med fejlene. Du tager dig af arbejdet.',
    },
  },
  {
    matches: (pathname) => /^\/app\/customers\/?/.test(pathname),
    copy: {
      headline: 'Kunderne er her.',
      body: 'Jeg blander mig kun, hvis du prikker til mig.',
    },
  },
  {
    matches: (pathname) => /^\/app\/docs\/?/.test(pathname),
    copy: {
      headline: 'Papirarbejde. Bare digitalt.',
      body: 'Jeg holder mig kort. Dokumenterne gør resten.',
    },
  },
  {
    matches: (pathname) => /^\/app\/(completed|jobs?)\/?/.test(pathname),
    copy: {
      headline: 'Sagerne først.',
      body: 'Jeg kan pege på det sted, der mangler noget.',
    },
  },
];

export function getClippyBubbleCopy(pathname: string, reaction: ClippyReaction): ClippyBubbleCopy {
  const reactionCopy = REACTION_COPY[reaction];
  if (reactionCopy) return reactionCopy;

  return CONTEXT_RULES.find((rule) => rule.matches(pathname))?.copy ?? DEFAULT_COPY;
}
