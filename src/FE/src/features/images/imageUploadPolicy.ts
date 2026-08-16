export const MAX_IMAGE_UPLOAD_MB = 25;
export const MAX_IMAGE_UPLOAD_BYTES = MAX_IMAGE_UPLOAD_MB * 1024 * 1024;
export const IMAGE_UPLOAD_ACCEPT = 'image/jpeg,image/png,image/webp';
export const ALLOWED_IMAGE_UPLOAD_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

export function validateImageUpload(file: File): string | null {
  if (file.size <= 0) return 'Billedfilen er tom.';
  if (!ALLOWED_IMAGE_UPLOAD_TYPES.has(file.type)) {
    return 'Brug et JPEG-, PNG- eller WebP-billede.';
  }
  if (file.size > MAX_IMAGE_UPLOAD_BYTES) {
    return `Billedet må højst være ${MAX_IMAGE_UPLOAD_MB} MB.`;
  }
  return null;
}
