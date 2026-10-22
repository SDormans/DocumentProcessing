import React, { useState } from 'react';

interface DocumentReviewFormProps {
  fieldName: string;
  currentValue: string | number | null;
  onSubmit: (correctedValue: string) => Promise<void>;
  isLoading: boolean;
}

export const DocumentReviewForm: React.FC<DocumentReviewFormProps> = ({ 
  fieldName, 
  currentValue, 
  onSubmit, 
  isLoading 
}) => {
  const [value, setValue] = useState<string | number | ''>(String(currentValue ?? ''));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setValue(e.target.value);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    
    try {
      await onSubmit(String(value));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="review-form">
      <h3>Correctie voor {fieldName}</h3>
      <p>Huidige waarde: {String(currentValue ?? 'N/A')}</p>
      <form onSubmit={handleSubmit}>
        <input
          type="text"
          value={String(value)}
          onChange={handleChange}
          placeholder="Nieuwe waarde invoeren"
          required
          disabled={isLoading}
        />
        <button type="submit" disabled={isLoading || submitting}>
          {isLoading ? 'Bezig...' : submitting ? 'Opslaan...' : 'Opslaan'}
        </button>
      </form>
      {error && <p className="error">Fout: {error}</p>}
    </div>
  );
};