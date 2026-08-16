import { describe, expect, it } from 'vitest';
import {
  MAX_DOCUMENT_ATTACHMENT_BYTES,
  MAX_DOCUMENT_ATTACHMENT_MB,
  validateDocumentAttachment,
} from './documentUploadPolicy';

describe('document upload policy', () => {
  it('raises the file limit to 75 MB', () => {
    expect(MAX_DOCUMENT_ATTACHMENT_MB).toBe(75);
    expect(validateDocumentAttachment({ size: 21 * 1024 * 1024 })).toBeNull();
  });

  it('accepts the exact 75 MB boundary', () => {
    expect(validateDocumentAttachment({ size: MAX_DOCUMENT_ATTACHMENT_BYTES })).toBeNull();
  });

  it('rejects files above 75 MB with a specific message', () => {
    expect(validateDocumentAttachment({ size: MAX_DOCUMENT_ATTACHMENT_BYTES + 1 }))
      .toBe('Filen må højst være 75 MB.');
  });

  it('rejects empty files separately', () => {
    expect(validateDocumentAttachment({ size: 0 })).toBe('Filen er tom.');
  });
});
