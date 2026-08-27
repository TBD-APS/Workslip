import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  ArrowLeft,
  BookOpen,
  Check,
  Clock3,
  FilePlus2,
  Pencil,
  Search,
  Tag,
  Trash2,
  X,
} from 'lucide-react';
import type { DocumentDetailResponse, DocumentListItemResponse } from '../../api/generated/models';
import { ConfirmDeleteDialog } from '../../components/common/ConfirmDeleteDialog';
import { ConfirmDialog } from '../../components/common/ConfirmDialog';
import { ErrorState } from '../../components/ErrorState';
import { useInfiniteList } from '../../hooks/useInfiniteList';
import { formatDateTime } from '../../lib/formatDate';
import { formatNumber } from '../../lib/presentation/number';
import { toUiLowerCase } from '../../lib/presentation/text';
import { notify } from '../../lib/toast';
import { useCan } from '../../providers/permissions/usePermissions';
import { DocumentAttachments } from './DocumentAttachments';
import {
  createDocument,
  deleteDocument,
  getDocument,
  listDocuments,
  updateDocument,
} from './docsApi';
import { docsQueryKeys } from './docsQueryKeys';
import './docs.css';
import './docsAttachments.css';

type Draft = {
  title: string;
  content: string;
  tagsText: string;
  revision: number;
};

type DraftState = {
  key: string;
  value: Draft;
};

const DOCS_PAGE_SIZE = 50;

const emptyDraft = (): Draft => ({ title: '', content: '', tagsText: '', revision: 0 });

const toDraft = (document: DocumentDetailResponse): Draft => ({
  title: document.title,
  content: document.content,
  tagsText: (document.tags ?? []).join(', '),
  revision: Number(document.revision),
});

const parseTags = (value: string): string[] => {
  const seen = new Set<string>();
  const tags: string[] = [];
  for (const part of value.split(',')) {
    const tag = part.trim();
    if (!tag) continue;
    const key = toUiLowerCase(tag);
    if (seen.has(key)) continue;
    seen.add(key);
    tags.push(tag);
  }
  return tags;
};

const formatUpdatedAt = (value: string): string => formatDateTime(value) ?? value;

const isConflict = (error: unknown): boolean =>
  typeof error === 'object'
  && error !== null
  && 'response' in error
  && (error as { response?: { status?: number } }).response?.status === 409;

const SUGGESTED_CATEGORIES = [
  'Onboarding',
  'Teknik & Drift',
  'Procedurer',
  'Produktvejledninger',
];

