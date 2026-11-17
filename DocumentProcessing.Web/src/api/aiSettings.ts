import { xsrfHeaders } from '../csrf';

export type AiModelType = 'Cloud' | 'Local';

export interface AiModelOption {
  modelType: AiModelType;
  enabled: boolean;
  provider: string;
  modelName: string;
  disabledReason: string | null;
  available: boolean | null;
  availabilityMessage: string | null;
}

export interface AiModelSettings {
  selectedModelType: AiModelType;
  defaultModelType: AiModelType;
  fallbackMode: 'None' | 'Automatic';
  options: AiModelOption[];
}

const errorMessage = async (response: Response, fallback: string) => {
  const body = await response.json().catch(() => null) as { message?: string } | null;
  return body?.message ?? fallback;
};

export const getAiSettings = async (check = false): Promise<AiModelSettings> => {
  const response = await fetch(`/api/settings/ai${check ? '?check=true' : ''}`, { credentials: 'same-origin' });
  if (!response.ok) throw new Error(await errorMessage(response, 'Could not load the AI model settings.'));
  return await response.json() as AiModelSettings;
};

export const saveAiModelType = async (modelType: AiModelType): Promise<AiModelSettings> => {
  const response = await fetch('/api/settings/ai', {
    method: 'PUT',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...xsrfHeaders() },
    body: JSON.stringify({ modelType }),
  });
  if (!response.ok) throw new Error(await errorMessage(response, 'Could not save the AI model choice.'));
  return await response.json() as AiModelSettings;
};

export const retryWithModel = async (documentId: string, modelType: AiModelType): Promise<void> => {
  const response = await fetch(`/api/documents/${documentId}/retry-with-model`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...xsrfHeaders() },
    body: JSON.stringify({ modelType }),
  });
  if (!response.ok) throw new Error(await errorMessage(response, `Could not retry with the ${modelType} model.`));
};

export const modelLabel = (option: Pick<AiModelOption, 'modelType' | 'modelName'>) =>
  `${option.modelType === 'Cloud' ? 'Cloud' : 'Local'} model${option.modelName ? ` (${option.modelName})` : ''}`;
