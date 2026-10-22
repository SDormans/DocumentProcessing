import { useState, useEffect } from 'react';

interface ReviewState {
  fieldName: string | null;
  currentValue: string | number | null;
  canApprove: boolean;
  canReject: boolean;
}

interface ReviewActionResponse {
  success: boolean;
  message: string;
}

export const useDocumentReview = (documentId: string) => {
  const [reviewState, setReviewState] = useState<ReviewState>({
    fieldName: null,
    currentValue: null,
    canApprove: false,
    canReject: false,
  });
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchReviewState();
  }, [documentId]);

  const fetchReviewState = async () => {
    try {
      const response = await fetch(`/api/documents/${documentId}/review`, {
        headers: { 'Accept': 'application/json' }
      });
      
      if (!response.ok) throw new Error('Failed to fetch review state');
      
      const data = await response.json();
      setReviewState({
        fieldName: data.fieldName || null,
        currentValue: data.currentValue || null,
        canApprove: data.canApprove || false,
        canReject: data.canReject || false,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
    } finally {
      setIsLoading(false);
    }
  };

  const submitCorrections = async (correctedValue: string) => {
    try {
      const response = await fetch(`/api/documents/${documentId}/review/corrections`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ correctedValue }),
      });
      
      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Fout bij indienen correcties');
      }
      
      const result = await response.json();
      setReviewState(prev => ({
        ...prev,
        currentValue: correctedValue,
      }));
      return result;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
      throw err;
    }
  };

  const approve = async (request?: any) => {
    try {
      const body = request ? JSON.stringify(request) : undefined;
      const response = await fetch(`/api/documents/${documentId}/review/approve`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body,
      });
      
      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Fout bij goedkeuring');
      }
      
      const result = await response.json() as ReviewActionResponse;
      setReviewState({
        fieldName: null,
        currentValue: null,
        canApprove: false,
        canReject: false,
      });
      return result;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
      throw err;
    }
  };

  const reject = async (request: any) => {
    try {
      const response = await fetch(`/api/documents/${documentId}/review/reject`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });
      
      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Fout bij afwijzing');
      }
      
      const result = await response.json() as ReviewActionResponse;
      setReviewState({
        fieldName: null,
        currentValue: null,
        canApprove: false,
        canReject: false,
      });
      return result;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Onbekende fout');
      throw err;
    }
  };

  return {
    reviewState,
    submitCorrections,
    approve,
    reject,
    isLoading,
    error,
  };
};