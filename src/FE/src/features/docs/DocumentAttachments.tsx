import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, File as FileIcon, FileAudio, FileImage, FileText, Pause, Play, Plus, Trash2 } from 'lucide-react';
import type { DocumentAttachmentInfoResponse } from '../../api/generated/models';
import { notify } from '../../lib/toast';
import {
  deleteDocumentAttachment,
  downloadDocumentAttachment,
  listDocumentAttachments,
  uploadDocumentAttachment,
} from './docsApi';

const MAX_ATTACHMENT_BYTES = 20 * 1024 * 1024;
const ACCEPTED_FILES = '.mp3,.wav,.ogg,.mp4,.pdf,.png,.jpg,.jpeg,.webp,.txt,.md,.csv';

const formatBytes = (bytes: number | string): string => {
  const value = Number(bytes);
  if (!Number.isFinite(value) || value < 0) return 'Ukendt størrelse';
  if (value < 1024) return `${value} B`;
  const kb = value / 1024;
  if (kb < 1024) return `${kb.toLocaleString('da-DK', { maximumFractionDigits: 1 })} KB`;
  return `${(kb / 1024).toLocaleString('da-DK', { maximumFractionDigits: 1 })} MB`;
};

const formatDate = (value: string): string =>
  new Intl.DateTimeFormat('da-DK', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));

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

export function DocumentAttachments({ documentId, canEdit }: DocumentAttachmentsProps) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const audioPreviewRef = useRef<AudioPreview | null>(null);
  const [audioPreview, setAudioPreview] = useState<AudioPreview | null>(null);
  const [loadingAudioId, setLoadingAudioId] = useState<string | null>(null);

  const attachmentsQuery = useQuery({
    queryKey: ['docs', 'attachments', documentId],
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
      await queryClient.invalidateQueries({ queryKey: ['docs', 'attachments', documentId] });
      notify.success('Filen er tilføjet.');
    },
    onError: () => notify.error('Filen kunne ikke uploades.'),
  });

  const deleteMutation = useMutation({
    mutationFn: (attachmentId: string) => deleteDocumentAttachment(documentId, attachmentId),
    onSuccess: async (_, attachmentId) => {
      if (audioPreviewRef.current?.attachmentId === attachmentId) replaceAudioPreview(null);
      await queryClient.invalidateQueries({ queryKey: ['docs', 'attachments', documentId] });
      notify.success('Filen er fjernet.');
    },
    onError: () => notify.error('Filen kunne ikke fjernes.'),
  });

  const handleFile = (file: File | undefined) => {
    if (!file) return;
    if (file.size <= 0) {
      notify.error('Filen er tom.');
      return;
    }
    if (file.size > MAX_ATTACHMENT_BYTES) {
      notify.error('Filen må højst være 20 MB.');
      return;
    }
    uploadMutation.mutate(file);
  };

  const download = async (attachment: DocumentAttachmentInfoResponse) => {
    try {
      const blob = await downloadDocumentAttachment(documentId, attachment.id);
      const url = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = url;
      anchor.download = attachment.fileName;
      anchor.click();
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

  const remove = (attachment: DocumentAttachmentInfoResponse) => {
    if (!window.confirm(`Fjern “${attachment.fileName}” fra dokumentet?`)) return;
    deleteMutation.mutate(attachment.id);
  };

  const attachments = attachmentsQuery.data ?? [];

  return (
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
              accept={ACCEPTED_FILES}
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
              <Plus size={16} /> {uploadMutation.isPending ? 'Uploader…' : 'Tilføj fil'}
            </button>
          </>
        )}
      </div>

      <p className="docs-attachments-help">MP3/WAV/OGG, MP4, PDF, billeder, TXT/MD eller CSV · maks. 20 MB pr. fil.</p>

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
                  {formatBytes(attachment.sizeBytes)} · {formatDate(attachment.createdAt)}
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
                    {audioPreview?.attachmentId === attachment.id ? <Pause size={17} /> : <Play size={17} />}
                  </button>
                )}
                <button
                  type="button"
                  className="docs-attachment-action"
                  onClick={() => { void download(attachment); }}
                  aria-label={`Download ${attachment.fileName}`}
                >
                  <Download size={17} />
                </button>
                {canEdit && (
                  <button
                    type="button"
                    className="docs-attachment-action docs-attachment-action--danger"
                    onClick={() => remove(attachment)}
                    disabled={deleteMutation.isPending}
                    aria-label={`Fjern ${attachment.fileName}`}
                  >
                    <Trash2 size={17} />
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
  );
}
