import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Clock } from 'lucide-react';
import { DOCUMENT_TYPES } from '../documentTypes';
import { Can } from '../../../providers/permissions';

/**
 * Generic document-type picker.
 *
 * Backed by `DOCUMENT_TYPES`. To add a new create flow: add an entry to
 * `documentTypes.ts` with `status: 'available'` and a target `path`. No
 * changes to this file required.
 */
export const Create = () => {
  const navigate = useNavigate();

  useEffect(() => {
    document.querySelector('.app-shell')?.scrollTo({ top: 0, behavior: 'smooth' });
  }, []);

  return (
    <div className="page-container">
      <div className="detail-header">
        <button
          className="btn-icon"
          onClick={() => navigate('/app')}
          aria-label="Tilbage"
        >
          <ArrowLeft size={22} />
        </button>
        <div>
          <h2 className="detail-title">Opret</h2>
          <p className="detail-subtitle">Vælg hvad du vil oprette</p>
        </div>
      </div>

      <ul className="create-type-list" role="list">
        {DOCUMENT_TYPES.map((type) => {
          const Icon = type.icon;
          const disabled = type.status !== 'available';
          const labelId = `create-type-${type.id}-label`;
          const descId = `create-type-${type.id}-desc`;
          const content = (
            <>
              <span className="create-type-icon" aria-hidden="true">
                <Icon size={24} />
              </span>
              <span className="create-type-body">
                <span id={labelId} className="create-type-label">
                  {type.label}
                </span>
                <span id={descId} className="create-type-description">
                  {type.description}
                </span>
              </span>
              {disabled ? (
                <span className="create-type-badge">
                  <Clock size={14} />
                  Kommer snart
                </span>
              ) : (
                <span className="create-type-chevron" aria-hidden="true">
                  ›
                </span>
              )}
            </>
          );

          const tile = disabled ? (
            <div
              className="create-type-tile create-type-tile--disabled"
              aria-disabled="true"
              aria-labelledby={labelId}
              aria-describedby={descId}
            >
              {content}
            </div>
          ) : (
            <button
              type="button"
              className="create-type-tile"
              onClick={() => navigate(type.path)}
              aria-labelledby={labelId}
              aria-describedby={descId}
            >
              {content}
            </button>
          );

          if (type.permission) {
            return (
              <li key={type.id} className="create-type-item">
                <Can permission={type.permission}>
                  {tile}
                </Can>
              </li>
            );
          }

          return (
            <li key={type.id} className="create-type-item">
              {tile}
            </li>
          );
        })}
      </ul>
    </div>
  );
};
