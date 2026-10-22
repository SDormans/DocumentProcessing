import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { DocumentListItem } from './DocumentListItem';

export const DocumentList: React.FC = () => {
  const [documents, setDocuments] = useState<DocumentDetailDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch('/api/documents')
      .then(async (response) => {
        if (!response.ok) throw new Error('Failed to fetch documents');
        const data = await response.json();
        setDocuments(data);
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : 'Unknown error');
        setDocuments([]);
      })
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <p>Laden...</p>;
  if (error) return <p className="error">Fout: {error}</p>;

  return (
    <div className="document-list">
      <h2>Documenten</h2>
      {documents.length === 0 ? (
        <p>Geen documenten gevonden.</p>
      ) : (
        <ul>
          {documents.map((doc) => (
            <li key={doc.id} className="document-item">
              <Link to={`/documents/${doc.id}`}>
                <DocumentListItem document={doc} />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

interface DocumentDetailDto {
  id: string;
  originalFileName: string;
  status: string;
  createdAtUtc: string;
}