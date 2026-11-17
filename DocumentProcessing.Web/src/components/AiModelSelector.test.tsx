import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiModelSelector } from './AiModelSelector';
import type { AiModelSettings } from '../api/aiSettings';

const settings = (selected: 'Cloud' | 'Local', localEnabled = true, localAvailable: boolean | null = true): AiModelSettings => ({
  selectedModelType: selected,
  defaultModelType: 'Cloud',
  fallbackMode: 'None',
  options: [
    { modelType: 'Cloud', enabled: true, provider: 'OpenAI', modelName: 'gpt-4o-mini', disabledReason: null, available: true, availabilityMessage: 'ok' },
    {
      modelType: 'Local', enabled: localEnabled, provider: 'OpenAI', modelName: 'llama3.1', disabledReason: localEnabled ? null : 'Set Ai:Local:Provider to enable it.',
      available: localAvailable, availabilityMessage: localAvailable === false ? "Model 'llama3.1' is not installed on the local runtime." : 'ok',
    },
  ],
});

const respond = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status }));

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('AiModelSelector', () => {
  it('shows the saved choice and saves a new one', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockImplementationOnce(() => respond(settings('Cloud')))
      .mockImplementationOnce(() => respond(settings('Local')));
    render(<AiModelSelector />);

    const select = await screen.findByLabelText('AI model for new uploads');
    expect(select).toHaveProperty('value', 'Cloud');

    await userEvent.selectOptions(select, 'Local');

    await waitFor(() => expect(select).toHaveProperty('value', 'Local'));
    const [url, init] = fetchMock.mock.calls[1];
    expect(url).toBe('/api/settings/ai');
    expect(init?.method).toBe('PUT');
    expect(init?.body).toBe(JSON.stringify({ modelType: 'Local' }));
  });

  it('disables a model that is not configured', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementationOnce(() => respond(settings('Cloud', false)));
    render(<AiModelSelector />);

    const option = await screen.findByRole('option', { name: /Local model \(llama3.1\) – not configured/ });
    expect(option).toHaveProperty('disabled', true);
  });

  it('warns when the selected model is unavailable', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementationOnce(() => respond(settings('Local', true, false)));
    render(<AiModelSelector />);

    expect(await screen.findByRole('status')).toHaveProperty('textContent', expect.stringContaining("Model 'llama3.1' is not installed"));
  });

  it('shows the server error and keeps the previous choice when saving fails', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockImplementationOnce(() => respond(settings('Cloud')))
      .mockImplementationOnce(() => respond({ message: 'The Local model is not available.' }, 400));
    render(<AiModelSelector />);

    const select = await screen.findByLabelText('AI model for new uploads');
    await userEvent.selectOptions(select, 'Local');

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'The Local model is not available.');
    expect(select).toHaveProperty('value', 'Cloud');
  });
});
