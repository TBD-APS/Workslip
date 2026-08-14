import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Camera, ChevronLeft, ChevronRight, ImagePlus, Loader2, Trash2, X } from 'lucide-react';
import { ConfirmDeleteDialog } from '../../components/common/ConfirmDeleteDialog';
import { notify } from '../../lib/toast';
import {
  deleteJobImage,
  fetchJobImageBlob,
  listJobImages,
  uploadJobImage,
  type ImageInfo,
} from './imageApi';
import { jobImageBlobQueryKey, jobImagesQueryKey } from './imageQueryKeys';
import { useObjectUrl } from './useObjectUrl';
import './images.css';

const MAX_IMAGE_SIZE = 10 * 1024 * 1024;
const ALLOWED_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

type JobImagesSectionProps = {
  jobId: string;
  allowManage?: boolean;
};

export function JobImagesSection({ jobId, allowManage = false }: JobImagesSectionProps) {
  const queryClient = useQueryClient();
  const cameraInputRef = useRef<HTMLInputElement>(null);
  const libraryInputRef = useRef<HTMLInputElement>(null);
  const [uploadProgress, setUploadProgress] = useState<{ current: number; total: number } | null>(null);
  const [imageToDelete, setImageToDelete] = useState<string | null>(null);
  const [previewIndex, setPreviewIndex] = useState<number | null>(null);

  const imagesQuery = useQuery({
    queryKey: jobImagesQueryKey(jobId),
    queryFn: () => listJobImages(jobId),
  });

  const deleteMutation = useMutation({
    mutationFn: (imageId: string) => deleteJobImage(jobId, imageId),
    onSuccess: async (_, imageId) => {
      queryClient.removeQueries({ queryKey: jobImageBlobQueryKey(jobId, imageId) });
      await queryClient.invalidateQueries({ queryKey: jobImagesQueryKey(jobId) });
      notify.success('Billedet er slettet');
    },
    onError: () => notify.error('Kunne ikke slette billedet. Prøv igen.'),
  });

  const handleFiles = async (fileList: FileList | null) => {
    const files = Array.from(fileList ?? []);
    if (cameraInputRef.current) cameraInputRef.current.value = '';
    if (libraryInputRef.current) libraryInputRef.current.value = '';
    if (files.length === 0) return;

    const validFiles = files.filter((file) => ALLOWED_TYPES.has(file.type) && file.size > 0 && file.size <= MAX_IMAGE_SIZE);
    const rejectedCount = files.length - validFiles.length;
    if (rejectedCount > 0) {
      notify.error(`${rejectedCount} billede${rejectedCount === 1 ? '' : 'r'} blev afvist. Brug JPEG, PNG eller WebP på maks. 10 MB.`);
    }
    if (validFiles.length === 0) return;

    let uploadedCount = 0;
    setUploadProgress({ current: 0, total: validFiles.length });

    for (let index = 0; index < validFiles.length; index += 1) {
      setUploadProgress({ current: index + 1, total: validFiles.length });
      try {
        await uploadJobImage(jobId, validFiles[index]);
        uploadedCount += 1;
      } catch {
        notify.error(`Kunne ikke uploade billede ${index + 1} af ${validFiles.length}.`);
      }
    }

    setUploadProgress(null);
    if (uploadedCount > 0) {
      await queryClient.invalidateQueries({ queryKey: jobImagesQueryKey(jobId) });
      notify.success(`${uploadedCount} billede${uploadedCount === 1 ? '' : 'r'} uploadet`);
    }
  };

  const confirmDelete = async () => {
    if (!imageToDelete) return;
    const imageId = imageToDelete;
    setImageToDelete(null);
    await deleteMutation.mutateAsync(imageId).catch(() => undefined);
  };

  const images = imagesQuery.data ?? [];

  return (
    <section className="job-images-section" aria-labelledby={`job-images-${jobId}`}>
      <div className="job-images-heading">
        <div>
          <h3 id={`job-images-${jobId}`}>Billeder</h3>
          <p className="job-images-subtitle">Dokumentation fra sagen</p>
        </div>
        {allowManage && (
          <div className="job-images-upload-actions">
            <input
              ref={cameraInputRef}
              className="sr-only"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              capture="environment"
              onChange={(event) => void handleFiles(event.target.files)}
              disabled={Boolean(uploadProgress)}
            />
            <input
              ref={libraryInputRef}
              className="sr-only"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              multiple
              onChange={(event) => void handleFiles(event.target.files)}
              disabled={Boolean(uploadProgress)}
            />
            <button
              className="btn btn-secondary job-images-upload job-images-camera-action"
              type="button"
              onClick={() => cameraInputRef.current?.click()}
              disabled={Boolean(uploadProgress)}
            >
              <Camera size={16} />
              Tag billede
            </button>
            <button
              className="btn btn-secondary job-images-upload"
              type="button"
              onClick={() => libraryInputRef.current?.click()}
              disabled={Boolean(uploadProgress)}
            >
              {uploadProgress ? <Loader2 size={16} className="spin" /> : <ImagePlus size={16} />}
              {uploadProgress
                ? `Uploader ${uploadProgress.current}/${uploadProgress.total}`
                : 'Vælg billeder'}
            </button>
          </div>
        )}
      </div>

      {imagesQuery.isLoading && (
        <div className="job-images-state" role="status">
          <Loader2 size={18} className="spin" /> Henter billeder...
        </div>
      )}

      {imagesQuery.isError && (
        <div className="job-images-state job-images-state--error">
          Kunne ikke hente billederne.
          <button className="btn-link" type="button" onClick={() => void imagesQuery.refetch()}>Prøv igen</button>
        </div>
      )}

      {imagesQuery.data?.length === 0 && (
        <p className="job-images-state">Der er ikke tilføjet billeder endnu.</p>
      )}

      {images.length > 0 && (
        <div className="job-images-grid">
          {images.map((image, index) => (
            <JobImageTile
              key={image.id}
              jobId={jobId}
              image={image}
              index={index}
              allowDelete={allowManage}
              deleting={deleteMutation.isPending && deleteMutation.variables === image.id}
              onPreview={() => setPreviewIndex(index)}
              onDelete={() => setImageToDelete(image.id)}
            />
          ))}
        </div>
      )}

      {previewIndex !== null && images.length > 0 && (
        <JobImagePreview
          jobId={jobId}
          images={images}
          index={previewIndex}
          onClose={() => setPreviewIndex(null)}
          onPrevious={() => setPreviewIndex((current) => current === null ? null : (current - 1 + images.length) % images.length)}
          onNext={() => setPreviewIndex((current) => current === null ? null : (current + 1) % images.length)}
        />
      )}

      <ConfirmDeleteDialog
        open={Boolean(imageToDelete)}
        title="Slet billede"
        message="Er du sikker på, at billedet skal slettes fra sagen?"
        onConfirm={() => void confirmDelete()}
        onClose={() => setImageToDelete(null)}
      />
    </section>
  );
}

