import { useCallback, useRef } from 'react';

const MIN_WIDTH = 40;

export function useColumnResize() {
  const columnWidths = useRef<Record<number, number>>({});
  const resizing = useRef<{
    index: number;
    startX: number;
    startWidth: number;
    element: HTMLElement;
  } | null>(null);

  function setColWidth(el: HTMLElement, w: number) {
    const px = `${w}px`;
    el.style.setProperty('min-width', px, 'important');
    el.style.setProperty('width', px, 'important');
    el.style.setProperty('max-width', px, 'important');
  }

  const handleMouseDown = useCallback((index: number, e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const th = (e.currentTarget as HTMLElement).closest('th');
    if (!th) return;

    const el = e.currentTarget as HTMLElement;

    const preventClick = (ce: MouseEvent) => {
      ce.stopPropagation();
    };
    el.addEventListener('click', preventClick, true);

    const startWidth = th.getBoundingClientRect().width;
    setColWidth(th, startWidth);
    resizing.current = { index, startX: e.clientX, startWidth, element: th };

    const handleMouseMove = (e: MouseEvent) => {
      if (!resizing.current) return;
      const { startX, startWidth, element } = resizing.current;
      const newWidth = Math.max(MIN_WIDTH, startWidth + (e.clientX - startX));
      setColWidth(element, newWidth);
    };

    const handleMouseUp = () => {
      el.removeEventListener('click', preventClick, true);
      if (resizing.current) {
        columnWidths.current[resizing.current.index] = resizing.current.element.offsetWidth;
      }
      resizing.current = null;
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };

    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  }, []);

  return { handleMouseDown };
}
