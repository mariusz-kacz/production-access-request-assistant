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
  const canCreateRequest =
    session?.authenticated === true &&
    session.capabilities.includes("createRequest");

  return (
    <div className="app-shell">
      <aside className="app-rail">
        <header className="app-rail__brand">
          <Link className="app-brand" to="/requests">
            <span>
              Governed Access
              <small>Production access</small>
            </span>
          </Link>
        </header>

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
            <span aria-hidden="true">01</span>
            Requests
          </NavLink>
          {canCreateRequest && (
            <NavLink
              to="/requests/new"
              className={({ isActive }) =>
                isActive
                  ? "navigation-link navigation-link--active"
                  : "navigation-link"
              }
            >
              <span aria-hidden="true">02</span>
              New request
            </NavLink>
          )}
        </nav>

        <DemoIdentitySelector onSessionChange={setSession} />
      </aside>

      <div className="app-workspace">
        {session === null ? (
          <SessionLoadingPage />
        ) : session.authenticated ? (
          <AuthenticatedRoutes
            key={session.principal.id}
            canCreateRequest={canCreateRequest}
          />
        ) : (
          <SignInRequiredPage />
        )}
      </div>
    </div>
  );
}

function AuthenticatedRoutes({
  canCreateRequest,
}: {
  canCreateRequest: boolean;
}) {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/requests" replace />} />
      <Route
        path="/requests"
        element={<RequestListPage canCreateRequest={canCreateRequest} />}
      />
      <Route
        path="/requests/new"
        element={
          canCreateRequest ? (
            <NewRequestPage />
          ) : (
            <Navigate to="/requests" replace />
          )
        }
      />
      <Route path="/requests/:requestId" element={<RequestDetailPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

function SessionLoadingPage() {
  return (
    <main className="session-page" aria-labelledby="session-loading-title">
      <h1 id="session-loading-title">Loading session</h1>
      <p>Checking the current demo identity.</p>
    </main>
  );
}

function SignInRequiredPage() {
  return (
    <main className="session-page" aria-labelledby="sign-in-required-title">
      <h1 id="sign-in-required-title">Sign in to continue</h1>
      <p>Select a demo identity above.</p>
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
