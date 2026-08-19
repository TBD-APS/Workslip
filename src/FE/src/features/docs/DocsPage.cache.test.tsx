import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DocumentDetailResponse, DocumentListResponse } from '../../api/generated/models';
import { DocsPage } from './DocsPage';
import * as docsApi from './docsApi';

vi.mock('../../providers/permissions/usePermissions', () => ({
  useCan: () => true,
}));

vi.mock('../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

vi.mock('./docsApi', () => ({
  listDocuments: vi.fn(),
  getDocument: vi.fn(),
  createDocument: vi.fn(),
  updateDocument: vi.fn(),
  deleteDocument: vi.fn(),
  listDocumentAttachments: vi.fn(),
  uploadDocumentAttachment: vi.fn(),
  deleteDocumentAttachment: vi.fn(),
  downloadDocumentAttachment: vi.fn(),
}));

const documentId = '11111111-1111-1111-1111-111111111111';
const original: DocumentDetailResponse = {
  id: documentId,
  title: 'Original titel',
  content: 'Originalt indhold',
  tags: ['Drift'],
  createdAt: '2026-08-18T10:00:00Z',
  updatedAt: '2026-08-18T10:00:00Z',
  createdByUserId: null,
  createdByDisplayName: null,
  updatedByUserId: null,
  updatedByDisplayName: 'Admin',
  revision: 1,
};

const listResponse: DocumentListResponse = {
  items: [{
    id: documentId,
    title: original.title,
    preview: original.content,
    tags: original.tags,
    updatedAt: original.updatedAt,
    updatedByDisplayName: original.updatedByDisplayName,
    revision: original.revision,
  }],
  totalCount: 1,
};

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const router = createMemoryRouter(
    [{ path: '/app/docs/:id', element: <DocsPage /> }],
    { initialEntries: [`/app/docs/${documentId}`] },
  );

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe('DocsPage cache invalidation', () => {
  beforeEach(() => {
    vi.mocked(docsApi.listDocuments).mockResolvedValue({ ...listResponse, totalCount: Number(listResponse.totalCount) });
    vi.mocked(docsApi.getDocument).mockResolvedValue(original);
    vi.mocked(docsApi.listDocumentAttachments).mockResolvedValue([]);
    vi.mocked(docsApi.updateDocument).mockImplementation(async (_id, request) => ({
      ...original,
      title: request.title,
      content: request.content,
      tags: request.tags ?? [],
      updatedAt: '2026-08-18T11:00:00Z',
      revision: 2,
    }));
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('refreshes document lists after update without refetching detail or attachments', async () => {
    renderPage();

    await screen.findByRole('heading', { name: 'Original titel' });
    await waitFor(() => expect(docsApi.listDocumentAttachments).toHaveBeenCalledTimes(1));
    expect(docsApi.getDocument).toHaveBeenCalledTimes(1);
    expect(docsApi.listDocuments).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }));
    const titleInput = screen.getByLabelText('Titel');
    fireEvent.change(titleInput, { target: { value: 'Opdateret titel' } });
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }));

    await waitFor(() => expect(docsApi.updateDocument).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(docsApi.listDocuments).toHaveBeenCalledTimes(2));

    expect(docsApi.getDocument).toHaveBeenCalledTimes(1);
    expect(docsApi.listDocumentAttachments).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('heading', { name: 'Opdateret titel' })).toBeTruthy();
  });
});
