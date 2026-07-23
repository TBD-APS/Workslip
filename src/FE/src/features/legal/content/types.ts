export interface LegalSection {
  heading: string;
  content: string[];
}

export interface LegalContent {
  title: string;
  lastUpdated: string;
  sections: LegalSection[];
}

export type LegalType = 'terms' | 'privacy' | 'cookies';
