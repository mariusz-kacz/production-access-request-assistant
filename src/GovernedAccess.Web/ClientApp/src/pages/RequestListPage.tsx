import { useEffect, useState } from "react";
import { Link } from "react-router";

import { ApiError, apiRequest } from "../api/client";
import type {
  RequestListItemResponse,
  RequestListResponse,
  RequestStatus,
} from "../api/contracts";

interface RequestListError {
  message: string;
  code?: string;
  correlationId?: string;
}

const statusOptions: ReadonlyArray<{
  value: RequestStatus;
  label: string;
}> = [
  {
    value: "AwaitingBusinessApproval",
    label: "Awaiting business approval",
  },
  {
    value: "AwaitingDevOpsApproval",
    label: "Awaiting DevOps approval",
  },
  {
    value: "ProvisioningFailed",
    label: "Provisioning failed",
  },
  { value: "Active", label: "Active" },
  { value: "Rejected", label: "Rejected" },
];

const statusLabels = Object.fromEntries(
  statusOptions.map(({ value, label }) => [value, label]),
) as Record<RequestStatus, string>;

const statusTones: Record<
  RequestStatus,
  "pending" | "critical" | "positive"
> = {
  AwaitingBusinessApproval: "pending",
  AwaitingDevOpsApproval: "pending",
  ProvisioningFailed: "critical",
  Active: "positive",
  Rejected: "critical",
};

const utcTimestampFormatter = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "UTC",
  timeZoneName: "short",
});

export function RequestListPage({
  canCreateRequest,
}: {
  canCreateRequest: boolean;
}) {
  const [statusFilter, setStatusFilter] = useState<RequestStatus | "">("");
  const [requests, setRequests] = useState<RequestListItemResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<RequestListError>();

  useEffect(() => {
    const controller = new AbortController();
    const path =
      statusFilter === ""
        ? "/api/requests"
        : `/api/requests?status=${encodeURIComponent(statusFilter)}`;

    setLoading(true);
    setError(undefined);
    setRequests([]);

    void apiRequest<RequestListResponse>(path, {
      signal: controller.signal,
    })
      .then((response) => {
        if (!controller.signal.aborted) {
          setRequests(response.items);
        }
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted && !isAbortError(requestError)) {
          setError(toRequestListError(requestError));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => controller.abort();
  }, [statusFilter]);

  return (
    <main className="request-list-page" aria-labelledby="request-list-title">
      <header className="page-header">
        <div className="page-header__title">
          <p className="eyebrow">Production access</p>
          <h1 id="request-list-title">Access requests</h1>
          <p>Records assigned to or submitted by the acting identity.</p>
        </div>
        {canCreateRequest && (
          <Link className="button-link button-link--primary" to="/requests/new">
            New request
          </Link>
        )}
      </header>

      <section className="request-list-filter" aria-labelledby="filter-title">
        <div className="request-list-filter__intro">
          <h2 id="filter-title">Filter</h2>
          <p>Case state</p>
        </div>
        <div className="request-list-filter__control">
          <label htmlFor="request-status-filter">Status</label>
          <select
            id="request-status-filter"
            value={statusFilter}
            onChange={(event) =>
              setStatusFilter(event.target.value as RequestStatus | "")
            }
          >
            <option value="">All statuses</option>
            {statusOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
      </section>

      <section className="request-list-results" aria-labelledby="results-title">
        <header className="request-list-results__header">
          <h2 id="results-title">Register</h2>
          {!loading && error === undefined && (
            <p aria-live="polite">
              {requests.length.toString().padStart(2, "0")}{" "}
              {requests.length === 1 ? "record" : "records"}
            </p>
          )}
        </header>

        {loading && (
          <p className="loading-message" role="status">
            Loading relevant requests…
          </p>
        )}

        {!loading && error !== undefined && (
          <div className="problem-summary" role="alert">
            <h3>Requests unavailable</h3>
            <p>{error.message}</p>
            {error.code !== undefined && <p>Code: {error.code}</p>}
            {error.correlationId !== undefined && (
              <p>Correlation ID: {error.correlationId}</p>
            )}
          </div>
        )}

        {!loading && error === undefined && requests.length === 0 && (
          <p className="empty-state">
            {statusFilter === ""
              ? "No requests to show."
              : `No requests match “${statusLabels[statusFilter]}”.`}
          </p>
        )}

        {!loading && error === undefined && requests.length > 0 && (
          <ul className="request-record-list">
            {requests.map((request) => (
              <RequestListItem key={request.requestId} request={request} />
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}

function RequestListItem({
  request,
}: {
  request: RequestListItemResponse;
}) {
  return (
    <li className="request-record">
      <article aria-labelledby={`request-${request.requestId}`}>
        <header className="request-record__header">
          <div className="request-record__identity">
            <p className="request-record__label">Request</p>
            <h3 id={`request-${request.requestId}`}>
              <Link
                className="identifier"
                to={`/requests/${encodeURIComponent(request.requestId)}`}
              >
                {request.requestId}
              </Link>
            </h3>
          </div>
          <span
            className={`status-label status-label--${statusTones[request.status]}`}
          >
            {statusLabels[request.status]}
          </span>
        </header>

        <dl className="request-record__scope">
          <div>
            <dt>Client</dt>
            <dd className="identifier">{request.clientId}</dd>
          </div>
          <div>
            <dt>Production environment</dt>
            <dd className="identifier">{request.environmentId}</dd>
          </div>
          <div>
            <dt>Requester</dt>
            <dd className="identifier">{request.requesterId}</dd>
          </div>
          <div>
            <dt>Last updated</dt>
            <dd>
              <time
                dateTime={request.lastModifiedAt}
                title={request.lastModifiedAt}
              >
                {formatTimestamp(request.lastModifiedAt)}
              </time>
            </dd>
          </div>
        </dl>

        <footer className="request-record__footer">
          <span
            className={
              request.actionable
                ? "action-indicator action-indicator--required"
                : "action-indicator"
            }
          >
            {request.actionable ? "Action required" : "No action required"}
          </span>
          <Link to={`/requests/${encodeURIComponent(request.requestId)}`}>
            Open request
          </Link>
        </footer>
      </article>
    </li>
  );
}

function formatTimestamp(value: string): string {
  const timestamp = new Date(value);
  return Number.isNaN(timestamp.getTime())
    ? value
    : utcTimestampFormatter.format(timestamp);
}

function toRequestListError(error: unknown): RequestListError {
  if (error instanceof ApiError) {
    return {
      message: error.detail,
      code: error.code,
      correlationId: error.correlationId,
    };
  }

  return {
    message: "The request list could not be loaded. Try again.",
    code: "unexpected_client_error",
  };
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}
