import React, { useState, useEffect } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { DocumentReviewForm } from './DocumentReviewForm';
import { ApproveRejectButtons } from './ApproveRejectButtons';
import { DocumentPreview } from './DocumentPreview';
import { ModelFailureNotice } from './ModelFailureNotice';
import { blockingResults, isDecided, useDocumentReview } from '../hooks/useDocumentReview';

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
  const { detail, submitCorrections, approve, reject, isLoading, reviewError } =
    useDocumentReview(documentId ?? '');

  useEffect(() => {
    if (!documentId) {
      setError('Document ID is missing.');
      setLoading(false);
      return;
    }
    fetch(`/api/documents/${documentId}`)
      .then(async (response) => {
        if (response.status === 404) throw new Error('This document does not exist or you do not have access to it.');
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

  const decided = detail ? isDecided(detail) : false;
  const awaitingReview = detail?.status === 'NeedsReview';
  const blocking = detail ? blockingResults(detail) : [];

  return (
    <div className="document-detail">
      <h2>{document.originalFileName}</h2>
      <p>
        Status: {detail?.status ?? document.status} · Review: {detail?.reviewStatus ?? '-'} ·
        Created: {new Date(document.createdAtUtc).toLocaleDateString()}
      </p>
      {detail && (
        <p className="model-info">
          AI model: {detail.aiModelType}
          {detail.processedWithModelName && ` · processed with ${detail.processedWithModelType} (${detail.processedWithModelName})`}
        </p>
      )}
      {detail?.status === 'Failed' && detail.failureReason && documentId && (
        <ModelFailureNotice
          documentId={documentId}
          failureReason={detail.failureReason}
          modelType={detail.aiModelType}
          onRetried={() => window.location.reload()}
        />
      )}
      {detail?.rejectionReason && <p>Rejection reason: {detail.rejectionReason}</p>}

      <div className="review-layout">
        <section aria-label="Document preview">
          {detail?.contentUrl && <DocumentPreview contentUrl={detail.contentUrl} title={document.originalFileName} />}
        </section>

        <section aria-label="Review">
          <h3>Review</h3>
          {isLoading && <p>Loading review data...</p>}
          {!isLoading && !detail && <p className="error">{reviewError ?? 'Review data is not available.'}</p>}
          {detail && detail.extractedFields.length === 0 && (
            <p>{awaitingReview ? 'No extraction result is available yet.' : `The document is ${detail.status}; nothing to review yet.`}</p>
          )}
          {detail && detail.extractedFields.length > 0 && (
            <>
              <DocumentReviewForm
                fields={detail.extractedFields}
                validationResults={detail.validationResults}
                readOnly={decided || !awaitingReview}
                onSubmit={async values => {
                  await submitCorrections(values);
                }}
              />
              {!decided && awaitingReview && (
                <ApproveRejectButtons
                  canApprove={blocking.length === 0}
                  approveHint={blocking.length > 0 ? 'Fix the validation errors above before approving.' : undefined}
                  canReject
                  onApprove={async () => {
                    await approve();
                    navigate('/');
                  }}
                  onReject={async reason => {
                    await reject({ reason });
                    navigate('/');
                  }}
                />
              )}
            </>
          )}
        </section>
      </div>

      <Link to="/">Back to documents</Link>
    </div>
  );
};
