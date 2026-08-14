import { customAxiosInstance } from '../../api/fetcherOrval';
import type {
  CreateDocumentRequest,
  DocumentAttachmentInfoResponse,
  DocumentDetailResponse,
  DocumentListResponse,
  UpdateDocumentRequest,
} from '../../api/generated/models';
import { apiClient } from '../../lib/axios';

export type DocumentListParams = {
  limit?: number;
  offset?: number;
  search?: string;
};

export function listDocuments(params?: DocumentListParams) {
  return customAxiosInstance<DocumentListResponse>({
    url: '/api/docs/',
    method: 'GET',
    params,
  });
}

export function getDocument(id: string) {
  return customAxiosInstance<DocumentDetailResponse>({
    url: `/api/docs/${id}`,
    method: 'GET',
  });
}

export function createDocument(data: CreateDocumentRequest) {
  return customAxiosInstance<DocumentDetailResponse>({
    url: '/api/docs/',
    method: 'POST',
    data,
  });
}

export function updateDocument(id: string, data: UpdateDocumentRequest) {
  return customAxiosInstance<DocumentDetailResponse>({
    url: `/api/docs/${id}`,
    method: 'PUT',
    data,
  });
}

export function deleteDocument(id: string) {
  return customAxiosInstance<void>({
    url: `/api/docs/${id}`,
    method: 'DELETE',
  });
}

export function listDocumentAttachments(documentId: string) {
  return customAxiosInstance<DocumentAttachmentInfoResponse[]>({
    url: `/api/docs/${documentId}/attachments`,
    method: 'GET',
  });
}

export function uploadDocumentAttachment(documentId: string, file: File) {
  const data = new FormData();
  data.append('file', file);
  return customAxiosInstance<DocumentAttachmentInfoResponse>({
    url: `/api/docs/${documentId}/attachments`,
    method: 'POST',
    data,
  });
}

export function deleteDocumentAttachment(documentId: string, attachmentId: string) {
  return customAxiosInstance<void>({
    url: `/api/docs/${documentId}/attachments/${attachmentId}`,
    method: 'DELETE',
  });
}

export async function downloadDocumentAttachment(documentId: string, attachmentId: string): Promise<Blob> {
  const response = await apiClient.get<Blob>(
    `/api/docs/${documentId}/attachments/${attachmentId}`,
    {
      responseType: 'blob',
      skipGlobalErrorToast: true,
    },
  );
  return response.data;
}
