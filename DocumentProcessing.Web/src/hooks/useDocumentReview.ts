import { useCallback, useEffect, useState } from 'react';
import { xsrfHeaders } from '../csrf';

export interface ReviewField {
  fieldName: string;
  value: string | null;
  correctedValue: string | null;
  approvedValue: string | null;
  effectiveValue: string | null;
  isCorrected: boolean;
}

export interface ValidationResult {
  ruleId: string;
  severity: string;
  message: string;
  affectedFields: string | null;
}

export interface ReviewDetail {
  status: string;
  reviewStatus: string;
  contentUrl: string | null;
  rejectionReason: string | null;
  extractedFields: ReviewField[];
  validationResults: ValidationResult[];
}

interface ReviewActionResponse {
  success: boolean;
  message: string | null;
  errors?: string[];
}

export class ReviewActionError extends Error {
  constructor(message: string, readonly errors: string[] = []) {
    super(message);
  }
}

const decidedStatuses = ['Approved', 'Rejected'];

export const isDecided = (detail: ReviewDetail) => decidedStatuses.includes(detail.reviewStatus);

export const blockingResults = (detail: ReviewDetail) =>
  detail.validationResults.filter(r => r.severity === 'Error' || r.severity === 'Critical');

const send = async (url: string, method: string, body: unknown, fallback: string) => {
  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...xsrfHeaders() },
    body: JSON.stringify(body),
  });
  const result = await response.json().catch(() => null) as ReviewActionResponse | null;
  if (!response.ok || !result?.success) {
    throw new ReviewActionError(result?.message ?? fallback, result?.errors ?? []);
  }
  return result;
};

export const useDocumentReview = (documentId: string) => {
  const [detail, setDetail] = useState<ReviewDetail | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [reviewError, setReviewError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const response = await fetch(`/api/documents/${documentId}/review`, {
      headers: { Accept: 'application/json' },
      credentials: 'same-origin',
    });
    if (response.status === 404) throw new Error('This document does not exist or you do not have access to it.');
    if (!response.ok) throw new Error('Could not load review data.');
    setDetail(await response.json() as ReviewDetail);
  }, [documentId]);

  useEffect(() => {
    if (!documentId) {
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    load()
      .catch(error => setReviewError(error instanceof Error ? error.message : 'Could not load review data.'))
      .finally(() => setIsLoading(false));
  }, [documentId, load]);

  const submitCorrections = async (correctedValues: Record<string, string>) => {
    const result = await send(`/api/documents/${documentId}/review/corrections`, 'PUT', { correctedValues },
      'Could not save the corrections.');
    await load();
    return result;
  };

  const approve = async () => {
    try {
      return await send(`/api/documents/${documentId}/review/approve`, 'POST', {}, 'Could not approve the document.');
    } finally {
      await load().catch(() => undefined);
    }
  };

  const reject = async (request: { reason: string }) => {
    try {
      return await send(`/api/documents/${documentId}/review/reject`, 'POST', request, 'Could not reject the document.');
    } finally {
      await load().catch(() => undefined);
    }
  };

  return { detail, submitCorrections, approve, reject, isLoading, reviewError };
};
