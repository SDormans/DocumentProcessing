import React, { useState, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { DocumentReviewForm } from './DocumentReviewForm';
import { ApproveRejectButtons } from './ApproveRejectButtons';
import { useDocumentReview } from '../hooks/useDocumentReview';

export const DocumentDetail: React.FC<{ documentId: string }> = ({ documentId }) => {
  const navigate = useNavigate();
  const [document, setDocument] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { reviewState, submitCorrections, approve, reject, isLoading } = useDocumentReview(documentId);

  useEffect(() => {
    fetch(`/api/documents/${documentId}`)
      .then(async (response) => {
        if (!response.ok) throw new Error('Failed to fetch document');
        const data = await response.json();
        setDocument(data);
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : 'Unknown error');
      })
      .finally(() => setLoading(false));
  }, [documentId]);

  if (loading) return <p>Laden document detail...</p>;
  if (error) return <p className="error">Fout: {error}</p>;
  if (!document) return null;

  return (
    <div className="document-detail">
      <h2>{document.originalFileName}</h2>
      <p>Status: {document.status}</p>
      <p>Gemaakt: {new Date(document.createdAtUtc).toLocaleDateString()}</p>
      
      <hr />
      <h3>Review-Interface</h3>
      
      {reviewState ? (
        <div>
          <p>Veld: {reviewState.fieldName ?? 'Onbekend'} - Huidige waarde: {reviewState.currentValue}</p>
          <DocumentReviewForm 
            fieldName={reviewState.fieldName ?? ''} 
            currentValue={reviewState.currentValue} 
            onSubmit={submitCorrections} 
            isLoading={isLoading} 
          />
          <ApproveRejectButtons 
            documentId={documentId}
            canApprove={approve}
            canReject={reject}
            canApprove={reviewState?.canApprove}
            canReject={reviewState?.canReject} onApprove={function (request?: any): Promise<void>
            {
              throw new Error('Function not implemented.');
            } } onReject={function (request: any): Promise<void>
            {
              throw new Error('Function not implemented.');
            } }          />
        </div>
      ) : (
        <p>Geen review-gegevens beschikbaar</p>
      )}
      
      <Link to="/">← Terug naar lijst</Link>
    </div>
  );
};