import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ReviewQueue } from './ReviewQueue';

const queue = (items: object[]) =>
  new Response(JSON.stringify(items), { status: 200, headers: { 'X-Total-Count': String(items.length) } });

describe('ReviewQueue', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('claims a document and opens it', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(queue([{ id: 'doc-1', originalFileName: 'acme.pdf', createdAtUtc: '2026-01-01T00:00:00Z' }]))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    render(<MemoryRouter initialEntries={['/queue']}><Routes>
      <Route path="/queue" element={<ReviewQueue />} />
      <Route path="/documents/:id" element={<p>detail page</p>} />
    </Routes></MemoryRouter>);

    await userEvent.click(await screen.findByRole('button', { name: 'Claim' }));

    expect(await screen.findByText('detail page')).toBeTruthy();
    expect(fetchMock).toHaveBeenLastCalledWith('/api/documents/doc-1/claim', expect.objectContaining({ method: 'POST' }));
  });

  it('explains when someone else claimed it first', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(queue([{ id: 'doc-1', originalFileName: 'acme.pdf', createdAtUtc: '2026-01-01T00:00:00Z' }]))
      .mockResolvedValueOnce(new Response(null, { status: 409 }))
      .mockResolvedValueOnce(queue([])));

    render(<MemoryRouter><ReviewQueue /></MemoryRouter>);
    await userEvent.click(await screen.findByRole('button', { name: 'Claim' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Another reviewer already claimed');
  });
});
