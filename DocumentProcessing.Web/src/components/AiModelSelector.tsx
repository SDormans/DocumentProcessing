import React, { useEffect, useState } from 'react';
import { type AiModelSettings, type AiModelType, getAiSettings, modelLabel, saveAiModelType } from '../api/aiSettings';

/** Lets the user choose whether new uploads are extracted by the cloud or the local model. Saved on the server per user. */
export const AiModelSelector: React.FC = () => {
  const [settings, setSettings] = useState<AiModelSettings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    getAiSettings(true)
      .then(setSettings)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Could not load the AI model settings.'));
  }, []);

  const onChange = async (event: React.ChangeEvent<HTMLSelectElement>) => {
    const modelType = event.target.value as AiModelType;
    setSaving(true);
    setError(null);
    try {
      const saved = await saveAiModelType(modelType);
      // Keep the availability we already probed; the save response does not re-check.
      setSettings((current) => ({
        ...saved,
        options: saved.options.map((o) => ({ ...o, ...pickAvailability(current, o.modelType) })),
      }));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the AI model choice.');
    } finally {
      setSaving(false);
    }
  };

  if (!settings) return error ? <p className="error" role="alert">{error}</p> : null;

  const selected = settings.options.find((o) => o.modelType === settings.selectedModelType);
  return (
    <div className="ai-model-selector">
      <label htmlFor="ai-model-type">AI model for new uploads</label>
      <select id="ai-model-type" value={settings.selectedModelType} onChange={onChange} disabled={saving}>
        {settings.options.map((option) => (
          <option key={option.modelType} value={option.modelType} disabled={!option.enabled}>
            {modelLabel(option)}{option.enabled ? '' : ' – not configured'}
          </option>
        ))}
      </select>
      {selected?.available === false && (
        <p className="warning" role="status">
          {selected.availabilityMessage} Uploads will fail until it is available
          {settings.fallbackMode === 'Automatic' ? ', unless the other model can take over.' : '; you can retry failed documents with the other model.'}
        </p>
      )}
      {error && <p className="error" role="alert">{error}</p>}
    </div>
  );
};

const pickAvailability = (settings: AiModelSettings | null, modelType: AiModelType) => {
  const option = settings?.options.find((o) => o.modelType === modelType);
  return option ? { available: option.available, availabilityMessage: option.availabilityMessage } : {};
};