export const DocsPage = () => {
  const { id } = useParams<{ id?: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useCan('docs:edit');
  const isCreating = id === 'new' || location.pathname === '/app/docs/new';
  const selectedId = id && id !== 'new' ? id : null;
  const draftKey = isCreating ? 'new' : selectedId ?? 'none';

  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [draftState, setDraftState] = useState<DraftState | null>(null);
  const [editingDocumentId, setEditingDocumentId] = useState<string | null>(null);
  const [pendingNavigationPath, setPendingNavigationPath] = useState<string | null>(null);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => window.clearTimeout(handle);
  }, [search]);

  const listQuery = useInfiniteList<DocumentListItemResponse>({
    queryKey: docsQueryKeys.list(debouncedSearch),
    pageSize: DOCS_PAGE_SIZE,
    fetchPage: ({ limit, offset }) => listDocuments({
      limit,
      offset,
      search: debouncedSearch || undefined,
    }),
  });

  const detailQuery = useQuery({
    queryKey: docsQueryKeys.detail(selectedId),
    queryFn: () => getDocument(selectedId!),
    enabled: Boolean(selectedId),
    staleTime: 10_000,
  });

  const sourceDraft = useMemo(() => {
    if (detailQuery.data) return toDraft(detailQuery.data);
    if (isCreating) {
      const searchParams = new URLSearchParams(location.search);
      const title = searchParams.get('title');
      if (title) return { ...emptyDraft(), title };
    }
    return emptyDraft();
  }, [detailQuery.data, isCreating, location.search]);
  const draft = draftState?.key === draftKey ? draftState.value : sourceDraft;
  const isEditing = isCreating || editingDocumentId === selectedId;
  const isDirty = isCreating
    ? Boolean(draft.title.trim() || draft.content || draft.tagsText.trim())
    : draft.title !== sourceDraft.title
      || draft.content !== sourceDraft.content
      || draft.tagsText !== sourceDraft.tagsText;

  const updateDraft = (mutate: (current: Draft) => Draft) => {
    setDraftState((current) => ({
      key: draftKey,
      value: mutate(current?.key === draftKey ? current.value : sourceDraft),
    }));
  };

  useEffect(() => {
    if (!isDirty) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [isDirty]);

  const invalidateDocumentLists = async () => {
    await queryClient.invalidateQueries({ queryKey: docsQueryKeys.lists() });
  };

  const createMutation = useMutation({
    mutationFn: ({ title, content, tags }: { title: string; content: string; tags: string[] }) =>
      createDocument({
        title: title.trim(),
        content,
        tags,
      }),
    onSuccess: async (document) => {
      queryClient.setQueryData(docsQueryKeys.detail(document.id), document);
      setDraftState(null);
      setEditingDocumentId(null);
      await invalidateDocumentLists();
    },
    onError: () => notify.error('Dokumentet kunne ikke oprettes.'),
  });

  const updateMutation = useMutation({
    mutationFn: () => updateDocument(selectedId!, {
      title: draft.title.trim(),
      content: draft.content,
      tags: parseTags(draft.tagsText),
      revision: draft.revision,
    }),
    onSuccess: async (document) => {
      queryClient.setQueryData(docsQueryKeys.detail(document.id), document);
      setDraftState({ key: document.id, value: toDraft(document) });
      setEditingDocumentId(null);
      await invalidateDocumentLists();
      notify.success('Dokumentet er gemt.');
    },
    onError: async (error) => {
      if (isConflict(error)) {
        setDraftState(null);
        setEditingDocumentId(null);
        notify.error('Dokumentet er ændret af en anden. Den nyeste version hentes nu.');
        await queryClient.invalidateQueries({ queryKey: docsQueryKeys.detail(selectedId) });
        return;
      }
      notify.error('Dokumentet kunne ikke gemmes.');
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteDocument(selectedId!),
    onSuccess: async () => {
      setDeleteDialogOpen(false);
      setDraftState(null);
      setEditingDocumentId(null);
      if (selectedId) {
        queryClient.removeQueries({ queryKey: docsQueryKeys.detail(selectedId) });
        queryClient.removeQueries({ queryKey: docsQueryKeys.attachments(selectedId) });
      }
      await invalidateDocumentLists();
      notify.success('Dokumentet er slettet.');
      navigate('/app/docs', { replace: true });
    },
    onError: () => notify.error('Dokumentet kunne ikke slettes.'),
  });

  const parsedTags = parseTags(draft.tagsText);
  const hasInvalidTags = parsedTags.some((tag) => tag.length > 40) || parsedTags.length > 10;
  const canSave = draft.title.trim().length > 0
    && draft.title.trim().length <= 200
    && draft.content.length <= 200_000
    && !hasInvalidTags
    && !createMutation.isPending
    && !updateMutation.isPending;

  const completeLeave = (path: string) => {
    setPendingNavigationPath(null);
    setDraftState(null);
    setEditingDocumentId(null);
    navigate(path);
  };

  const leaveCurrentDocument = (path: string) => {
    if (isDirty) {
      setPendingNavigationPath(path);
      return;
    }
    completeLeave(path);
  };

  const selectDocument = (documentId: string) => leaveCurrentDocument(`/app/docs/${documentId}`);
  const startNew = () => leaveCurrentDocument('/app/docs/new');

  const cancelEdit = () => {
    if (isCreating) {
      leaveCurrentDocument('/app/docs');
      return;
    }
    setDraftState(null);
    setEditingDocumentId(null);
  };

  const submit = () => {
    if (!canSave) return;
    if (isCreating) createMutation.mutate();
    else updateMutation.mutate();
  };

  const items = listQuery.items;
  const remainingCount = Math.max(listQuery.totalCount - items.length, 0);
  const selectedDocument = detailQuery.data;
  const showWorkspace = isCreating || Boolean(selectedId);

  return (
    <>
      <div className={`docs-shell ${showWorkspace ? 'docs-shell--document-open' : ''}`}>
        <aside className="docs-sidebar" aria-label="Dokumenter">
          <div className="docs-sidebar-header">
            <div>
              <span className="docs-eyebrow">Intern viden</span>
               <h1>Dokumenter</h1>
            </div>
            {canEdit && (
              <button type="button" className="docs-new-button" onClick={startNew} aria-label="Opret dokument">
                <FilePlus2 size={18} aria-hidden="true" />
                <span>Nyt</span>
              </button>
            )}
          </div>

          <label className="docs-search">
            <Search size={17} aria-hidden="true" />
            <span className="sr-only">Søg i dokumenter</span>
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Søg i titel, tekst eller tags"
              maxLength={120}
            />
            {search && (
              <button type="button" onClick={() => setSearch('')} aria-label="Ryd søgning">
                <X size={15} aria-hidden="true" />
              </button>
            )}
          </label>

          <div className="docs-list-meta">
            <span>{listQuery.totalCount} dokumenter</span>
            {debouncedSearch && <span>Matcher “{debouncedSearch}”</span>}
          </div>

          <div className="docs-list" role="list">
            {listQuery.isLoading && Array.from({ length: 5 }).map((_, index) => (
              <div className="docs-list-skeleton" key={index} aria-hidden="true" />
            ))}

            {listQuery.isError && (
              <div className="docs-sidebar-state">
                <p>Dokumenterne kunne ikke hentes.</p>
                <button type="button" className="btn btn-secondary" onClick={() => listQuery.refetch()}>Prøv igen</button>
              </div>
            )}

            {!listQuery.isLoading && !listQuery.isError && items.length === 0 && (
              <div className="docs-sidebar-state">
                <BookOpen size={28} aria-hidden="true" />
                <p>{debouncedSearch ? 'Ingen dokumenter matcher søgningen.' : 'Der er ingen dokumenter endnu.'}</p>
                {!debouncedSearch && canEdit && (
                  <div className="docs-suggestions">
                    <span className="docs-suggestions-label">Forslag til mapper:</span>
                    <div className="docs-suggestions-grid">
                       {SUGGESTED_CATEGORIES.map((category) => (
                        <button
                          key={category}
                          type="button"
                          className="docs-suggestion-item"
                          onClick={() => {
                            navigate(`/app/docs/new?title=${encodeURIComponent(category)}`);
                            // Note: we'd need to handle the query param in DocsPage to pre-fill the title
                          }}
                        >
                          <span className="docs-suggestion-icon">📁</span>
                          <span className="docs-suggestion-text">{category}</span>
                        </button>
                      ))}
                    </div>
                  </div>
                )}
                {!debouncedSearch && canEdit && (
                  <button type="button" className="btn btn-primary" onClick={startNew} style={{ marginTop: '1rem' }}>Opret eget dokument</button>
                )}
              </div>
            )}

            {items.map((document) => (
              <button
                type="button"
                role="listitem"
                key={document.id}
                className={`docs-list-item ${selectedId === document.id ? 'is-active' : ''}`}
                onClick={() => selectDocument(document.id)}
                aria-current={selectedId === document.id ? 'page' : undefined}
              >
                <strong>{document.title}</strong>
                <span className="docs-list-preview">{document.preview || 'Intet indhold endnu'}</span>
                <span className="docs-list-footer">
                  <Clock3 size={13} aria-hidden="true" />
                  {formatUpdatedAt(document.updatedAt)}
                  {(document.tags?.length ?? 0) > 0 && <span>· {document.tags!.slice(0, 2).join(', ')}</span>}
                </span>
              </button>
            ))}

            {listQuery.hasNextPage && (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => void listQuery.fetchNextPage()}
                disabled={listQuery.isFetchingNextPage}
              >
                {listQuery.isFetchingNextPage ? 'Henter flere…' : `Vis flere (${remainingCount})`}
              </button>
            )}
          </div>
        </aside>

        <main className="docs-workspace">
          {!showWorkspace && (
            <div className="docs-welcome">
              <div className="docs-welcome-icon"><BookOpen size={34} aria-hidden="true" /></div>
               <span className="docs-eyebrow">Workslip Dokumenter</span>
              <h2>Viden, der ikke skal genopfindes.</h2>
              <p>Vælg et dokument til venstre, eller opret et nyt. Tekniske sandheder hører fortsat hjemme i repository-dokumentationen.</p>
              {canEdit && <button type="button" className="btn btn-primary" onClick={startNew}><FilePlus2 size={17} aria-hidden="true" /> Nyt dokument</button>}
            </div>
          )}

          {showWorkspace && !isCreating && detailQuery.isLoading && (
            <div className="docs-document-loading" aria-label="Henter dokument">
              <div className="skeleton skeleton-title" />
              <div className="docs-loading-line" />
              <div className="docs-loading-line" />
              <div className="docs-loading-line is-short" />
            </div>
          )}

          {showWorkspace && !isCreating && detailQuery.isError && (
            <ErrorState message="Dokumentet kunne ikke hentes.">
              <button type="button" className="btn btn-secondary" onClick={() => navigate('/app/docs')}>Til dokumentlisten</button>
            </ErrorState>
          )}

          {(isCreating || selectedDocument) && (
            <article className="docs-document">
              <header className="docs-document-header">
                <button type="button" className="docs-mobile-back" onClick={() => leaveCurrentDocument('/app/docs')} aria-label="Tilbage til dokumenter">
                  <ArrowLeft size={18} aria-hidden="true" />
                </button>
                <div className="docs-document-heading">
                  <span className="docs-eyebrow">{isCreating ? 'Nyt dokument' : 'Internt dokument'}</span>
                  {!isEditing && selectedDocument && <h2>{selectedDocument.title}</h2>}
                </div>
                <div className="docs-document-actions">
                  {!isEditing && canEdit && selectedDocument && (
                    <button type="button" className="btn btn-secondary" onClick={() => setEditingDocumentId(selectedId)}>
                      <Pencil size={16} aria-hidden="true" /> Rediger
                    </button>
                  )}
                  {isEditing && (
                    <>
                      <button type="button" className="btn btn-secondary" onClick={cancelEdit}>Annuller</button>
                      <button type="button" className="btn btn-primary" onClick={submit} disabled={!canSave}>
                        <Check size={16} aria-hidden="true" /> {createMutation.isPending || updateMutation.isPending ? 'Gemmer…' : 'Gem'}
                      </button>
                    </>
                  )}
                </div>
              </header>

              {isEditing ? (
                <div className="docs-editor">
                  <label className="docs-field docs-title-field">
                    <span>Titel</span>
                    <input
                      autoFocus={isCreating}
                      value={draft.title}
                      onChange={(event) => updateDraft((current) => ({ ...current, title: event.target.value }))}
                      maxLength={200}
                      placeholder="Fx Onboarding af nye medarbejdere"
                    />
                  </label>

                  <label className="docs-field">
                    <span>Tags <small>kommasepareret, maks. 10</small></span>
                    <div className="docs-tags-input">
                      <Tag size={16} aria-hidden="true" />
                      <input
                        value={draft.tagsText}
                        onChange={(event) => updateDraft((current) => ({ ...current, tagsText: event.target.value }))}
                        placeholder="Onboarding, Drift, Produkt"
                      />
                    </div>
                    {hasInvalidTags && <small className="docs-field-error">Maks. 10 tags og 40 tegn pr. tag.</small>}
                  </label>

                  <label className="docs-field docs-content-field">
                    <span>Indhold <small>{formatNumber(draft.content.length)} / 200.000</small></span>
                    <textarea
                      value={draft.content}
                      onChange={(event) => updateDraft((current) => ({ ...current, content: event.target.value }))}
                      maxLength={200_000}
                      placeholder="Skriv den viden, teamet skal kunne finde igen…"
                    />
                  </label>

                  {isDirty && <div className="docs-unsaved" role="status">Ikke-gemte ændringer</div>}
                </div>
              ) : selectedDocument ? (
                <div className="docs-reader">
                  {(selectedDocument.tags?.length ?? 0) > 0 && (
                    <div className="docs-tags" aria-label="Tags">
                      {selectedDocument.tags!.map((tag) => <span key={tag}>{tag}</span>)}
                    </div>
                  )}
                  <div className="docs-content">
                    {selectedDocument.content || <span className="docs-empty-content">Dokumentet har endnu ikke noget indhold.</span>}
                  </div>
                  <footer className="docs-document-meta">
                    <span>Opdateret {formatUpdatedAt(selectedDocument.updatedAt)}</span>
                    {selectedDocument.updatedByDisplayName && <span>af {selectedDocument.updatedByDisplayName}</span>}
                    <span>Revision {selectedDocument.revision}</span>
                    {canEdit && (
                      <button type="button" className="docs-delete-button" onClick={() => setDeleteDialogOpen(true)} disabled={deleteMutation.isPending}>
                        <Trash2 size={15} aria-hidden="true" /> {deleteMutation.isPending ? 'Sletter…' : 'Slet dokument'}
                      </button>
                    )}
                  </footer>
                </div>
              ) : null}

              {selectedDocument && (
                <DocumentAttachments documentId={selectedDocument.id} canEdit={canEdit} />
              )}
            </article>
          )}
        </main>
      </div>

      <ConfirmDialog
        open={Boolean(pendingNavigationPath)}
        title="Forlad uden at gemme?"
        message="Du har ændringer, der ikke er gemt. Hvis du fortsætter, går ændringerne tabt."
        confirmLabel="Forlad"
        variant="danger"
        onConfirm={() => {
          if (pendingNavigationPath) completeLeave(pendingNavigationPath);
        }}
        onClose={() => setPendingNavigationPath(null)}
      />

      <ConfirmDeleteDialog
        open={deleteDialogOpen}
        title="Slet dokument"
        message="Dokumentet slettes permanent. Handlingen kan ikke fortrydes."
        onConfirm={() => deleteMutation.mutateAsync()}
        onClose={() => setDeleteDialogOpen(false)}
      />
    </>
  );
};
