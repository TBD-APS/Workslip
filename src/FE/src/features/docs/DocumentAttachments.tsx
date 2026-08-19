import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, File as FileIcon, FileAudio, FileImage, FileText, Pause, Play, Plus, Trash2 } from 'lucide-react';
import type { DocumentAttachmentInfoResponse } from '../../api/generated/models';
import { ConfirmDeleteDialog } from '../../components/common/ConfirmDeleteDialog';
import { formatDateTime } from '../../lib/formatDate';
import { formatNumber } from '../../lib/presentation/number';
import { notify } from '../../lib/toast';
import { getUploadErrorMessage } from '../../lib/uploadError';
import {
  deleteDocumentAttachment,
  downloadDocumentAttachment,
  listDocumentAttachments,
  uploadDocumentAttachment,
} from './docsApi';
import { docsQueryKeys } from './docsQueryKeys';
import {
  ACCEPTED_DOCUMENT_FILES,
  MAX_DOCUMENT_ATTACHMENT_MB,
  validateDocumentAttachment,
} from './documentUploadPolicy';

const formatBytes = (bytes: number | string): string => {
  const value = Number(bytes);
  if (!Number.isFinite(value) || value < 0) return 'Ukendt størrelse';
  if (value < 1024) return `${value} B`;
  const kb = value / 1024;
  if (kb < 1024) return `${formatNumber(kb, { maximumFractionDigits: 1 })} KB`;
  return `${formatNumber(kb / 1024, { maximumFractionDigits: 1 })} MB`;
};

const isAudio = (attachment: DocumentAttachmentInfoResponse) => attachment.contentType.startsWith('audio/');
const isImage = (attachment: DocumentAttachmentInfoResponse) => attachment.contentType.startsWith('image/');

const AttachmentIcon = ({ attachment }: { attachment: DocumentAttachmentInfoResponse }) => {
  if (isAudio(attachment)) return <FileAudio size={19} aria-hidden="true" />;
  if (isImage(attachment)) return <FileImage size={19} aria-hidden="true" />;
  if (attachment.contentType === 'application/pdf' || attachment.contentType.startsWith('text/')) {
    return <FileText size={19} aria-hidden="true" />;
  }
  return <FileIcon size={19} aria-hidden="true" />;
};

type AudioPreview = {
  attachmentId: string;
  fileName: string;
  url: string;
};

interface DocumentAttachmentsProps {
  documentId: string;
  canEdit: boolean;
}

const triggerDownload = (url: string, fileName: string) => {
  const anchor = window.document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
};

