import React, { useEffect, useState } from 'react';

// Fetches the PDF with the session cookie and shows it from a blob URL, so the API can keep serving it as an attachment.
export const DocumentPreview: React.FC<{ contentUrl: string; title: string }> = ({ contentUrl, title }) => {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let url: string | null = null;
    let cancelled = false;
    fetch(contentUrl, { credentials: 'same-origin' })
      .then(async response => {
        if (!response.ok) throw new Error('Could not load the PDF preview.');
        const blob = await response.blob();
        if (cancelled) return;
        url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
        setObjectUrl(url);
      })
      .catch(err => !cancelled && setError(err instanceof Error ? err.message : 'Could not load the PDF preview.'));
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [contentUrl]);

  if (error) return <p className="error">{error}</p>;
  if (!objectUrl) return <p>Loading preview...</p>;
  return (
    <div className="document-preview">
      <iframe src={objectUrl} title={`Preview of ${title}`} />
      <a href={objectUrl} download={title}>Download PDF</a>
    </div>
  );
};
