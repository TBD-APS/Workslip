export const MAX_DOCUMENT_ATTACHMENT_MB = 75;
export const MAX_DOCUMENT_ATTACHMENT_BYTES = MAX_DOCUMENT_ATTACHMENT_MB * 1024 * 1024;
export const ACCEPTED_DOCUMENT_FILES = '.mp3,.wav,.ogg,.mp4,.pdf,.png,.jpg,.jpeg,.webp,.txt,.md,.csv';

export function validateDocumentAttachment(file: Pick<File, 'size'>): string | null {
  if (file.size <= 0) return 'Filen er tom.';
  if (file.size > MAX_DOCUMENT_ATTACHMENT_BYTES) {
    return `Filen må højst være ${MAX_DOCUMENT_ATTACHMENT_MB} MB.`;
  }
  return null;
}
