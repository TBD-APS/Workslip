import { describe, expect, it } from 'vitest';
import { MAX_IMAGE_UPLOAD_BYTES, MAX_IMAGE_UPLOAD_MB, validateImageUpload } from './imageUploadPolicy';

const makeFile = (size: number, type = 'image/jpeg') =>
  new File([new Uint8Array(size)], 'photo.jpg', { type });

describe('image upload policy', () => {
  it('accepts images above the former 10 MB limit', () => {
    expect(MAX_IMAGE_UPLOAD_MB).toBe(25);
    expect(validateImageUpload(makeFile(11 * 1024 * 1024))).toBeNull();
  });

  it('rejects images above 25 MB with a specific size message', () => {
    expect(validateImageUpload(makeFile(MAX_IMAGE_UPLOAD_BYTES + 1))).toBe('Billedet må højst være 25 MB.');
  });

  it('reports unsupported image types separately from size errors', () => {
    expect(validateImageUpload(makeFile(1024, 'image/heic'))).toBe('Brug et JPEG-, PNG- eller WebP-billede.');
  });
});
