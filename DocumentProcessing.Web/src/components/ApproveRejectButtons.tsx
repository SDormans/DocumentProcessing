import React, { useState } from 'react';

interface ApproveRejectButtonsProps {
  onApprove: () => Promise<void>;
  onReject: (reason: string) => Promise<void>;
  canApprove: boolean;
  canReject: boolean;
}

export const ApproveRejectButtons: React.FC<ApproveRejectButtonsProps> = ({ 
  onApprove, 
  onReject, 
  canApprove, 
  canReject 
}) => {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rejectionReason, setRejectionReason] = useState('');

  const handleApprove = async () => {
    if (!canApprove) return;
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
      setError('Enter a reason before rejecting this document.');
      return;
    }
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
      {canApprove && (
        <button 
          onClick={handleApprove} 
          disabled={submitting}
          className="btn-approve"
        >
          {submitting ? 'Saving...' : 'Approve'}
        </button>
      )}
      {canReject && (
        <>
          <label htmlFor="rejection-reason">Rejection reason</label>
          <input id="rejection-reason" required value={rejectionReason}
            onChange={event => setRejectionReason(event.target.value)} />
          <button onClick={handleReject} disabled={submitting} className="btn-reject">
            {submitting ? 'Saving...' : 'Reject'}
          </button>
        </>
      )}
      {error && <p className="error">Error: {error}</p>}
    </div>
  );
};