import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DocumentAttachmentInfoResponse } from '../../api/generated/models';
import { DocumentAttachments } from './DocumentAttachments';
import * as docsApi from './docsApi';

vi.mock('../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

vi.mock('./docsApi', () => ({
  listDocumentAttachments: vi.fn(),
  uploadDocumentAttachment: vi.fn(),
  deleteDocumentAttachment: vi.fn(),
  downloadDocumentAttachment: vi.fn(),
}));

const documentId = '11111111-1111-1111-1111-111111111111';
const attachment: DocumentAttachmentInfoResponse = {
  id: '22222222-2222-2222-2222-222222222222',
  documentId,
  fileName: 'optagelse.mp3',
  contentType: 'audio/mpeg',
  sizeBytes: 25 * 1024 * 1024,
  createdAt: '2026-08-18T10:00:00Z',
  uploadedByUserId: null,
  uploadedByDisplayName: 'Admin',
};

function renderAttachments() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <DocumentAttachments documentId={documentId} canEdit={false} />
    </QueryClientProvider>,
  );
}

describe('DocumentAttachments audio download reuse', () => {
  beforeEach(() => {
    vi.mocked(docsApi.listDocumentAttachments).mockResolvedValue([attachment]);
    vi.mocked(docsApi.downloadDocumentAttachment).mockResolvedValue(
      new Blob(['audio-content'], { type: 'audio/mpeg' }),
    );
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:loaded-audio'),
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('downloads an actively loaded audio attachment without issuing a second GET', async () => {
    renderAttachments();

    const playButton = await screen.findByRole('button', { name: 'Afspil optagelse.mp3' });
    fireEvent.click(playButton);

    await screen.findByRole('region', { name: 'Afspiller optagelse.mp3' });
    await waitFor(() => expect(docsApi.downloadDocumentAttachment).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole('button', { name: 'Download optagelse.mp3' }));

    expect(docsApi.downloadDocumentAttachment).toHaveBeenCalledTimes(1);
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalledTimes(1);
  });
});
