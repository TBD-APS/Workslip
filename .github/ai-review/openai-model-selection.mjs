export function selectOpenAiReviewModel(availableModelIds, preferredModel, fallbackModels) {
  const available = new Set(
    Array.from(availableModelIds || [])
      .filter((value) => typeof value === 'string')
      .map((value) => value.trim())
      .filter(Boolean),
  );

  const candidates = [preferredModel, ...(fallbackModels || [])]
    .filter((value) => typeof value === 'string')
    .map((value) => value.trim())
    .filter(Boolean);

  for (const candidate of new Set(candidates)) {
    if (available.has(candidate)) return candidate;
  }

  return null;
}
