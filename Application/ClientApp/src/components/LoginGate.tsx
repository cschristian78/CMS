import { FormEvent, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../api';
import {
  fetchCatLoginUsers,
  loginWithEmail,
  type AuthCatUser,
} from '../authSession';

interface LoginGateProps {
  onAuthenticated: (session: { fullName?: string; email?: string; userID?: number }) => void;
}

export default function LoginGate({ onAuthenticated }: LoginGateProps) {
  const [users, setUsers] = useState<AuthCatUser[]>([]);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loadingUsers, setLoadingUsers] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      setLoadingUsers(true);
      setError(null);
      try {
        const rows = await fetchCatLoginUsers();
        if (cancelled) {
          return;
        }
        setUsers(rows);
        if (rows.length === 1) {
          setEmail(rows[0].email);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : 'Could not load CAT users.');
        }
      } finally {
        if (!cancelled) {
          setLoadingUsers(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const selected = useMemo(
    () => users.find((u) => u.email.toLowerCase() === email.trim().toLowerCase()) ?? null,
    [users, email],
  );
  const mustSetPassword = selected?.mustSetPassword === true;

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!email.trim()) {
      setError('Select your email to continue.');
      return;
    }
    if (mustSetPassword) {
      if (!newPassword.trim()) {
        setError('Create a password to launch CMS for the first time.');
        return;
      }
      if (newPassword !== confirmPassword) {
        setError('New password and confirmation do not match.');
        return;
      }
    } else if (!password) {
      setError('Enter your password.');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const result = await loginWithEmail(
        mustSetPassword
          ? { email: email.trim(), newPassword: newPassword.trim() }
          : { email: email.trim(), password },
      );
      onAuthenticated(result);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Sign-in failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="home-modal-overlay login-gate-overlay" role="presentation">
      <div
        className="home-modal-dialog login-gate-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cms-login-title"
      >
        <h2 id="cms-login-title" className="home-modal-title">
          Sign in to launch
        </h2>
        <p className="home-modal-body">
          Access is limited to active users in the <strong>CAT</strong> department. First-time users set a password
          before launching.
        </p>

        <form className="login-gate-form" onSubmit={(e) => void handleSubmit(e)}>
          <label htmlFor="cms-login-email">Username (email)</label>
          <select
            id="cms-login-email"
            value={email}
            disabled={loadingUsers || busy}
            onChange={(e) => {
              setEmail(e.target.value);
              setPassword('');
              setNewPassword('');
              setConfirmPassword('');
              setError(null);
            }}
            required
          >
            <option value="">{loadingUsers ? 'Loading CAT users…' : 'Select email…'}</option>
            {users.map((u) => (
              <option key={u.userID} value={u.email}>
                {u.email}
                {u.mustSetPassword ? ' (first launch)' : ''}
              </option>
            ))}
          </select>

          {mustSetPassword ? (
            <>
              <label htmlFor="cms-login-new-password">Create password</label>
              <input
                id="cms-login-new-password"
                type="password"
                autoComplete="new-password"
                value={newPassword}
                disabled={busy}
                onChange={(e) => setNewPassword(e.target.value)}
                required
              />
              <label htmlFor="cms-login-confirm-password">Confirm password</label>
              <input
                id="cms-login-confirm-password"
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                disabled={busy}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
              />
              <p className="home-modal-hint">Default password is none until you create one on first launch.</p>
            </>
          ) : (
            <>
              <label htmlFor="cms-login-password">Password</label>
              <input
                id="cms-login-password"
                type="password"
                autoComplete="current-password"
                value={password}
                disabled={busy || !email}
                onChange={(e) => setPassword(e.target.value)}
                required={!!email}
              />
            </>
          )}

          {error ? (
            <div className="alert alert-error" role="alert">
              {error}
            </div>
          ) : null}

          <div className="home-modal-actions">
            <button type="submit" className="primary-button" disabled={busy || loadingUsers || !email}>
              {busy ? 'Signing in…' : mustSetPassword ? 'Set password & launch' : 'Launch'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
