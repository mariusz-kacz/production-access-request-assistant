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
    return (
      <section
        className="devops-decision-panel devops-decision-panel--completed"
        aria-labelledby="devops-decision-title"
      >
        <h2 id="devops-decision-title">DevOps decision</h2>
        <p role="status" aria-live="polite">
          {completedDecision.status === "Active"
            ? "DevOps approval recorded. Synthetic access is active."
            : "DevOps rejection recorded. The request is rejected."}
        </p>

        {completedDecision.grant !== null && (
          <SafeGrantSummary grant={completedDecision.grant} />
        )}

        <p>Correlation ID: {completedDecision.correlationId}</p>
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
      className="devops-decision-panel"
      aria-labelledby="devops-decision-title"
    >
      <h2 id="devops-decision-title">DevOps decision</h2>
      <p>
        Approval provisions the exact business-approved role for the fixed access
        window. The server reloads and validates the approved request scope before
        provisioning.
      </p>

      {error !== undefined && (
        <div className="problem-summary" role="alert">
          <h3>Decision or provisioning did not complete</h3>
          <p>{error.message}</p>
          {error.provisioningOutcomeUnknown && (
            <p>
              Refresh the request before trying again. The server determines whether
              provisioning can be retried safely.
            </p>
          )}
          {error.code !== undefined && <p>Code: {error.code}</p>}
          {error.correlationId !== undefined && (
            <p>Correlation ID: {error.correlationId}</p>
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

      <div className="devops-decision-panel__actions">
        <button
          type="button"
          onClick={() => void submitDecision("Approve")}
          disabled={busy}
        >
          {pendingDecision === "Approve"
            ? "Approving and provisioning..."
            : "Approve and provision"}
        </button>
        <button
          type="button"
          onClick={() => void submitDecision("Reject")}
          disabled={busy}
        >
          {pendingDecision === "Reject" ? "Rejecting..." : "Reject request"}
        </button>
      </div>
    </section>
  );
}

function SafeGrantSummary({ grant }: { grant: DevOpsAccessGrant }) {
  return (
    <div className="devops-decision-panel__grant" aria-label="Active access grant">
      <h3>Active grant</h3>
      <dl>
        <dt>Grant ID</dt>
        <dd>{grant.grantId}</dd>
        <dt>Environment</dt>
        <dd>{grant.environmentId}</dd>
        <dt>Role</dt>
        <dd>{grant.roleId}</dd>
        <dt>Activated</dt>
        <dd>
          <time dateTime={grant.activatedAt}>{formatTimestamp(grant.activatedAt)}</time>
        </dd>
        <dt>Expires</dt>
        <dd>
          <time dateTime={grant.expiresAt}>{formatTimestamp(grant.expiresAt)}</time>
        </dd>
      </dl>
    </div>
  );
}

function formatTimestamp(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
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
