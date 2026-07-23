import { useEffect, useRef, useState } from "react";
import { useParams } from "react-router";

import { ApiError, apiRequest } from "../api/client";
import type {
  ApprovalDecisionResponse,
  ProvisioningOperationResponse,
  ProvisioningRetryResponse,
  RequestAuditEventResponse,
  RequestDetailResponse,
  RequestGrantResponse,
} from "../api/contracts";
import {
  BUSINESS_DECISION_ACTION,
  BusinessDecisionPanel,
  type BusinessDecisionResponse,
} from "../components/BusinessDecisionPanel";
import {
  DEVOPS_DECISION_ACTION,
  DevOpsDecisionPanel,
  type DevOpsDecisionResponse,
} from "../components/DevOpsDecisionPanel";
import { RequestStatus } from "../components/RequestStatus";

export const RETRY_PROVISIONING_ACTION = "retryProvisioning";

interface RequestDetailError {
  message: string;
  code?: string;
  correlationId?: string;
}

const utcTimestampFormatter = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "UTC",
  timeZoneName: "short",
});

export function RequestDetailPage() {
  const { requestId } = useParams<{ requestId: string }>();
  const [currentRequest, setCurrentRequest] =
    useState<RequestDetailResponse>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<RequestDetailError>();
  const evidenceRefreshRequest = useRef<AbortController | undefined>(
    undefined,
  );

  useEffect(() => {
    evidenceRefreshRequest.current?.abort();
    evidenceRefreshRequest.current = undefined;
    setCurrentRequest(undefined);
    setError(undefined);

    if (requestId === undefined || requestId.length === 0) {
      setLoading(false);
      setError({
        message: "The request identifier is missing.",
        code: "request_id_required",
      });
      return;
    }

    const controller = new AbortController();
    setLoading(true);

    void apiRequest<RequestDetailResponse>(
      `/api/requests/${encodeURIComponent(requestId)}`,
      { signal: controller.signal },
    )
      .then((response) => {
        if (!controller.signal.aborted) {
          setCurrentRequest(response);
        }
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted && !isAbortError(requestError)) {
          setError(toRequestDetailError(requestError));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => {
      controller.abort();
      evidenceRefreshRequest.current?.abort();
      evidenceRefreshRequest.current = undefined;
    };
  }, [requestId]);

  function applyBusinessDecision(response: BusinessDecisionResponse) {
    setCurrentRequest((current) => {
      if (current === undefined || current.requestId !== response.requestId) {
        return current;
      }

      return {
        ...current,
        status: response.status,
        availableActions: current.availableActions.filter(
          (action) => action !== BUSINESS_DECISION_ACTION,
        ),
      };
    });
  }

  function refreshAuthoritativeEvidence(completedRequestId: string) {
    evidenceRefreshRequest.current?.abort();
    const controller = new AbortController();
    evidenceRefreshRequest.current = controller;

    void apiRequest<RequestDetailResponse>(
      `/api/requests/${encodeURIComponent(completedRequestId)}`,
      { signal: controller.signal },
    )
      .then((response) => {
        if (!controller.signal.aborted) {
          setCurrentRequest((current) =>
            current?.requestId === response.requestId ? response : current,
          );
        }
      })
      .catch((refreshError: unknown) => {
        if (!controller.signal.aborted && isAbortError(refreshError)) {
          return;
        }

        // The retry response remains authoritative for its status and grant.
        // A later route/session refresh will reload complete operation and audit evidence.
      })
      .finally(() => {
        if (evidenceRefreshRequest.current === controller) {
          evidenceRefreshRequest.current = undefined;
        }
      });
  }

  function applyDevOpsDecision(response: DevOpsDecisionResponse) {
    setCurrentRequest((current) => {
      if (current === undefined || current.requestId !== response.requestId) {
        return current;
      }

      return {
        ...current,
        status: response.status,
        availableActions: current.availableActions.filter(
          (action) => action !== DEVOPS_DECISION_ACTION,
        ),
      };
    });
  }

  function applyProvisioningRetry(response: ProvisioningRetryResponse) {
    setCurrentRequest((current) => {
      if (current === undefined || current.requestId !== response.requestId) {
        return current;
      }

      return {
        ...current,
        status: response.status,
        grant: response.grant,
        availableActions: current.availableActions.filter(
          (action) => action !== RETRY_PROVISIONING_ACTION,
        ),
      };
    });

    refreshAuthoritativeEvidence(response.requestId);
  }

  if (loading) {
    return (
      <main className="request-detail-page" aria-labelledby="request-loading-title">
        <h1 id="request-loading-title">Loading request details</h1>
      </main>
    );
  }

  if (error !== undefined || currentRequest === undefined) {
    const problem = error ?? {
      message: "The request details could not be loaded. Try again.",
      code: "unexpected_client_error",
    };

    return (
      <main className="request-detail-page" aria-labelledby="request-error-title">
        <div className="problem-summary" role="alert">
          <h1 id="request-error-title">Request details unavailable</h1>
          <p>{problem.message}</p>
          {problem.code !== undefined && (
            <p>
              Code: <span className="identifier">{problem.code}</span>
            </p>
          )}
          {problem.correlationId !== undefined && (
            <p>
              Correlation ID:{" "}
              <span className="identifier">{problem.correlationId}</span>
            </p>
          )}
        </div>
      </main>
    );
  }

  return (
    <main className="request-detail-page" aria-labelledby="request-detail-title">
      <header className="page-header">
        <p className="eyebrow">Immutable access request</p>
        <h1 id="request-detail-title">Request details</h1>
        <p>
          Human decisions and provisioning apply only to this request identifier
          and its unchanged submitted scope.
        </p>
        <p className="page-header__identifier">
          Request <span className="identifier">{currentRequest.requestId}</span>
        </p>
      </header>

      <RequestStatus status={currentRequest.status} />

      <div className="request-detail-layout">
        <div className="request-detail-evidence">
          <RequestSummary request={currentRequest} />
          <ValidationEvidence request={currentRequest} />
          <DecisionEvidence decisions={currentRequest.decisions} />
          <ProvisioningEvidence
            operation={currentRequest.provisioningOperation}
          />
          <GrantEvidence grant={currentRequest.grant} />
          <AuditTimeline auditEvents={currentRequest.auditEvents} />
        </div>

        <aside
          className="request-detail-actions"
          aria-labelledby="request-actions-title"
        >
          <header className="request-detail-actions__header">
            <p className="eyebrow">Current action</p>
            <h2 id="request-actions-title">Next governed action</h2>
            <p>
              Available actions come from the server. Every submitted decision
              is revalidated and authorized against the authenticated identity.
            </p>
          </header>
          <BusinessDecisionPanel
            requestId={currentRequest.requestId}
            availableActions={currentRequest.availableActions}
            onDecisionCompleted={applyBusinessDecision}
          />
          <DevOpsDecisionPanel
            requestId={currentRequest.requestId}
            availableActions={currentRequest.availableActions}
            onDecisionCompleted={applyDevOpsDecision}
          />
          <ProvisioningRetryPanel
            requestId={currentRequest.requestId}
            availableActions={currentRequest.availableActions}
            onRetryCompleted={applyProvisioningRetry}
          />
          {currentRequest.availableActions.length === 0 && (
            <p className="request-detail-actions__empty">
              No additional action is currently available to this identity.
            </p>
          )}
        </aside>
      </div>
    </main>
  );
}

function RequestSummary({ request }: { request: RequestDetailResponse }) {
  return (
    <section
      className="evidence-section"
      aria-labelledby="request-summary-title"
    >
      <h2 id="request-summary-title">Immutable request scope</h2>
      <dl className="evidence-list">
        <dt>Request ID</dt>
        <dd className="identifier">{request.requestId}</dd>
        <dt>Requester</dt>
        <dd className="identifier">{request.requesterId}</dd>
        <dt>Client</dt>
        <dd className="identifier">{request.clientId}</dd>
        <dt>Production environment</dt>
        <dd className="identifier">{request.environmentId}</dd>
        <dt>Requested role</dt>
        <dd className="identifier">{request.requestedRoleId}</dd>
        <dt>Access lifetime</dt>
        <dd>8 hours after activation</dd>
        <dt>Justification</dt>
        <dd>{request.justification}</dd>
        <dt>Incident</dt>
        <dd className="identifier">{request.incidentId ?? "None"}</dd>
        <dt>Submitted</dt>
        <dd>
          <Timestamp value={request.createdAt} />
        </dd>
        <dt>Last updated</dt>
        <dd>
          <Timestamp value={request.lastModifiedAt} />
        </dd>
      </dl>
    </section>
  );
}

function ValidationEvidence({ request }: { request: RequestDetailResponse }) {
  return (
    <section
      className="evidence-section"
      aria-labelledby="request-validation-title"
    >
      <h2 id="request-validation-title">Current validation</h2>
      {request.validation.isValid ? (
        <p className="validation-status validation-status--valid">
          The immutable request scope is valid against current authoritative data.
        </p>
      ) : (
        <>
          <p className="validation-status validation-status--invalid">
            Current authoritative data no longer validates every submitted value.
          </p>
          <ul className="validation-error-list">
            {request.validation.fieldErrors.map((fieldError) => (
              <li key={`${fieldError.field}-${fieldError.code}`}>
                <strong>{fieldError.field}</strong>: {fieldError.message}{" "}
                <span className="identifier">({fieldError.code})</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

function DecisionEvidence({
  decisions,
}: {
  decisions: ApprovalDecisionResponse[];
}) {
  return (
    <section
      className="evidence-section"
      aria-labelledby="approval-evidence-title"
    >
      <h2 id="approval-evidence-title">Approval evidence</h2>
      {decisions.length === 0 ? (
        <p>No human decision has been recorded.</p>
      ) : (
        <ol className="decision-evidence-list">
          {decisions.map((decision) => (
            <li key={decision.decisionId}>
              <article>
                <header>
                  <h3>{decision.stage} decision</h3>
                  <p>{decision.decision}</p>
                </header>
                <dl className="evidence-list">
                  <dt>Decision ID</dt>
                  <dd className="identifier">{decision.decisionId}</dd>
                  <dt>Approver</dt>
                  <dd className="identifier">{decision.approverId}</dd>
                  <dt>Approved role</dt>
                  <dd className="identifier">
                    {decision.approvedRoleId ?? "Not applicable"}
                  </dd>
                  <dt>Comment</dt>
                  <dd>{decision.comment ?? "No comment"}</dd>
                  <dt>Recorded</dt>
                  <dd>
                    <Timestamp value={decision.decidedAt} />
                  </dd>
                  <dt>Correlation ID</dt>
                  <dd className="identifier">{decision.correlationId}</dd>
                </dl>
              </article>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function ProvisioningEvidence({
  operation,
}: {
  operation: ProvisioningOperationResponse | null;
}) {
  return (
    <section
      className="evidence-section"
      aria-labelledby="provisioning-evidence-title"
    >
      <h2 id="provisioning-evidence-title">Provisioning evidence</h2>
      {operation === null ? (
        <p>No provisioning operation has been created.</p>
      ) : (
        <dl className="evidence-list">
          <dt>Operation and request ID</dt>
          <dd className="identifier">{operation.requestId}</dd>
          <dt>Status</dt>
          <dd>{operation.status}</dd>
          <dt>Environment</dt>
          <dd className="identifier">{operation.environmentId}</dd>
          <dt>Role</dt>
          <dd className="identifier">{operation.roleId}</dd>
          <dt>Attempt count</dt>
          <dd>{operation.attemptCount}</dd>
          <dt>Last outcome code</dt>
          <dd className="identifier">
            {operation.lastOutcomeCode ?? "No outcome recorded"}
          </dd>
          <dt>Created</dt>
          <dd>
            <Timestamp value={operation.createdAt} />
          </dd>
          <dt>Last attempted</dt>
          <dd>
            <Timestamp value={operation.lastAttemptAt} />
          </dd>
        </dl>
      )}
    </section>
  );
}

function GrantEvidence({ grant }: { grant: RequestGrantResponse | null }) {
  return (
    <section
      className="evidence-section"
      aria-labelledby="grant-evidence-title"
    >
      <h2 id="grant-evidence-title">Access grant</h2>
      {grant === null ? (
        <p>No access grant has been recorded.</p>
      ) : (
        <>
          <p
            className={
              grant.isExpired
                ? "grant-status grant-status--expired"
                : "grant-status grant-status--active"
            }
          >
            {grant.isExpired
              ? "Expired — the fixed access window has ended."
              : "Active — the fixed access window has not expired."}
          </p>
          <dl className="evidence-list">
            <dt>Grant ID</dt>
            <dd className="identifier">{grant.grantId}</dd>
            <dt>Request ID</dt>
            <dd className="identifier">{grant.requestId}</dd>
            <dt>Requester</dt>
            <dd className="identifier">{grant.requesterId}</dd>
            <dt>Environment</dt>
            <dd className="identifier">{grant.environmentId}</dd>
            <dt>Role</dt>
            <dd className="identifier">{grant.roleId}</dd>
            <dt>Outcome</dt>
            <dd>{grant.outcome}</dd>
            <dt>Activated</dt>
            <dd>
              <Timestamp value={grant.activatedAt} />
            </dd>
            <dt>Expires</dt>
            <dd>
              <Timestamp value={grant.expiresAt} />
            </dd>
            <dt>Correlation ID</dt>
            <dd className="identifier">{grant.correlationId}</dd>
          </dl>
        </>
      )}
    </section>
  );
}

function AuditTimeline({
  auditEvents,
}: {
  auditEvents: RequestAuditEventResponse[];
}) {
  return (
    <section
      className="evidence-section"
      aria-labelledby="audit-timeline-title"
    >
      <h2 id="audit-timeline-title">Audit timeline</h2>
      {auditEvents.length === 0 ? (
        <p>No audit events are visible.</p>
      ) : (
        <ol className="audit-timeline">
          {auditEvents.map((auditEvent) => (
            <li key={auditEvent.eventId}>
              <article>
                <header>
                  <h3>{auditEvent.eventType}</h3>
                  <Timestamp value={auditEvent.occurredAt} />
                </header>
                <dl className="evidence-list">
                  <dt>Event ID</dt>
                  <dd className="identifier">{auditEvent.eventId}</dd>
                  <dt>Actor</dt>
                  <dd className="identifier">
                    {auditEvent.actorId ?? "System"}
                  </dd>
                  <dt>Outcome code</dt>
                  <dd className="identifier">{auditEvent.outcomeCode}</dd>
                  <dt>Correlation ID</dt>
                  <dd className="identifier">{auditEvent.correlationId}</dd>
                </dl>
                <details>
                  <summary>Allowlisted event details</summary>
                  <pre>{JSON.stringify(auditEvent.details, null, 2)}</pre>
                </details>
              </article>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function ProvisioningRetryPanel({
  requestId,
  availableActions,
  onRetryCompleted,
}: {
  requestId: string;
  availableActions: readonly string[];
  onRetryCompleted: (response: ProvisioningRetryResponse) => void;
}) {
  const [pending, setPending] = useState(false);
  const [completed, setCompleted] = useState<ProvisioningRetryResponse>();
  const [error, setError] = useState<RequestDetailError>();
  const activeRequest = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    activeRequest.current?.abort();
    activeRequest.current = undefined;
    setPending(false);
    setCompleted(undefined);
    setError(undefined);

    return () => {
      activeRequest.current?.abort();
      activeRequest.current = undefined;
    };
  }, [requestId]);

  const canRetry = availableActions.includes(RETRY_PROVISIONING_ACTION);
  if (!canRetry && completed === undefined) {
    return null;
  }

  if (completed !== undefined) {
    return (
      <section
        className="provisioning-retry-panel provisioning-retry-panel--completed"
        aria-labelledby="provisioning-retry-title"
      >
        <h2 id="provisioning-retry-title">Provisioning retry</h2>
        <p role="status" aria-live="polite">
          Provisioning succeeded for the stored immutable scope. The single grant
          is active.
        </p>
        <p>
          Grant ID: <span className="identifier">{completed.grant.grantId}</span>
        </p>
      </section>
    );
  }

  async function retryProvisioning() {
    if (pending) {
      return;
    }

    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setPending(true);
    setError(undefined);

    try {
      const response = await apiRequest<ProvisioningRetryResponse>(
        `/api/requests/${encodeURIComponent(requestId)}/retry-provisioning`,
        {
          method: "POST",
          signal: controller.signal,
        },
      );

      if (controller.signal.aborted) {
        return;
      }

      setCompleted(response);
      onRetryCompleted(response);
    } catch (requestError: unknown) {
      if (!isAbortError(requestError)) {
        setError(toRetryError(requestError));
      }
    } finally {
      if (activeRequest.current === controller) {
        activeRequest.current = undefined;
        setPending(false);
      }
    }
  }

  return (
    <section
      className="provisioning-retry-panel"
      aria-labelledby="provisioning-retry-title"
    >
      <h2 id="provisioning-retry-title">Provisioning retry</h2>
      <p>
        Retry the failed operation with the same request ID and persisted approved
        scope. No role, environment, duration, approval, or actor values are sent.
      </p>

      {error !== undefined && (
        <div className="problem-summary" role="alert">
          <h3>Provisioning retry did not complete</h3>
          <p>{error.message}</p>
          {error.code !== undefined && (
            <p>
              Code: <span className="identifier">{error.code}</span>
            </p>
          )}
          {error.correlationId !== undefined && (
            <p>
              Correlation ID:{" "}
              <span className="identifier">{error.correlationId}</span>
            </p>
          )}
        </div>
      )}

      <button
        type="button"
        onClick={() => void retryProvisioning()}
        disabled={pending}
      >
        {pending ? "Retrying provisioning…" : "Retry provisioning"}
      </button>
    </section>
  );
}

function Timestamp({ value }: { value: string }) {
  const timestamp = new Date(value);
  const label = Number.isNaN(timestamp.getTime())
    ? value
    : utcTimestampFormatter.format(timestamp);

  return (
    <time className="timestamp" dateTime={value} title={value}>
      {label}
    </time>
  );
}

function toRequestDetailError(error: unknown): RequestDetailError {
  if (error instanceof ApiError) {
    return {
      message: error.detail,
      code: error.code,
      correlationId: error.correlationId,
    };
  }

  return {
    message: "The request details could not be loaded. Try again.",
    code: "unexpected_client_error",
  };
}

function toRetryError(error: unknown): RequestDetailError {
  if (error instanceof ApiError) {
    return {
      message: error.detail,
      code: error.code,
      correlationId: error.correlationId,
    };
  }

  return {
    message: "Provisioning could not be retried. Refresh the request and try again.",
    code: "unexpected_client_error",
  };
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}
