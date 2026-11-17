import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { DocumentListItem } from './DocumentListItem';
import { xsrfHeaders } from '../csrf';

const PAGE_SIZE = 25;
const STATUSES = ['Queued', 'Processing', 'NeedsReview', 'Completed', 'Failed'];

export const DocumentList: React.FC = () => {
  const [documents, setDocuments] = useState<DocumentSummaryDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [uploadProgress, setUploadProgress] = useState<string | null>(null);

  useEffect(() => {
    void loadDocuments();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, status, search]);

  const loadDocuments = async () => {
    try {
      const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
      if (status) query.set('status', status);
      if (search.trim()) query.set('search', search.trim());
      const response = await fetch(`/api/documents?${query.toString()}`, { credentials: 'same-origin' });
      if (!response.ok) throw new Error('Could not load documents.');
      setDocuments(await response.json() as DocumentSummaryDto[]);
      setTotalCount(Number(response.headers.get('X-Total-Count') ?? 0));
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Could not load documents.');
    } finally {
      setLoading(false);
    }
  };

  const uploadDocuments = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (selectedFiles.length === 0) {
      setError('Choose one or more PDF files to upload.');
      return;
    }

    setError(null);
    const failed: string[] = [];
    for (const [index, file] of selectedFiles.entries()) {
      setUploadProgress(`Uploading ${index + 1} of ${selectedFiles.length}...`);
      const formData = new FormData();
      formData.append('file', file);
      try {
        const response = await fetch('/api/documents', {
          method: 'POST',
          credentials: 'same-origin',
          headers: xsrfHeaders(),
          body: formData,
        });
        if (!response.ok) {
          failed.push(response.status === 413 ? `${file.name} (too large)` : file.name);
        }
      } catch {
        failed.push(file.name);
      }
    }
    setUploadProgress(null);
    setSelectedFiles([]);
    if (failed.length > 0) setError(`Could not upload: ${failed.join(', ')}.`);
    setPage(1);
    await loadDocuments();
  };

  if (loading) return <p>Loading documents...</p>;

  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <div className="document-list">
      <h2>Documents</h2>
      <form className="upload-form" onSubmit={uploadDocuments}>
        <label htmlFor="invoice-pdf">Upload invoice PDFs</label>
        <input id="invoice-pdf" type="file" accept=".pdf,application/pdf" multiple required
          onChange={event => setSelectedFiles(Array.from(event.target.files ?? []))} />
        <button type="submit" disabled={uploadProgress !== null || selectedFiles.length === 0}>
          {uploadProgress ?? (selectedFiles.length > 1 ? `Upload ${selectedFiles.length} PDFs` : 'Upload PDF')}
        </button>
      </form>
      <div className="list-filters">
        <label htmlFor="document-search">Search</label>
        <input id="document-search" type="search" placeholder="File name" value={search}
          onChange={event => { setSearch(event.target.value); setPage(1); }} />
        <label htmlFor="document-status">Status</label>
        <select id="document-status" value={status}
          onChange={event => { setStatus(event.target.value); setPage(1); }}>
          <option value="">All</option>
          {STATUSES.map(s => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
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
      <nav className="pager" aria-label="Pages">
        <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
        <span>Page {page} of {pageCount} ({totalCount} documents)</span>
        <button type="button" disabled={page >= pageCount} onClick={() => setPage(page + 1)}>Next</button>
      </nav>
    </div>
  );
};

interface DocumentSummaryDto {
  id: string;
  originalFileName: string;
  status: string;
  createdAtUtc: string;
}