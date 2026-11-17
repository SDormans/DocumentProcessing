import React, { useState } from 'react';

interface ApproveRejectButtonsProps {
  onApprove: () => Promise<void>;
  onReject: (reason: string) => Promise<void>;
  canApprove: boolean;
  canReject: boolean;
  approveHint?: string;
}

export const ApproveRejectButtons: React.FC<ApproveRejectButtonsProps> = ({ 
  onApprove, 
  onReject, 
  canApprove,
  canReject,
  approveHint,
}) => {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rejectionReason, setRejectionReason] = useState('');
  const [rejectionReasonError, setRejectionReasonError] = useState(false);

  const handleApprove = async () => {
    if (!canApprove) return;
    setRejectionReasonError(false);
    setSubmitting(true);
    setError(null);
    
    try {
      await onApprove();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleReject = async () => {
    if (!canReject) return;
    if (!rejectionReason.trim()) {
      setRejectionReasonError(true);
      setError('Enter a reason before rejecting this document.');
      return;
    }
    setRejectionReasonError(false);
    setSubmitting(true);
    setError(null);

    try {
      await onReject(rejectionReason.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="approve-reject-buttons">
      <button
        type="button"
        onClick={handleApprove}
        disabled={submitting || !canApprove}
        title={approveHint}
        className="btn-approve"
      >
        {submitting ? 'Saving...' : 'Approve'}
      </button>
      {approveHint && <small className="validation error">{approveHint}</small>}
      {canReject && (
        <>
          <label htmlFor="rejection-reason">Rejection reason</label>
          <input id="rejection-reason" maxLength={2000} aria-invalid={rejectionReasonError || undefined} value={rejectionReason}
            onChange={event => setRejectionReason(event.target.value)} />
          <button type="button" onClick={handleReject} disabled={submitting} className="btn-reject">
            {submitting ? 'Saving...' : 'Reject'}
          </button>
        </>
      )}
      {error && <p className="error">Error: {error}</p>}
    </div>
  );
};