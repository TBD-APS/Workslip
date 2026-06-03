import type { ReactNode } from 'react';
import { MessageSquare } from 'lucide-react';

export function JobAttachmentsStep() {
  return (
    <PlaceholderStep
      icon={<MessageSquare size={18} />}
      title="Bilag"
      text="Bilag bygges på næste trin."
    />
  );
}

function PlaceholderStep({ icon, title, text }: { icon: ReactNode; title: string; text: string }) {
  return (
    <section className="detail-section">
      <div className="section-header-row">
        {icon}
        <h3>{title}</h3>
      </div>
      <p className="empty-state-text">{text}</p>
    </section>
  );
}
