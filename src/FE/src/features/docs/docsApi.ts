import { customAxiosInstance } from '../../api/fetcherOrval';
import type {
  CreateDocumentRequest,
  DocumentDetailResponse,
  DocumentListResponse,
  UpdateDocumentRequest,
} from '../../api/generated/models';

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
