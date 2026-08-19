export const docsQueryKeys = {
  all: ['docs'] as const,
  lists: () => ['docs', 'list'] as const,
  list: (search: string) => ['docs', 'list', search] as const,
  details: () => ['docs', 'detail'] as const,
  detail: (documentId: string | null) => ['docs', 'detail', documentId] as const,
  attachments: (documentId: string) => ['docs', 'attachments', documentId] as const,
};
