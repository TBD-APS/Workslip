export function isValidEmail(value: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}

export function isValidPhone(value: string) {
  return /^[\d\s+\-()]+$/.test(value) && value.replace(/\D/g, '').length >= 8;
}

const fieldLabels: Record<string, string> = {
  name: 'Kundenavn',
  email: 'E-mail',
  phone: 'Telefon',
};

export type CustomerFieldErrors = Record<string, string>;

export function validateCustomer(values: { name: string; email: string; phone: string }) {
  const errors: CustomerFieldErrors = {};

  if (!values.name.trim()) {
    errors.name = `${fieldLabels.name} er påkrævet.`;
  }

  if (values.email.trim() && !isValidEmail(values.email.trim())) {
    errors.email = `${fieldLabels.email} er ikke gyldig.`;
  }

  if (values.phone.trim() && !isValidPhone(values.phone.trim())) {
    errors.phone = `${fieldLabels.phone} er ikke gyldig.`;
  }

  return errors;
}
