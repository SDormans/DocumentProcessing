// @ts-nocheck
import React from 'react';

interface ApproveRejectButtonsProps {
  documentId: string;
  onApprove: (request?: any) => Promise<void>;
  onReject: (request: any) => Promise<void>;
  canApprove: boolean;
  canReject: boolean;
}

export const ApproveRejectButtons: React.FC<ApproveRejectButtonsProps> = ({ 
  documentId, 
  onApprove, 
  onReject, 
  canApprove, 
  canReject 
}) => {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleApprove = async () => {
    if (!canApprove) return;
    setSubmitting(true);
    setError(null);
    
    try {
      await onApprove();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
    } finally {
      setSubmitting(false);
    }
  };

  const handleReject = async () => {
    if (!canReject) return;
    setSubmitting(true);
    setError(null);
    
    try {
      await onReject({ reason: 'Gebruiker weigert goedkeuring' });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
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
          {submitting ? 'Bezig...' : 'Goedgekeurd'}
        </button>
      )}
      {canReject && (
        <button 
          onClick={handleReject} 
          disabled={submitting || !canReject}
          className="btn-reject"
        >
          {submitting ? 'Bezig...' : 'Afgewezen'}
        </button>
      )}
      {error && <p className="error">Fout: {error}</p>}
    </div>
  );
};