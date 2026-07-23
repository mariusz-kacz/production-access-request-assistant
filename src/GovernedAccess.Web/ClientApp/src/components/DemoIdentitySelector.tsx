import {
  type FormEvent,
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react";

import { ApiError, apiRequest } from "../api/client";
import {
  demoPrincipalKeys,
  type DemoPrincipalKey,
  type DemoSessionRequest,
  type SessionView,
} from "../api/contracts";

export interface DemoIdentitySelectorProps {
  onSessionChange: (session: SessionView | null) => void;
}

interface SessionError {
  message: string;
  code?: string;
  correlationId?: string;
}

type SessionActivity = "loading" | "signingIn" | "signingOut";

const anonymousSession: SessionView = {
  authenticated: false,
  principal: null,
  capabilities: [],
};

const demoIdentities: ReadonlyArray<{
  key: DemoPrincipalKey;
  label: string;
}> = [
  { key: demoPrincipalKeys.requester, label: "Demo Requester" },
  {
    key: demoPrincipalKeys.clientAlphaApprover,
    label: "Client Alpha Business Approver",
  },
  {
    key: demoPrincipalKeys.clientBetaApprover,
    label: "Client Beta Business Approver",
  },
  { key: demoPrincipalKeys.devOpsApprover, label: "DevOps Approver" },
];

export function DemoIdentitySelector({
  onSessionChange,
}: DemoIdentitySelectorProps) {
  const [session, setSession] = useState<SessionView | null>(null);
  const [selectedPrincipalKey, setSelectedPrincipalKey] =
    useState<DemoPrincipalKey>(demoPrincipalKeys.requester);
  const [activity, setActivity] = useState<SessionActivity | undefined>(
    "loading",
  );
  const [error, setError] = useState<SessionError>();
  const activeRequest = useRef<AbortController | undefined>(undefined);

  const publishSession = useCallback(
    (nextSession: SessionView) => {
      setSession(nextSession);
      if (
        nextSession.authenticated &&
        isDemoPrincipalKey(nextSession.principal.id)
      ) {
        setSelectedPrincipalKey(nextSession.principal.id);
      }
      onSessionChange(nextSession);
    },
    [onSessionChange],
  );

  useEffect(() => {
    const controller = beginRequest("loading");

    void apiRequest<SessionView>("/api/session", {
      signal: controller.signal,
    })
      .then((response) => {
        if (!controller.signal.aborted) {
          publishSession(response);
        }
      })
      .catch((requestError: unknown) => {
        if (!isAbortError(requestError)) {
          setError(toSessionError(requestError));
          onSessionChange(null);
        }
      })
      .finally(() => finishRequest(controller));

    return () => {
      if (activeRequest.current === controller) {
        controller.abort();
        activeRequest.current = undefined;
      }
    };
  }, [onSessionChange, publishSession]);

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (activity !== undefined) {
      return;
    }

    const controller = beginRequest("signingIn");
    try {
      const nextSession = await apiRequest<SessionView, DemoSessionRequest>(
        "/api/demo/session",
        {
          method: "POST",
          body: { principalKey: selectedPrincipalKey },
          signal: controller.signal,
        },
      );

      if (!controller.signal.aborted) {
        publishSession(nextSession);
      }
    } catch (requestError: unknown) {
      if (!isAbortError(requestError)) {
        setError(toSessionError(requestError));
      }
    } finally {
      finishRequest(controller);
    }
  }

  async function signOut() {
    if (activity !== undefined) {
      return;
    }

    const controller = beginRequest("signingOut");
    try {
      await apiRequest<void>("/api/demo/session", {
        method: "DELETE",
        signal: controller.signal,
      });

      if (!controller.signal.aborted) {
        publishSession(anonymousSession);
      }
    } catch (requestError: unknown) {
      if (!isAbortError(requestError)) {
        setError(toSessionError(requestError));
      }
    } finally {
      finishRequest(controller);
    }
  }

  function beginRequest(nextActivity: SessionActivity): AbortController {
    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setActivity(nextActivity);
    setError(undefined);
    return controller;
  }

  function finishRequest(controller: AbortController) {
    if (activeRequest.current === controller) {
      activeRequest.current = undefined;
      setActivity(undefined);
    }
  }

  const busy = activity !== undefined;

  return (
    <section
      className="demo-identity-selector"
      aria-labelledby="demo-identity-title"
    >
      <div className="demo-identity-selector__intro">
        <h2 id="demo-identity-title">Acting identity</h2>
        <p id="demo-identity-description">
          Change roles to inspect the governed workflow.
        </p>
      </div>

      <div className="demo-identity-selector__status">
        {session?.authenticated === true ? (
          <p role="status" aria-live="polite">
            Signed in as {session.principal.displayName}
          </p>
        ) : activity === "loading" ? (
          <p role="status" aria-live="polite">
            Loading demo session…
          </p>
        ) : (
          <p>No demo identity is signed in.</p>
        )}
      </div>

      {error !== undefined && (
        <div className="problem-summary" role="alert">
          <h3>Session not changed</h3>
          <p>{error.message}</p>
          {error.code !== undefined && <p>Code: {error.code}</p>}
          {error.correlationId !== undefined && (
            <p>Correlation ID: {error.correlationId}</p>
          )}
        </div>
      )}

      <div className="demo-identity-selector__controls">
        <form onSubmit={signIn}>
          <label htmlFor="demo-principal-key">Demo identity</label>
          <select
            id="demo-principal-key"
            name="principalKey"
            value={selectedPrincipalKey}
            aria-describedby="demo-identity-description"
            onChange={(event) => {
              setSelectedPrincipalKey(event.target.value as DemoPrincipalKey);
              setError(undefined);
            }}
            disabled={busy}
          >
            {demoIdentities.map((identity) => (
              <option key={identity.key} value={identity.key}>
                {identity.label}
              </option>
            ))}
          </select>
          <button type="submit" disabled={busy}>
            {activity === "signingIn"
              ? "Changing identity…"
              : session?.authenticated === true
                ? "Switch identity"
                : "Sign in"}
          </button>
        </form>

        {session?.authenticated === true && (
          <button
            className="demo-identity-selector__sign-out"
            type="button"
            onClick={() => void signOut()}
            disabled={busy}
          >
            {activity === "signingOut" ? "Signing out…" : "Sign out"}
          </button>
        )}
      </div>
    </section>
  );
}

function toSessionError(error: unknown): SessionError {
  if (error instanceof ApiError) {
    return {
      message: error.detail,
      code: error.code,
      correlationId: error.correlationId,
    };
  }

  return {
    message: "The demo session could not be changed. Try again.",
    code: "unexpected_client_error",
  };
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}

function isDemoPrincipalKey(value: string): value is DemoPrincipalKey {
  return Object.values(demoPrincipalKeys).some(
    (principalKey) => principalKey === value,
  );
}
