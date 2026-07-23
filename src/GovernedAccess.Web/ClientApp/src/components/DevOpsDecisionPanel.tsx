import { useEffect, useRef, useState } from "react";

import { ApiError, apiRequest } from "../api/client";

export const DEVOPS_DECISION_ACTION = "decideDevOpsRequest";

export type DevOpsDecision = "Approve" | "Reject";

export interface DevOpsDecisionRequest {
  decision: DevOpsDecision;
  comment?: string;
}

export interface DevOpsAccessGrant {
  grantId: string;
  environmentId: string;
  roleId: string;
  activatedAt: string;
  expiresAt: string;
}

export interface DevOpsDecisionResponse {
  requestId: string;
  status: "Active" | "Rejected";
  correlationId: string;
  grant: DevOpsAccessGrant | null;
}

export interface DevOpsDecisionPanelProps {
  requestId: string;
  availableActions: readonly string[];
  onDecisionCompleted: (response: DevOpsDecisionResponse) => void;
}

interface DecisionError {
  message: string;
  code?: string;
  correlationId?: string;
  provisioningOutcomeUnknown: boolean;
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

export function DevOpsDecisionPanel({
  requestId,
  availableActions,
  onDecisionCompleted,
}: DevOpsDecisionPanelProps) {
  const [comment, setComment] = useState("");
  const [pendingDecision, setPendingDecision] = useState<DevOpsDecision>();
  const [completedDecision, setCompletedDecision] =
    useState<DevOpsDecisionResponse>();
  const [error, setError] = useState<DecisionError>();
  const activeRequest = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    activeRequest.current?.abort();
    activeRequest.current = undefined;
    setComment("");
    setPendingDecision(undefined);
    setCompletedDecision(undefined);
    setError(undefined);

    return () => {
      activeRequest.current?.abort();
      activeRequest.current = undefined;
    };
  }, [requestId]);

  const canDecide = availableActions.includes(DEVOPS_DECISION_ACTION);

  if (!canDecide && completedDecision === undefined) {
    return null;
  }

  if (completedDecision !== undefined) {
    const active = completedDecision.status === "Active";

    return (
      <section
        className={`devops-decision-panel decision-panel decision-panel--completed decision-panel--${
          active ? "positive" : "critical"
        }`}
        aria-labelledby="devops-decision-title"
      >
        <header className="decision-panel__header">
          <p className="eyebrow">DevOps review</p>
          <h2 id="devops-decision-title">DevOps decision</h2>
        </header>
        <div
          className={`decision-panel__outcome decision-panel__outcome--${
            active ? "positive" : "critical"
          }`}
          role="status"
          aria-live="polite"
        >
          <strong>{active ? "Approved and provisioned" : "Rejected"}</strong>
          <span>
            {active
              ? "Access is active."
              : "Request closed."}
          </span>
        </div>

        {completedDecision.grant !== null && (
          <SafeGrantSummary grant={completedDecision.grant} />
        )}

        <dl className="decision-panel__result">
          <div>
            <dt>Correlation ID</dt>
            <dd className="identifier">{completedDecision.correlationId}</dd>
          </div>
        </dl>
      </section>
    );
  }

  const busy = pendingDecision !== undefined;

  async function submitDecision(decision: DevOpsDecision) {
    if (busy) {
      return;
    }

    const normalizedComment = comment.trim();
    if (normalizedComment.length > 1000) {
      setError({
        message: "The decision comment cannot exceed 1,000 characters.",
        code: "comment_too_long",
        provisioningOutcomeUnknown: false,
      });
      return;
    }

    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setPendingDecision(decision);
    setError(undefined);

    const body: DevOpsDecisionRequest =
      normalizedComment.length === 0
        ? { decision }
        : { decision, comment: normalizedComment };

    try {
      const response = await apiRequest<
        DevOpsDecisionResponse,
        DevOpsDecisionRequest
      >(`/api/requests/${requestId}/devops-decisions`, {
        method: "POST",
        body,
        signal: controller.signal,
      });

      if (controller.signal.aborted) {
        return;
      }

      setCompletedDecision(response);
      onDecisionCompleted(response);
    } catch (requestError: unknown) {
      if (!isAbortError(requestError)) {
        setError(toDecisionError(requestError));
      }
    } finally {
      if (activeRequest.current === controller) {
        activeRequest.current = undefined;
        setPendingDecision(undefined);
      }
    }
  }

  return (
    <section
      className="devops-decision-panel decision-panel"
      aria-labelledby="devops-decision-title"
      aria-busy={busy}
    >
      <header className="decision-panel__header">
        <p className="eyebrow">DevOps review</p>
        <h2 id="devops-decision-title">DevOps decision</h2>
        <p>Approve to provision the approved role for eight hours, or reject.</p>
      </header>

      <div className="decision-panel__scope-note">
        <strong>Access</strong>
        <span>
          Approved role · eight hours
        </span>
      </div>

      {error !== undefined && (
        <div className="problem-summary" role="alert">
          <h3>Decision or provisioning did not complete</h3>
          <p>{error.message}</p>
          {error.provisioningOutcomeUnknown && (
            <p>
              Refresh before trying again; the operation may have completed.
            </p>
          )}
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

      <div className="form-field">
        <label htmlFor="devops-decision-comment">
          Decision comment (optional)
        </label>
        <p id="devops-decision-comment-hint">Maximum 1,000 characters.</p>
        <textarea
          id="devops-decision-comment"
          name="comment"
          rows={4}
          maxLength={1000}
          value={comment}
          aria-describedby="devops-decision-comment-hint"
          onChange={(event) => {
            setComment(event.target.value);
            setError(undefined);
          }}
          disabled={busy}
        />
      </div>

      {busy && (
        <p
          className="decision-panel__activity decision-panel__activity--pending"
          role="status"
          aria-live="polite"
        >
          {pendingDecision === "Approve"
            ? "Approving and provisioning…"
            : "Recording DevOps rejection…"}
        </p>
      )}

      <div className="devops-decision-panel__actions decision-panel__actions">
        <button
          type="button"
          className="decision-action decision-action--approve"
          onClick={() => void submitDecision("Approve")}
          disabled={busy}
        >
          {pendingDecision === "Approve"
            ? "Approving and provisioning…"
            : "Approve and provision"}
        </button>
        <button
          type="button"
          className="decision-action decision-action--reject"
          onClick={() => void submitDecision("Reject")}
          disabled={busy}
        >
          {pendingDecision === "Reject" ? "Rejecting…" : "Reject request"}
        </button>
      </div>
    </section>
  );
}

function SafeGrantSummary({ grant }: { grant: DevOpsAccessGrant }) {
  return (
    <div className="safe-grant" aria-label="Active access grant">
      <header className="safe-grant__header">
        <p className="eyebrow">Grant</p>
        <h3>Active grant</h3>
      </header>
      <dl className="safe-grant__evidence">
        <div>
          <dt>Grant ID</dt>
          <dd className="identifier">{grant.grantId}</dd>
        </div>
        <div>
          <dt>Environment</dt>
          <dd className="identifier">{grant.environmentId}</dd>
        </div>
        <div>
          <dt>Role</dt>
          <dd className="identifier">{grant.roleId}</dd>
        </div>
        <div>
          <dt>Activated</dt>
          <dd>
            <GrantTimestamp value={grant.activatedAt} />
          </dd>
        </div>
        <div>
          <dt>Expires</dt>
          <dd>
            <GrantTimestamp value={grant.expiresAt} />
          </dd>
        </div>
      </dl>
    </div>
  );
}

function GrantTimestamp({ value }: { value: string }) {
  const parsed = new Date(value);
  const label = Number.isNaN(parsed.getTime())
    ? value
    : utcTimestampFormatter.format(parsed);

  return (
    <time className="timestamp" dateTime={value} title={value}>
      {label}
    </time>
  );
}

function toDecisionError(error: unknown): DecisionError {
  if (error instanceof ApiError) {
    return {
      message: error.detail,
      code: error.code,
      correlationId: error.correlationId,
      provisioningOutcomeUnknown: error.status === 503,
    };
  }

  return {
    message: "The DevOps decision could not be completed. Try again.",
    code: "unexpected_client_error",
    provisioningOutcomeUnknown: true,
  };
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}
