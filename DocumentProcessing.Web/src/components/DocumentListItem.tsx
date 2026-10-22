import React from 'react';

export const DocumentListItem: React.FC<{ document: any }> = ({ document }) => {
  return (
    <div>
      <span>{document.originalFileName}</span>
      <span>{document.status}</span>
    </div>
  );
};