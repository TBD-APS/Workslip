export type ValidationResult = string | null;

export function validateEmail(value: string | null): ValidationResult {
  if (!value?.trim()) return null;
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim()) ? null : 'Indtast en gyldig email-adresse.';
}

export function validatePhoneNumber(value: string | null): ValidationResult {
  if (!value?.trim()) return null;

  const trimmed = value.trim();
  const digits = trimmed.replace(/\D/g, '');
  const hasValidCharacters = /^[+\d()\s.-]+$/.test(trimmed);

  if (!hasValidCharacters || digits.length !== 8) {
    return 'Indtast et telefonnummer på 8 cifre.';
  }

  return null;
}
