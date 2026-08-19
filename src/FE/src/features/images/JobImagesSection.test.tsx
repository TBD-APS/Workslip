import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobImagesSection } from './JobImagesSection';
import * as imageApi from './imageApi';

vi.mock('./imageApi', () => ({
  listJobImages: vi.fn(),
  fetchJobImageBlob: vi.fn(),
  uploadJobImage: vi.fn(),
  deleteJobImage: vi.fn(),
}));

const images: imageApi.ImageInfo[] = Array.from({ length: 6 }, (_, index) => ({
  id: `00000000-0000-0000-0000-00000000000${index + 1}`,
  contentType: 'image/jpeg',
  sizeBytes: 1024,
  createdAt: `2026-08-18T10:0${index}:00Z`,
}));

const renderSection = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <JobImagesSection jobId="11111111-1111-1111-1111-111111111111" />
    </QueryClientProvider>,
  );
};

beforeEach(() => {
  vi.mocked(imageApi.listJobImages).mockResolvedValue(images);
  vi.mocked(imageApi.fetchJobImageBlob).mockResolvedValue(new Blob(['image'], { type: 'image/jpeg' }));
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    value: vi.fn(() => 'blob:job-image'),
  });
  Object.defineProperty(URL, 'revokeObjectURL', {
    configurable: true,
    value: vi.fn(),
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('JobImagesSection collapsed gallery', () => {
  it('loads only four image blobs until the user expands the gallery', async () => {
    renderSection();

    const expand = await screen.findByRole('button', { name: 'Se flere billeder (2)' });

    expect(screen.getAllByRole('button', { name: /Åbn sagsbillede .* i stor visning/ })).toHaveLength(4);
    await waitFor(() => expect(imageApi.fetchJobImageBlob).toHaveBeenCalledTimes(4));

    fireEvent.click(expand);

    await waitFor(() => {
      expect(screen.getAllByRole('button', { name: /Åbn sagsbillede .* i stor visning/ })).toHaveLength(6);
    });
    await waitFor(() => expect(imageApi.fetchJobImageBlob).toHaveBeenCalledTimes(6));
    expect(screen.getByRole('button', { name: 'Vis færre billeder' }).getAttribute('aria-expanded')).toBe('true');
  });
});
