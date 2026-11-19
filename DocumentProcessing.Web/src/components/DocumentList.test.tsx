import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DocumentList, PAGE_SIZE } from './DocumentList';

const doc = (n: number) => ({
  id: `id-${n}`,
  originalFileName: `invoice-${n}.pdf`,
  status: 'Uploaded',
  createdAtUtc: '2026-01-01T00:00:00Z',
});

const page = (items: ReturnType<typeof doc>[], total: number) =>
  new Response(JSON.stringify(items), { status: 200, headers: { 'X-Total-Count': String(total) } });

describe('DocumentList', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('loads the next page when there are more documents', async () => {
    const first = Array.from({ length: PAGE_SIZE }, (_, i) => doc(i));
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(page(first, PAGE_SIZE + 1))
      .mockResolvedValueOnce(page([doc(PAGE_SIZE)], PAGE_SIZE + 1));
    vi.stubGlobal('fetch', fetchMock);

    render(<MemoryRouter><DocumentList /></MemoryRouter>);

    expect(await screen.findByText(`Page 1 of 2 (${PAGE_SIZE + 1} documents)`)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));

    expect(await screen.findByText(`invoice-${PAGE_SIZE}.pdf`)).toBeTruthy();
    expect(fetchMock).toHaveBeenLastCalledWith(`/api/documents?skip=${PAGE_SIZE}&take=${PAGE_SIZE}`, expect.anything());
    expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
