import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DocumentListItemResponse } from '../../api/generated/models';
import { DocsPage } from './DocsPage';
import * as docsApi from './docsApi';

vi.mock('../../providers/permissions/usePermissions', () => ({
  useCan: () => false,
}));

vi.mock('./docsApi', () => ({
  listDocuments: vi.fn(),
  getDocument: vi.fn(),
  createDocument: vi.fn(),
  updateDocument: vi.fn(),
  deleteDocument: vi.fn(),
}));

const documents: DocumentListItemResponse[] = Array.from({ length: 51 }, (_, index) => ({
  id: `00000000-0000-0000-0000-${String(index + 1).padStart(12, '0')}`,
  title: `Dokument ${index + 1}`,
  preview: `Indhold ${index + 1}`,
  tags: [],
  updatedAt: '2026-08-18T10:00:00Z',
  updatedByDisplayName: null,
  revision: 1,
}));

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const router = createMemoryRouter(
    [{ path: '/app/docs', element: <DocsPage /> }],
    { initialEntries: ['/app/docs'] },
  );

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe('DocsPage pagination', () => {
  beforeEach(() => {
    vi.mocked(docsApi.listDocuments).mockImplementation(async (params) => {
      const offset = params?.offset ?? 0;
      const limit = params?.limit ?? 50;
      return {
        items: documents.slice(offset, offset + limit),
        totalCount: documents.length,
      };
    });
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('loads documents beyond the first page on demand', async () => {
    renderPage();

    const loadMore = await screen.findByRole('button', { name: 'Vis flere (1)' });
    expect(screen.getAllByRole('listitem')).toHaveLength(50);
    expect(docsApi.listDocuments).toHaveBeenCalledWith(expect.objectContaining({ limit: 50, offset: 0 }));

    fireEvent.click(loadMore);

    await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(51));
    expect(docsApi.listDocuments).toHaveBeenCalledWith(expect.objectContaining({ limit: 50, offset: 50 }));
    expect(screen.queryByRole('button', { name: /Vis flere/ })).toBeNull();
  });
});
