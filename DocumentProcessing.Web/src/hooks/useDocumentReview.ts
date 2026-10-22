import { useEffect, useState } from 'react';

interface ReviewState {
  fieldName: string;
  currentValue: string | null;
  canApprove: boolean;
  canReject: boolean;
}

interface ReviewActionResponse {
  success: boolean;
  message: string | null;
}

interface DocumentDetail {
  extractedFields?: Array<{
    fieldName: string;
    value: string | null;
  }>;
}

export const useDocumentReview = (documentId: string) => {
  const [reviewState, setReviewState] = useState<ReviewState | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [reviewError, setReviewError] = useState<string | null>(null);

  useEffect(() => {
    if (!documentId) {
      setIsLoading(false);
      return;
    }

    const loadReviewState = async () => {
      try {
        const response = await fetch(`/api/documents/${documentId}/review`, {
          headers: { Accept: 'application/json' },
          credentials: 'same-origin',
        });
        if (!response.ok) throw new Error('Could not load review data.');

        const detail = await response.json() as DocumentDetail;
        const field = detail.extractedFields?.[0];
        if (field) {
          setReviewState({
            fieldName: field.fieldName,
            currentValue: field.value,
            canApprove: true,
            canReject: true,
          });
        } else {
          setReviewState(null);
        }
      } catch (error) {
        setReviewError(error instanceof Error ? error.message : 'Could not load review data.');
      } finally {
        setIsLoading(false);
      }
    };

    void loadReviewState();
  }, [documentId]);

  const submitCorrections = async (fieldName: string, correctedValue: string) => {
    const response = await fetch(`/api/documents/${documentId}/review/corrections`, {
      method: 'PUT',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ correctedValues: { [fieldName]: correctedValue } }),
    });
    if (!response.ok) throw new Error('Could not save the correction.');

    const result = await response.json() as ReviewActionResponse;
    if (!result.success) throw new Error(result.message ?? 'Could not save the correction.');
    setReviewState(previous => previous
      ? { ...previous, currentValue: correctedValue }
      : previous);
    return result;
  };

  const approve = async () => {
    const response = await fetch(`/api/documents/${documentId}/review/approve`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
    });
    if (!response.ok) throw new Error('Could not approve the document.');

    const result = await response.json() as ReviewActionResponse;
    if (!result.success) throw new Error(result.message ?? 'Could not approve the document.');
    setReviewState(null);
    return result;
  };

  const reject = async (request: { reason: string }) => {
    const response = await fetch(`/api/documents/${documentId}/review/reject`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error('Could not reject the document.');

    const result = await response.json() as ReviewActionResponse;
    if (!result.success) throw new Error(result.message ?? 'Could not reject the document.');
    setReviewState(null);
    return result;
  };

  return {
    reviewState,
    submitCorrections,
    approve,
    reject,
    isLoading,
    reviewError,
  };
};
