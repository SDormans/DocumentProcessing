import React, { useEffect, useState } from 'react';
import type { ReviewField, ValidationResult } from '../hooks/useDocumentReview';
import { ReviewActionError } from '../hooks/useDocumentReview';

const labels: Record<string, string> = {
  SupplierName: 'Supplier',
  InvoiceNumber: 'Invoice number',
  InvoiceDate: 'Invoice date',
  DueDate: 'Due date',
  Currency: 'Currency',
  Subtotal: 'Subtotal',
  TaxAmount: 'Tax',
  TotalAmount: 'Total',
  Lines: 'Invoice lines',
};

const hints: Record<string, string> = {
  InvoiceDate: 'yyyy-MM-dd',
  DueDate: 'yyyy-MM-dd',
  Currency: 'e.g. EUR',
  Subtotal: 'e.g. 100.00',
  TaxAmount: 'e.g. 21.00',
  TotalAmount: 'e.g. 121.00',
  Lines: 'One line per row',
};

export const fieldLabel = (fieldName: string) => labels[fieldName] ?? fieldName;

const affects = (result: ValidationResult, fieldName: string) =>
  (result.affectedFields ?? '').split(',').map(f => f.trim()).includes(fieldName);

interface DocumentReviewFormProps {
  fields: ReviewField[];
  validationResults: ValidationResult[];
  readOnly: boolean;
  onSubmit: (correctedValues: Record<string, string>) => Promise<void>;
}

export const DocumentReviewForm: React.FC<DocumentReviewFormProps> = ({ fields, validationResults, readOnly, onSubmit }) => {
  const [values, setValues] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ReviewActionError | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setValues(Object.fromEntries(fields.map(f => [f.fieldName, f.effectiveValue ?? ''])));
  }, [fields]);

  const changed = Object.fromEntries(
    fields
      .filter(f => (values[f.fieldName] ?? '') !== (f.effectiveValue ?? ''))
      .map(f => [f.fieldName, values[f.fieldName] ?? '']));
  const hasChanges = Object.keys(changed).length > 0;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!hasChanges) return;
    setSubmitting(true);
    setError(null);
    setSaved(false);
    try {
      await onSubmit(changed);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ReviewActionError ? err : new ReviewActionError(err instanceof Error ? err.message : 'Unknown error.'));
    } finally {
      setSubmitting(false);
    }
  };

  const generalResults = validationResults.filter(r => !fields.some(f => affects(r, f.fieldName)));

  return (
    <form className="review-form" onSubmit={handleSubmit} aria-label="Extracted fields">
      {fields.map(field => {
        const id = `field-${field.fieldName}`;
        const messages = validationResults.filter(r => affects(r, field.fieldName));
        const input = {
          id,
          name: field.fieldName,
          value: values[field.fieldName] ?? '',
          placeholder: hints[field.fieldName],
          disabled: readOnly || submitting,
          'aria-invalid': messages.some(m => m.severity === 'Error' || m.severity === 'Critical') || undefined,
          onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
            const value = e.target.value;
            setValues(previous => ({ ...previous, [field.fieldName]: value }));
            setSaved(false);
          },
        };
        return (
          <div className={`review-field${field.isCorrected ? ' corrected' : ''}`} key={field.fieldName}>
            <label htmlFor={id}>{fieldLabel(field.fieldName)}</label>
            {field.fieldName === 'Lines' ? <textarea rows={4} {...input} /> : <input type="text" {...input} />}
            {field.isCorrected && (
              <small className="original-value">Extracted: {field.value ?? '(empty)'}</small>
            )}
            {messages.map(m => (
              <small key={`${m.ruleId}-${m.message}`} className={`validation ${m.severity.toLowerCase()}`}>{m.message}</small>
            ))}
          </div>
        );
      })}
      {generalResults.length > 0 && (
        <ul className="validation-list">
          {generalResults.map(m => (
            <li key={`${m.ruleId}-${m.message}`} className={`validation ${m.severity.toLowerCase()}`}>{m.message}</li>
          ))}
        </ul>
      )}
      {!readOnly && (
        <button type="submit" disabled={!hasChanges || submitting}>
          {submitting ? 'Saving...' : 'Save corrections'}
        </button>
      )}
      {saved && <p className="success" role="status">Corrections saved.</p>}
      {error && (
        <div className="error" role="alert">
          <p>Error: {error.message}</p>
          {error.errors.length > 0 && <ul>{error.errors.map(e => <li key={e}>{e}</li>)}</ul>}
        </div>
      )}
    </form>
  );
};
