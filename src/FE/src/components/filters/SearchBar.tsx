import { Search, X } from 'lucide-react';

interface SearchBarProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  inputId?: string;
}

export const SearchBar = ({ value, onChange, placeholder = 'Søg...', inputId }: SearchBarProps) => (
  <div className="search-input-wrapper">
    <Search size={16} className="search-input-icon" />
    <input
      id={inputId}
      type="text"
      className="search-input"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      aria-label={placeholder}
    />
    {value && (
      <button
        type="button"
        className="search-input-clear"
        onClick={() => onChange('')}
        aria-label="Ryd søgning"
      >
        <X size={16} />
      </button>
    )}
  </div>
);