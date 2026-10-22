import React from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { DocumentList } from './components/DocumentList';
import { DocumentDetail } from './components/DocumentDetail';

const App: React.FC = () => {
  return (
    <Router>
      <Routes>
        <Route path="/" element={<DocumentList />} />
        <Route path="/documents/:documentId" element={<DocumentDetail />} />
      </Routes>
    </Router>
  );
};

export default App;