import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { NotFoundPage } from './NotFoundPage';

afterEach(cleanup);

describe('NotFoundPage', () => {
  it('offers a working recovery action without a dead retry button', async () => {
    render(
      <MemoryRouter initialEntries={['/missing']}>
        <Routes>
          <Route path="/app" element={<h1>Forside</h1>} />
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', { name: 'Siden blev ikke fundet' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Prøv igen' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Gå til forsiden' }));

    expect(await screen.findByRole('heading', { name: 'Forside' })).toBeInTheDocument();
  });
});
