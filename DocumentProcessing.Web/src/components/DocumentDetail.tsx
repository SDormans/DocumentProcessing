import React, { useState, useEffect } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { DocumentReviewForm } from './DocumentReviewForm';
import { ApproveRejectButtons } from './ApproveRejectButtons';
import { useDocumentReview } from '../hooks/useDocumentReview';

interface DocumentSummary {
  originalFileName: string;
  status: string;
  createdAtUtc: string;
}

export const DocumentDetail: React.FC = () => {
  const { documentId } = useParams<{ documentId: string }>();
  const navigate = useNavigate();
  const [document, setDocument] = useState<DocumentSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { reviewState, submitCorrections, approve, reject, isLoading, reviewError } =
    useDocumentReview(documentId ?? '');

  useEffect(() => {
    if (!documentId) {
      setError('Document ID is missing.');
      setLoading(false);
      return;
    }
    fetch(`/api/documents/${documentId}`)
      .then(async (response) => {
        if (!response.ok) throw new Error('Could not load the document.');
        const data = await response.json() as DocumentSummary;
        setDocument(data);
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : 'Could not load the document.');
      })
      .finally(() => setLoading(false));
  }, [documentId]);

  if (loading) return <p>Loading document details...</p>;
  if (error) return <p className="error">Error: {error}</p>;
  if (!document) return null;

  return (
    <div className="document-detail">
      <h2>{document.originalFileName}</h2>
      <p>Status: {document.status}</p>
      <p>Created: {new Date(document.createdAtUtc).toLocaleDateString()}</p>
      
      <hr />
      <h3>Review</h3>
      
      {reviewState ? (
        <div>
          <p>Field: {reviewState.fieldName} - Current value: {reviewState.currentValue}</p>
          <DocumentReviewForm 
            fieldName={reviewState.fieldName}
            currentValue={reviewState.currentValue} 
            onSubmit={async value => {
              await submitCorrections(reviewState.fieldName, value);
            }}
            isLoading={isLoading} 
          />
          <ApproveRejectButtons 
            canApprove={reviewState.canApprove}
            canReject={reviewState.canReject}
            onApprove={async () => {
              await approve();
              navigate('/');
            }}
            onReject={async reason => {
              await reject({ reason });
              navigate('/');
            }}
          />
        </div>
      ) : (
        <p>{reviewError ?? 'No extracted fields are available to review.'}</p>
      )}
      
      <Link to="/">Back to documents</Link>
    </div>
  );
};