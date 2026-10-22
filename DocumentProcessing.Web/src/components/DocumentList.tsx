import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { DocumentListItem } from './DocumentListItem';

export const DocumentList: React.FC = () => {
  const [documents, setDocuments] = useState<DocumentSummaryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);

  useEffect(() => {
    void loadDocuments();
  }, []);

  const loadDocuments = async () => {
    try {
      const response = await fetch('/api/documents', { credentials: 'same-origin' });
      if (!response.ok) throw new Error('Could not load documents.');
      setDocuments(await response.json() as DocumentSummaryDto[]);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Could not load documents.');
    } finally {
      setLoading(false);
    }
  };

  const uploadDocument = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedFile) {
      setError('Choose a PDF file to upload.');
      return;
    }

    setUploading(true);
    setError(null);
    try {
      const formData = new FormData();
      formData.append('file', selectedFile);
      const response = await fetch('/api/documents', {
        method: 'POST',
        credentials: 'same-origin',
        body: formData,
      });
      if (!response.ok) {
        throw new Error(response.status === 413
          ? 'The PDF exceeds the configured upload limit.'
          : 'The PDF could not be uploaded.');
      }
      setSelectedFile(null);
      await loadDocuments();
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : 'The PDF could not be uploaded.');
    } finally {
      setUploading(false);
    }
  };

  if (loading) return <p>Loading documents...</p>;

  return (
    <div className="document-list">
      <h2>Documents</h2>
      <form className="upload-form" onSubmit={uploadDocument}>
        <label htmlFor="invoice-pdf">Upload a synthetic invoice PDF</label>
        <input id="invoice-pdf" type="file" accept=".pdf,application/pdf" required
          onChange={event => setSelectedFile(event.target.files?.[0] ?? null)} />
        <button type="submit" disabled={uploading || !selectedFile}>
          {uploading ? 'Uploading...' : 'Upload PDF'}
        </button>
      </form>
      {error && <p className="error" role="alert">Error: {error}</p>}
      {documents.length === 0 ? (
        <p>No documents found.</p>
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

interface DocumentSummaryDto {
  id: string;
  originalFileName: string;
  status: string;
  createdAtUtc: string;
}