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

  const handleMouseDown = useCallback((index: number, e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const th = (e.currentTarget as HTMLElement).closest('th');
    if (!th) return;

    const startWidth = th.offsetWidth;
    resizing.current = { index, startX: e.clientX, startWidth, element: th };

    const handleMouseMove = (e: MouseEvent) => {
      if (!resizing.current) return;
      const { startX, startWidth, element } = resizing.current;
      const newWidth = Math.max(MIN_WIDTH, startWidth + (e.clientX - startX));
      element.style.width = `${newWidth}px`;
    };

    const handleMouseUp = () => {
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
