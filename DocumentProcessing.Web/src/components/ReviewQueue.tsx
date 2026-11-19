import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { xsrfHeaders } from '../csrf';

interface QueueItem {
  id: string;
  originalFileName: string;
  createdAtUtc: string;
}

export const ReviewQueue: React.FC = () => {
  const [items, setItems] = useState<QueueItem[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  const load = async () => {
    try {
      const response = await fetch('/api/documents/review-queue', { credentials: 'same-origin' });
      if (response.status === 403) throw new Error('Only reviewers and admins can use the review queue.');
      if (!response.ok) throw new Error('Could not load the review queue.');
      setItems(await response.json() as QueueItem[]);
      setTotal(Number(response.headers.get('X-Total-Count') ?? 0));
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Could not load the review queue.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const claim = async (id: string) => {
    setError(null);
    const response = await fetch(`/api/documents/${id}/claim`, {
      method: 'POST', credentials: 'same-origin', headers: xsrfHeaders(),
    });
    if (response.ok) {
      navigate(`/documents/${id}`);
      return;
    }
    setError(response.status === 409 ? 'Another reviewer already claimed this document.' : 'Could not claim the document.');
    await load();
  };

  if (loading) return <p>Loading review queue...</p>;

  return (
    <div className="review-queue">
      <h2>Review queue ({total})</h2>
      {error && <p className="error" role="alert">{error}</p>}
      {items.length === 0 ? <p>No documents are waiting for a reviewer.</p> : (
        <ul>
          {items.map(item => (
            <li key={item.id} className="document-item">
              <span>{item.originalFileName}</span>
              <button type="button" onClick={() => void claim(item.id)}>Claim</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};
