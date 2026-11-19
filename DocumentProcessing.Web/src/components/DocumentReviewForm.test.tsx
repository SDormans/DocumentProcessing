import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DocumentReviewForm } from './DocumentReviewForm';
import { ReviewActionError, type ReviewField } from '../hooks/useDocumentReview';

const field = (fieldName: string, value: string | null, correctedValue: string | null = null): ReviewField => ({
  fieldName,
  value,
  correctedValue,
  approvedValue: null,
  effectiveValue: correctedValue ?? value,
  isCorrected: correctedValue !== null && correctedValue !== value,
});

const fields = [
  field('SupplierName', 'Acme B.V.'),
  field('InvoiceNumber', 'INV-1', 'INV-1-A'),
  field('TotalAmount', null),
  field('Lines', 'Consulting'),
];

afterEach(cleanup);

describe('DocumentReviewForm', () => {
  it('shows every field with its effective value and the extracted value of corrected fields', () => {
    render(<DocumentReviewForm fields={fields} validationResults={[]} readOnly={false} onSubmit={vi.fn()} />);

    expect(screen.getByLabelText('Supplier')).toHaveProperty('value', 'Acme B.V.');
    expect(screen.getByLabelText('Invoice number')).toHaveProperty('value', 'INV-1-A');
    expect(screen.getByLabelText('Total')).toHaveProperty('value', '');
    expect(screen.getByLabelText('Invoice lines').tagName).toBe('TEXTAREA');
    expect(screen.getByText('Extracted: INV-1')).toBeTruthy();
  });

  it('shows validation messages next to the affected field', () => {
    render(
      <DocumentReviewForm
        fields={fields}
        validationResults={[
          { ruleId: 'MISSING_REQUIRED_FIELDS', severity: 'Error', message: "Field 'TotalAmount' is required", affectedFields: 'TotalAmount' },
          { ruleId: 'OTHER', severity: 'Warning', message: 'General warning', affectedFields: null },
        ]}
        readOnly={false}
        onSubmit={vi.fn()}
      />);

    expect(screen.getByLabelText('Total').getAttribute('aria-invalid')).toBe('true');
    expect(screen.getByText("Field 'TotalAmount' is required")).toBeTruthy();
    expect(screen.getByText('General warning')).toBeTruthy();
  });

  it('submits only the changed fields', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<DocumentReviewForm fields={fields} validationResults={[]} readOnly={false} onSubmit={onSubmit} />);
    const save = screen.getByRole('button', { name: 'Save corrections' });
    expect(save).toHaveProperty('disabled', true);

    await userEvent.type(screen.getByLabelText('Total'), '121.00');
    await userEvent.click(save);

    expect(onSubmit).toHaveBeenCalledWith({ TotalAmount: '121.00' });
    expect(await screen.findByText('Corrections saved.')).toBeTruthy();
  });

  it('shows the server validation errors when saving fails', async () => {
    const onSubmit = vi.fn().mockRejectedValue(
      new ReviewActionError('De correcties zijn ongeldig.', ["Field 'TotalAmount' must be a number such as 1234.56."]));
    render(<DocumentReviewForm fields={fields} validationResults={[]} readOnly={false} onSubmit={onSubmit} />);

    await userEvent.type(screen.getByLabelText('Total'), 'abc');
    await userEvent.click(screen.getByRole('button', { name: 'Save corrections' }));

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByText("Field 'TotalAmount' must be a number such as 1234.56.")).toBeTruthy();
  });

  it('is read-only once the review is decided', () => {
    render(<DocumentReviewForm fields={fields} validationResults={[]} readOnly onSubmit={vi.fn()} />);

    expect(screen.getByLabelText('Supplier')).toHaveProperty('disabled', true);
    expect(screen.queryByRole('button', { name: 'Save corrections' })).toBeNull();
  });
});
