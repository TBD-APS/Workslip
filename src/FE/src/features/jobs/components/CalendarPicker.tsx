import { useEffect, useRef, useState } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react';
import { fromDateIso, toDateIso, formatDate } from './worksheetUtils';
import './CalendarPicker.css';

export function CalendarPicker({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const selectedDate = fromDateIso(value);
  const [isOpen, setIsOpen] = useState(false);
  const [visibleMonth, setVisibleMonth] = useState(() => new Date(selectedDate.getFullYear(), selectedDate.getMonth(), 1));
  const pickerRef = useRef<HTMLDivElement | null>(null);
  const monthLabel = formatDate(visibleMonth.toDateString());
  const firstDay = new Date(visibleMonth.getFullYear(), visibleMonth.getMonth(), 1);
  const startOffset = (firstDay.getDay() + 6) % 7;
  const daysInMonth = new Date(visibleMonth.getFullYear(), visibleMonth.getMonth() + 1, 0).getDate();
  const days = Array.from({ length: startOffset + daysInMonth }, (_, index) => index < startOffset ? null : index - startOffset + 1);

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (!pickerRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen]);

  const moveMonth = (offset: number) => {
    setVisibleMonth((current) => new Date(current.getFullYear(), current.getMonth() + offset, 1));
  };

  const selectDay = (day: number) => {
    const nextDate = new Date(visibleMonth.getFullYear(), visibleMonth.getMonth(), day);
    onChange(toDateIso(nextDate));
    setIsOpen(false);
  };

  return (
    <div className="form-group calendar-picker-field" ref={pickerRef}>
      <label className="form-label">Dato</label>
      <button
        type="button"
        className="form-input calendar-picker-trigger"
        onClick={() => setIsOpen((open) => !open)}
        aria-expanded={isOpen}
        aria-haspopup="dialog"
      >
        <span>{formatDate(value)}</span>
        <CalendarDays size={16} aria-hidden="true" />
      </button>

      {isOpen && (
        <div className="calendar-picker-popover" role="dialog" aria-label={`Vælg dato i ${monthLabel}`}>
          <div className="calendar-picker-header">
            <button type="button" className="btn-icon" onClick={() => moveMonth(-1)} aria-label="Forrige måned">
              <ChevronLeft size={16} aria-hidden="true" />
            </button>
            <span>{monthLabel}</span>
            <button type="button" className="btn-icon" onClick={() => moveMonth(1)} aria-label="Næste måned">
              <ChevronRight size={16} aria-hidden="true" />
            </button>
          </div>
          <div className="calendar-picker-weekdays" aria-hidden="true">
            {['ma', 'ti', 'on', 'to', 'fr', 'lø', 'sø'].map((day) => <span key={day}>{day}</span>)}
          </div>
          <div className="calendar-picker-grid">
            {days.map((day, index) => {
              if (!day) return <span key={`blank-${index}`} aria-hidden="true" />;
              const dayIso = toDateIso(new Date(visibleMonth.getFullYear(), visibleMonth.getMonth(), day));
              const isSelected = dayIso === value;
              return (
                <button
                  key={dayIso}
                  type="button"
                  className={isSelected ? 'calendar-picker-day selected' : 'calendar-picker-day'}
                  onClick={() => selectDay(day)}
                  aria-pressed={isSelected}
                >
                  {day}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
