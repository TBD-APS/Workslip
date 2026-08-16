import assert from 'node:assert/strict';
import { selectOpenAiReviewModel } from './openai-model-selection.mjs';

assert.equal(
  selectOpenAiReviewModel(['gpt-b', 'gpt-a'], 'gpt-a', ['gpt-b']),
  'gpt-a',
  'preferred accessible model wins',
);

assert.equal(
  selectOpenAiReviewModel(['gpt-b'], 'gpt-a', ['gpt-b', 'gpt-c']),
  'gpt-b',
  'first accessible fallback wins',
);

assert.equal(
  selectOpenAiReviewModel(['gpt-c'], '', ['gpt-b', 'gpt-c']),
  'gpt-c',
  'blank preference is ignored',
);

assert.equal(
  selectOpenAiReviewModel(['gpt-z'], 'gpt-a', ['gpt-b']),
  null,
  'no accessible candidate fails closed',
);

assert.equal(
  selectOpenAiReviewModel(['gpt-b'], 'gpt-a', ['gpt-a', 'gpt-b', 'gpt-b']),
  'gpt-b',
  'duplicate candidates do not change ordering',
);

console.log('OpenAI review model selection tests passed');
