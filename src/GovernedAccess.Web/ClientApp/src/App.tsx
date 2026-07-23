import { useState } from "react";
import {
  BrowserRouter,
  Link,
  Navigate,
  NavLink,
  Route,
  Routes,
} from "react-router";

import type { SessionView } from "./api/contracts";
import { DemoIdentitySelector } from "./components/DemoIdentitySelector";
import { NewRequestPage } from "./pages/NewRequestPage";
import { RequestDetailPage } from "./pages/RequestDetailPage";
import { RequestListPage } from "./pages/RequestListPage";

export function App() {
  return (
    <BrowserRouter>
      <ApplicationShell />
    </BrowserRouter>
  );
}

function ApplicationShell() {
  const [session, setSession] = useState<SessionView | null>(null);

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="app-header__primary">
          <div className="app-header__identity">
            <Link className="app-brand" to="/requests">
              Governed Access
            </Link>
            <span className="environment-badge">
              Synthetic demo · no real access
            </span>
          </div>

          <nav className="app-navigation" aria-label="Primary navigation">
            <NavLink
              to="/requests"
              end
              className={({ isActive }) =>
                isActive
                  ? "navigation-link navigation-link--active"
                  : "navigation-link"
              }
            >
              Requests
            </NavLink>
            <NavLink
              to="/requests/new"
              className={({ isActive }) =>
                isActive
                  ? "navigation-link navigation-link--active"
                  : "navigation-link"
              }
            >
              New request
            </NavLink>
          </nav>
        </div>

        <DemoIdentitySelector onSessionChange={setSession} />
      </header>

      {session === null ? (
        <SessionLoadingPage />
      ) : session.authenticated ? (
        <AuthenticatedRoutes key={session.principal.id} />
      ) : (
        <SignInRequiredPage />
      )}

      <footer className="app-footer">
        AI assists with interpretation. Humans approve. Deterministic services
        authorize and execute.
      </footer>
    </div>
  );
}

function AuthenticatedRoutes() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/requests" replace />} />
      <Route path="/requests" element={<RequestListPage />} />
      <Route path="/requests/new" element={<NewRequestPage />} />
      <Route path="/requests/:requestId" element={<RequestDetailPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

function SessionLoadingPage() {
  return (
    <main className="session-page" aria-labelledby="session-loading-title">
      <h1 id="session-loading-title">Checking your demo session</h1>
      <p>Protected application content will appear after the session is loaded.</p>
    </main>
  );
}

function SignInRequiredPage() {
  return (
    <main className="session-page" aria-labelledby="sign-in-required-title">
      <h1 id="sign-in-required-title">Sign in to continue</h1>
      <p>
        Select a fixed demo identity to use protected application capabilities.
      </p>
    </main>
  );
}

function NotFoundPage() {
  return (
    <main className="not-found-page" aria-labelledby="not-found-title">
      <h1 id="not-found-title">Page not found</h1>
      <p>The requested application page is not available.</p>
      <Link to="/requests">Return to relevant requests</Link>
    </main>
  );
}

export default App;