type JobImageTileProps = {
  jobId: string;
  image: ImageInfo;
  index: number;
  allowDelete: boolean;
  deleting: boolean;
  onPreview: () => void;
  onDelete: () => void;
};

function JobImageTile({ jobId, image, index, allowDelete, deleting, onPreview, onDelete }: JobImageTileProps) {
  const tileRef = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(() => typeof IntersectionObserver === 'undefined');

  useEffect(() => {
    const element = tileRef.current;
    if (!element || visible || !('IntersectionObserver' in window)) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setVisible(true);
          observer.disconnect();
        }
      },
      { rootMargin: '200px' },
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, [visible]);

  const imageQuery = useQuery({
    queryKey: jobImageBlobQueryKey(jobId, image.id),
    queryFn: () => fetchJobImageBlob(jobId, image.id),
    enabled: visible,
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
  const objectUrl = useObjectUrl(imageQuery.data);

  return (
    <div ref={tileRef} className="job-image-tile">
      <button
        type="button"
        className="job-image-preview-trigger"
        aria-label={`Åbn sagsbillede ${index + 1} i stor visning`}
        onClick={onPreview}
        disabled={!objectUrl}
      >
        {objectUrl ? (
          <img src={objectUrl} alt={`Sagsbillede ${index + 1}`} loading="lazy" />
        ) : imageQuery.isError ? (
          <div className="job-image-placeholder job-image-placeholder--error">Kunne ikke hente</div>
        ) : (
          <div className="job-image-placeholder" aria-label={`Henter sagsbillede ${index + 1}`}>
            <Loader2 size={18} className="spin" />
          </div>
        )}
      </button>
      {allowDelete && (
        <button
          type="button"
          className="job-image-delete"
          aria-label={`Slet sagsbillede ${index + 1}`}
          onClick={onDelete}
          disabled={deleting}
        >
          {deleting ? <Loader2 size={15} className="spin" /> : <Trash2 size={15} />}
        </button>
      )}
    </div>
  );
}

type JobImagePreviewProps = {
  jobId: string;
  images: ImageInfo[];
  index: number;
  onClose: () => void;
  onPrevious: () => void;
  onNext: () => void;
};

function JobImagePreview({ jobId, images, index, onClose, onPrevious, onNext }: JobImagePreviewProps) {
  const image = images[index];
  const imageQuery = useQuery({
    queryKey: image ? jobImageBlobQueryKey(jobId, image.id) : ['job-image-preview-missing'],
    queryFn: () => fetchJobImageBlob(jobId, image.id),
    enabled: Boolean(image),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
  const objectUrl = useObjectUrl(imageQuery.data);

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
      if (event.key === 'ArrowLeft' && images.length > 1) onPrevious();
      if (event.key === 'ArrowRight' && images.length > 1) onNext();
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [images.length, onClose, onNext, onPrevious]);

  if (!image) return null;

  return (
    <div
      className="job-image-preview-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        className="job-image-preview-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={`Sagsbillede ${index + 1} af ${images.length}`}
      >
        <div className="job-image-preview-toolbar">
          <span>{index + 1} / {images.length}</span>
          <button
            type="button"
            className="job-image-preview-control"
            aria-label="Luk billedvisning"
            onClick={onClose}
            autoFocus
          >
            <X size={22} />
          </button>
        </div>

        <div className="job-image-preview-content">
          {objectUrl ? (
            <img src={objectUrl} alt={`Sagsbillede ${index + 1} af ${images.length}`} />
          ) : imageQuery.isError ? (
            <div className="job-image-preview-state">Kunne ikke hente billedet.</div>
          ) : (
            <div className="job-image-preview-state">
              <Loader2 size={28} className="spin" />
              Henter billede...
            </div>
          )}
        </div>

        {images.length > 1 && (
          <>
            <button
              type="button"
              className="job-image-preview-control job-image-preview-previous"
              aria-label="Forrige billede"
              onClick={onPrevious}
            >
              <ChevronLeft size={28} />
            </button>
            <button
              type="button"
              className="job-image-preview-control job-image-preview-next"
              aria-label="Næste billede"
              onClick={onNext}
            >
              <ChevronRight size={28} />
            </button>
          </>
        )}
      </div>
    </div>
  );
}
