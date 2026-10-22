import React from 'react';
import { useEffect, useState } from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { DocumentList } from './components/DocumentList';
import { DocumentDetail } from './components/DocumentDetail';

const App: React.FC = () => {
  const [authenticated, setAuthenticated] = useState<boolean | null>(null);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    fetch('/api/auth/me', { credentials: 'same-origin' })
      .then(response => setAuthenticated(response.ok))
      .catch(() => {
        setAuthenticated(false);
        setError('The API could not be reached.');
      });
  }, []);

  const signIn = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSubmitting(true);
    setError(null);
    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      });
      if (!response.ok) {
        throw new Error(response.status === 429
          ? 'Too many login attempts. Wait a minute and try again.'
          : 'The username or password is incorrect.');
      }
      setPassword('');
      setAuthenticated(true);
    } catch (loginError) {
      setError(loginError instanceof Error ? loginError.message : 'Sign-in failed.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const signOut = async () => {
    try {
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'same-origin',
      });
      if (!response.ok) throw new Error('Sign-out failed. Try again.');
      setAuthenticated(false);
    } catch (signOutError) {
      setError(signOutError instanceof Error ? signOutError.message : 'Sign-out failed.');
    }
  };

  if (authenticated === null) {
    return <main className="app-shell"><p>Checking your session...</p></main>;
  }

  if (!authenticated) {
    return (
      <main className="app-shell">
        <form className="sign-in" onSubmit={signIn}>
          <h1>Document Processing</h1>
          <p>Sign in to review uploaded documents.</p>
          <label htmlFor="username">Username</label>
          <input id="username" autoComplete="username" required value={username}
            onChange={event => setUsername(event.target.value)} />
          <label htmlFor="password">Password</label>
          <input id="password" type="password" autoComplete="current-password" required
            value={password} onChange={event => setPassword(event.target.value)} />
          {error && <p className="error" role="alert">{error}</p>}
          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in...' : 'Sign in'}
          </button>
        </form>
      </main>
    );
  }

  return (
    <Router>
      <main className="app-shell">
        <header className="app-header">
          <h1>Document Processing</h1>
          <button type="button" onClick={signOut}>Sign out</button>
        </header>
        {error && <p className="error" role="alert">{error}</p>}
        <Routes>
          <Route path="/" element={<DocumentList />} />
          <Route path="/documents/:documentId" element={<DocumentDetail />} />
        </Routes>
      </main>
    </Router>
  );
};

export default App;