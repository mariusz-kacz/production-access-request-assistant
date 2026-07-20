import { useEffect, useState } from "react";
import { useParams } from "react-router";

import { ApiError, apiRequest } from "../api/client";
import type { RequestDetailResponse } from "../api/contracts";
import {
  BUSINESS_DECISION_ACTION,
  BusinessDecisionPanel,
  type BusinessDecisionResponse,
} from "../components/BusinessDecisionPanel";

interface RequestDetailError {
  message: string;
  code?: string;
  correlationId?: string;
}

export function RequestDetailPage() {
  const { requestId } = useParams<{ requestId: string }>();
  const [currentRequest, setCurrentRequest] =
    useState<RequestDetailResponse>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<RequestDetailError>();

  useEffect(() => {
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

    return () => controller.abort();
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
          {problem.code !== undefined && <p>Code: {problem.code}</p>}
          {problem.correlationId !== undefined && (
            <p>Correlation ID: {problem.correlationId}</p>
          )}
        </div>
      </main>
    );
  }

  return (
    <main className="request-detail-page" aria-labelledby="request-detail-title">
      <header>
        <p className="eyebrow">Immutable access request</p>
        <h1 id="request-detail-title">Request details</h1>
        <p>
          Human decisions apply only to this request identifier and its unchanged
          submitted scope.
        </p>
      </header>

      <section aria-labelledby="request-summary-title">
        <h2 id="request-summary-title">Request summary</h2>
        <dl>
          <dt>Request ID</dt>
          <dd>{currentRequest.requestId}</dd>
          <dt>Status</dt>
          <dd aria-live="polite">{currentRequest.status}</dd>
          <dt>Requester</dt>
          <dd>{currentRequest.requesterId}</dd>
          <dt>Client</dt>
          <dd>{currentRequest.clientId}</dd>
          <dt>Production environment</dt>
          <dd>{currentRequest.environmentId}</dd>
          <dt>Requested role</dt>
          <dd>{currentRequest.requestedRoleId}</dd>
          <dt>Access lifetime</dt>
          <dd>8 hours after activation</dd>
          <dt>Justification</dt>
          <dd>{currentRequest.justification}</dd>
          <dt>Incident</dt>
          <dd>{currentRequest.incidentId ?? "None"}</dd>
          <dt>Submitted</dt>
          <dd>{currentRequest.createdAt}</dd>
          <dt>Last updated</dt>
          <dd>{currentRequest.lastModifiedAt}</dd>
        </dl>
      </section>

      <BusinessDecisionPanel
        requestId={currentRequest.requestId}
        availableActions={currentRequest.availableActions}
        onDecisionCompleted={applyBusinessDecision}
      />
    </main>
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

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}
