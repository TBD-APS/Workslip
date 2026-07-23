import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { termsContent } from '../content/terms';
import { privacyContent } from '../content/privacy';
import { cookiesContent } from '../content/cookies';
import type { LegalContent, LegalType } from '../content/types';

const contentMap: Record<LegalType, LegalContent> = {
  terms: termsContent,
  privacy: privacyContent,
  cookies: cookiesContent,
};

export const LegalPage = () => {
  const { type } = useParams<{ type: string }>();
  const navigate = useNavigate();
  const content = contentMap[type as LegalType];

  if (!content) {
    return (
      <div className="page-container">
        <div className="page-header">
          <h2>Siden blev ikke fundet</h2>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container">
      <button
        type="button"
        className="btn-icon"
        onClick={() => navigate('/app/settings')}
        aria-label="Tilbage til indstillinger"
        style={{ marginBottom: '1rem' }}
      >
        <ArrowLeft size={20} />
      </button>

      <div className="page-header">
        <h2>{content.title}</h2>
        <p className="subtitle">Senest opdateret: {content.lastUpdated}</p>
      </div>

      {content.sections.map((section) => (
        <div key={section.heading} className="section-card" style={{ marginBottom: '1rem' }}>
          <h3 className="section-card-title">{section.heading}</h3>
          {section.content.map((paragraph, i) => (
            <p key={i} style={{ marginBottom: '0.75rem', color: 'var(--text-primary)' }}>
              {paragraph}
            </p>
          ))}
        </div>
      ))}
    </div>
  );
};
