import { useEffect, useRef, useState } from "react";

import { ApiError, apiRequest } from "../api/client";

export const BUSINESS_DECISION_ACTION = "decideBusinessRequest";

export type BusinessDecision = "Approve" | "Reject";

export interface BusinessDecisionRequest {
  decision: BusinessDecision;
  comment?: string;
}

export interface BusinessDecisionResponse {
  requestId: string;
  status: "AwaitingDevOpsApproval" | "Rejected";
  correlationId: string;
}

export interface BusinessDecisionPanelProps {
  requestId: string;
  availableActions: readonly string[];
  onDecisionCompleted: (response: BusinessDecisionResponse) => void;
}

interface DecisionError {
  message: string;
  code?: string;
  correlationId?: string;
}

export function BusinessDecisionPanel({
  requestId,
  availableActions,
  onDecisionCompleted,
}: BusinessDecisionPanelProps) {
  const [comment, setComment] = useState("");
  const [pendingDecision, setPendingDecision] = useState<BusinessDecision>();
  const [completedDecision, setCompletedDecision] =
    useState<BusinessDecisionResponse>();
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

  const canDecide = availableActions.includes(BUSINESS_DECISION_ACTION);

  if (!canDecide && completedDecision === undefined) {
    return null;
  }

  if (completedDecision !== undefined) {
    return (
      <section
        className="business-decision-panel business-decision-panel--completed"
        aria-labelledby="business-decision-title"
      >
        <h2 id="business-decision-title">Business decision</h2>
        <p role="status" aria-live="polite">
          {completedDecision.status === "AwaitingDevOpsApproval"
            ? "Business approval recorded. The request is awaiting DevOps approval."
            : "Business rejection recorded. The request is rejected."}
        </p>
        <p>Correlation ID: {completedDecision.correlationId}</p>
      </section>
    );
  }

  const busy = pendingDecision !== undefined;

  async function submitDecision(decision: BusinessDecision) {
    if (busy) {
      return;
    }

    const normalizedComment = comment.trim();
    if (normalizedComment.length > 1000) {
      setError({
        message: "The decision comment cannot exceed 1,000 characters.",
        code: "comment_too_long",
      });
      return;
    }

    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setPendingDecision(decision);
    setError(undefined);

    const body: BusinessDecisionRequest =
      normalizedComment.length === 0
        ? { decision }
        : { decision, comment: normalizedComment };

    try {
      const response = await apiRequest<
        BusinessDecisionResponse,
        BusinessDecisionRequest
      >(`/api/requests/${requestId}/business-decisions`, {
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
      className="business-decision-panel"
      aria-labelledby="business-decision-title"
    >
      <h2 id="business-decision-title">Business decision</h2>
      <p>
        Approve or reject this immutable request scope. The server verifies your
        authenticated identity, environment responsibility, and the current request
        state before recording the decision.
      </p>

      {error !== undefined && (
        <div className="problem-summary" role="alert">
          <h3>Decision not recorded</h3>
          <p>{error.message}</p>
          {error.code !== undefined && <p>Code: {error.code}</p>}
          {error.correlationId !== undefined && (
            <p>Correlation ID: {error.correlationId}</p>
          )}
        </div>
      )}

      <div className="form-field">
        <label htmlFor="business-decision-comment">
          Decision comment (optional)
        </label>
        <p id="business-decision-comment-hint">Maximum 1,000 characters.</p>
        <textarea
          id="business-decision-comment"
          name="comment"
          rows={4}
          maxLength={1000}
          value={comment}
          aria-describedby="business-decision-comment-hint"
          onChange={(event) => {
            setComment(event.target.value);
            setError(undefined);
          }}
          disabled={busy}
        />
      </div>

      <div className="business-decision-panel__actions">
        <button
          type="button"
          onClick={() => void submitDecision("Approve")}
          disabled={busy}
        >
          {pendingDecision === "Approve" ? "Approving..." : "Approve request"}
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

function toDecisionError(error: unknown): DecisionError {
  if (error instanceof ApiError) {
    return {
      message: error.detail,
      code: error.code,
      correlationId: error.correlationId,
    };
  }

  return {
    message: "The business decision could not be recorded. Try again.",
    code: "unexpected_client_error",
  };
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}
