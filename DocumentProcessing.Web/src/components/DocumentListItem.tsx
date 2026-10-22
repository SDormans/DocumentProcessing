import React from 'react';

interface DocumentSummary {
  originalFileName: string;
  status: string;
}

export const DocumentListItem: React.FC<{ document: DocumentSummary }> = ({ document }) => {
  return (
    <div>
      <span>{document.originalFileName}</span>
      <span>{document.status}</span>
    </div>
  );
};