export function DocumentAttachments({ documentId, canEdit }: DocumentAttachmentsProps) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const audioPreviewRef = useRef<AudioPreview | null>(null);
  const [audioPreview, setAudioPreview] = useState<AudioPreview | null>(null);
  const [loadingAudioId, setLoadingAudioId] = useState<string | null>(null);
  const [attachmentToRemove, setAttachmentToRemove] = useState<DocumentAttachmentInfoResponse | null>(null);
  const attachmentsKey = docsQueryKeys.attachments(documentId);

  const attachmentsQuery = useQuery({
    queryKey: attachmentsKey,
    queryFn: () => listDocumentAttachments(documentId),
    staleTime: 15_000,
  });

  useEffect(() => () => {
    if (audioPreviewRef.current) URL.revokeObjectURL(audioPreviewRef.current.url);
  }, []);

  const replaceAudioPreview = (next: AudioPreview | null) => {
    if (audioPreviewRef.current) URL.revokeObjectURL(audioPreviewRef.current.url);
    audioPreviewRef.current = next;
    setAudioPreview(next);
  };

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadDocumentAttachment(documentId, file),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: attachmentsKey });
      notify.success('Filen er tilføjet.');
    },
    onError: (error) => notify.error(getUploadErrorMessage(error, {
      fallback: 'Filen kunne ikke uploades. Prøv igen.',
      tooLarge: `Filen må højst være ${MAX_DOCUMENT_ATTACHMENT_MB} MB.`,
    })),
  });

  const deleteMutation = useMutation({
    mutationFn: (attachmentId: string) => deleteDocumentAttachment(documentId, attachmentId),
    onSuccess: async (_, attachmentId) => {
      setAttachmentToRemove(null);
      if (audioPreviewRef.current?.attachmentId === attachmentId) replaceAudioPreview(null);
      await queryClient.invalidateQueries({ queryKey: attachmentsKey });
      notify.success('Filen er fjernet.');
    },
    onError: () => notify.error('Filen kunne ikke fjernes.'),
  });

  const handleFile = (file: File | undefined) => {
    if (!file) return;
    const validationError = validateDocumentAttachment(file);
    if (validationError) {
      notify.error(validationError);
      return;
    }
    uploadMutation.mutate(file);
  };

  const download = async (attachment: DocumentAttachmentInfoResponse) => {
    const loadedAudio = audioPreviewRef.current;
    if (loadedAudio?.attachmentId === attachment.id) {
      triggerDownload(loadedAudio.url, attachment.fileName);
      return;
    }

    try {
      const blob = await downloadDocumentAttachment(documentId, attachment.id);
      const url = URL.createObjectURL(blob);
      triggerDownload(url, attachment.fileName);
      window.setTimeout(() => URL.revokeObjectURL(url), 0);
    } catch {
      notify.error('Filen kunne ikke hentes.');
    }
  };

  const toggleAudio = async (attachment: DocumentAttachmentInfoResponse) => {
    if (audioPreview?.attachmentId === attachment.id) {
      replaceAudioPreview(null);
      return;
    }

    setLoadingAudioId(attachment.id);
    try {
      const blob = await downloadDocumentAttachment(documentId, attachment.id);
      replaceAudioPreview({
        attachmentId: attachment.id,
        fileName: attachment.fileName,
        url: URL.createObjectURL(blob),
      });
    } catch {
      notify.error('Lydfilen kunne ikke afspilles.');
    } finally {
      setLoadingAudioId(null);
    }
  };

  const attachments = attachmentsQuery.data ?? [];

  return (
    <>
      <section className="docs-attachments" aria-labelledby={`docs-attachments-${documentId}`}>
        <div className="docs-attachments-header">
          <div>
            <span className="docs-eyebrow">Filer</span>
            <h3 id={`docs-attachments-${documentId}`}>Vedhæftninger</h3>
          </div>
          {canEdit && (
            <>
              <input
                ref={inputRef}
                className="docs-file-input"
                type="file"
                accept={ACCEPTED_DOCUMENT_FILES}
                onChange={(event) => {
                  handleFile(event.target.files?.[0]);
                  event.currentTarget.value = '';
                }}
              />
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => inputRef.current?.click()}
                disabled={uploadMutation.isPending}
              >
                <Plus size={16} aria-hidden="true" /> {uploadMutation.isPending ? 'Uploader…' : 'Tilføj fil'}
              </button>
            </>
          )}
        </div>

        <p className="docs-attachments-help">MP3/WAV/OGG, MP4, PDF, billeder, TXT/MD eller CSV · maks. {MAX_DOCUMENT_ATTACHMENT_MB} MB pr. fil.</p>

        {attachmentsQuery.isLoading && <div className="docs-attachments-state">Henter filer…</div>}
        {attachmentsQuery.isError && (
          <div className="docs-attachments-state docs-attachments-state--error">
            <span>Filerne kunne ikke hentes.</span>
            <button type="button" className="btn btn-secondary" onClick={() => attachmentsQuery.refetch()}>Prøv igen</button>
          </div>
        )}

        {!attachmentsQuery.isLoading && !attachmentsQuery.isError && attachments.length === 0 && (
          <div className="docs-attachments-empty">
            <FileIcon size={22} aria-hidden="true" />
            <span>Ingen filer er vedhæftet endnu.</span>
          </div>
        )}

        {attachments.length > 0 && (
          <div className="docs-attachment-list">
            {attachments.map((attachment) => (
              <div className="docs-attachment-row" key={attachment.id}>
                <span className="docs-attachment-icon"><AttachmentIcon attachment={attachment} /></span>
                <div className="docs-attachment-copy">
                  <strong>{attachment.fileName}</strong>
                  <span>
                    {formatBytes(attachment.sizeBytes)} · {formatDateTime(attachment.createdAt)}
                    {attachment.uploadedByDisplayName ? ` · ${attachment.uploadedByDisplayName}` : ''}
                  </span>
                </div>
                <div className="docs-attachment-actions">
                  {isAudio(attachment) && (
                    <button
                      type="button"
                      className="docs-attachment-action"
                      onClick={() => { void toggleAudio(attachment); }}
                      disabled={loadingAudioId === attachment.id}
                      aria-label={audioPreview?.attachmentId === attachment.id ? `Stop ${attachment.fileName}` : `Afspil ${attachment.fileName}`}
                    >
                      {audioPreview?.attachmentId === attachment.id ? <Pause size={17} aria-hidden="true" /> : <Play size={17} aria-hidden="true" />}
                    </button>
                  )}
                  <button
                    type="button"
                    className="docs-attachment-action"
                    onClick={() => { void download(attachment); }}
                    aria-label={`Download ${attachment.fileName}`}
                  >
                    <Download size={17} aria-hidden="true" />
                  </button>
                  {canEdit && (
                    <button
                      type="button"
                      className="docs-attachment-action docs-attachment-action--danger"
                      onClick={() => setAttachmentToRemove(attachment)}
                      disabled={deleteMutation.isPending}
                      aria-label={`Fjern ${attachment.fileName}`}
                    >
                      <Trash2 size={17} aria-hidden="true" />
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {audioPreview && (
          <div className="docs-audio-preview" role="region" aria-label={`Afspiller ${audioPreview.fileName}`}>
            <span>{audioPreview.fileName}</span>
            <audio src={audioPreview.url} controls autoPlay preload="metadata" />
          </div>
        )}
      </section>

      <ConfirmDeleteDialog
        open={Boolean(attachmentToRemove)}
        title="Fjern fil"
        message={attachmentToRemove ? `Fjern “${attachmentToRemove.fileName}” fra dokumentet?` : 'Fjern filen fra dokumentet?'}
        confirmLabel="Fjern"
        onConfirm={() => attachmentToRemove ? deleteMutation.mutateAsync(attachmentToRemove.id) : undefined}
        onClose={() => setAttachmentToRemove(null)}
      />
    </>
  );
}
