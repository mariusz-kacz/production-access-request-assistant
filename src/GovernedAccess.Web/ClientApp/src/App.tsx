import {
  BrowserRouter,
  Link,
  Navigate,
  NavLink,
  Route,
  Routes,
} from "react-router";

import { NewRequestPage } from "./pages/NewRequestPage";

export function App() {
  return (
    <BrowserRouter>
      <ApplicationShell />
    </BrowserRouter>
  );
}

function ApplicationShell() {
  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="app-header__identity">
          <Link className="app-brand" to="/requests/new">
            Governed Access
          </Link>
          <span className="environment-badge">Synthetic local demo</span>
        </div>

        <nav aria-label="Primary navigation">
          <NavLink
            to="/requests/new"
            className={({ isActive }) =>
              isActive ? "navigation-link navigation-link--active" : "navigation-link"
            }
          >
            New request
          </NavLink>
        </nav>
      </header>

      <Routes>
        <Route path="/" element={<Navigate to="/requests/new" replace />} />
        <Route path="/requests/new" element={<NewRequestPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>

      <footer className="app-footer">
        AI assists with interpretation. Humans approve. Deterministic services
        authorize and execute.
      </footer>
    </div>
  );
}

function NotFoundPage() {
  return (
    <main className="not-found-page" aria-labelledby="not-found-title">
      <h1 id="not-found-title">Page not found</h1>
      <p>The requested application page is not available.</p>
      <Link to="/requests/new">Prepare a new request</Link>
    </main>
  );
}

export default App;
