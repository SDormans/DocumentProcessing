import React, { useEffect, useState } from 'react';
import { type AiModelOption, type AiModelType, getAiSettings, modelLabel, retryWithModel } from '../api/aiSettings';

interface Props {
  documentId: string;
  failureReason: string;
  modelType: string;
  onRetried: () => void;
}

/** Explains why processing failed and offers to retry the document with each enabled model. */
export const ModelFailureNotice: React.FC<Props> = ({ documentId, failureReason, modelType, onRetried }) => {
  const [options, setOptions] = useState<AiModelOption[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    getAiSettings().then((s) => setOptions(s.options.filter((o) => o.enabled))).catch(() => setOptions([]));
  }, []);

  const retry = async (target: AiModelType) => {
    setBusy(true);
    setError(null);
    try {
      await retryWithModel(documentId, target);
      onRetried();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Retry failed.');
    } finally {
      setBusy(false);
    }
  };

  // The other model first: that is the useful choice when the chosen one is down.
  const ordered = [...options].sort((a, b) => Number(a.modelType === modelType) - Number(b.modelType === modelType));
  return (
    <div className="model-failure" role="alert">
      <p><strong>Processing failed with the {modelType} model.</strong> {failureReason}</p>
      <div className="model-failure-actions">
        {ordered.map((option) => (
          <button key={option.modelType} type="button" disabled={busy} onClick={() => retry(option.modelType)}>
            {option.modelType === modelType ? `Retry with the same model (${option.modelName})` : `Switch to ${modelLabel(option)} and retry`}
          </button>
        ))}
      </div>
      {error && <p className="error">{error}</p>}
    </div>
  );
};
