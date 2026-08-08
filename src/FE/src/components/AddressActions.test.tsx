import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AddressActions } from './AddressActions';
import { getAddressMapsUrl } from './addressActionsUtils';

describe('AddressActions', () => {
  it('uses the same normalized address for Google Maps', () => {
    render(<AddressActions address="  Vesterbrogade 100, 1620 København V  " />);

    const mapsLink = screen.getByRole('link', { name: 'Åbn adresse i Google Maps' });
    expect(mapsLink).toHaveAttribute(
      'href',
      getAddressMapsUrl('Vesterbrogade 100, 1620 København V'),
    );
    expect(mapsLink).toHaveAttribute('target', '_blank');
  });

  it('does not trigger a clickable parent when Maps is opened', () => {
    const onParentClick = vi.fn();
    render(
      <div onClick={onParentClick}>
        <AddressActions address="Nørrebrogade 1" />
      </div>,
    );

    fireEvent.click(screen.getByRole('link', { name: 'Åbn adresse i Google Maps' }));
    expect(onParentClick).not.toHaveBeenCalled();
  });
});
