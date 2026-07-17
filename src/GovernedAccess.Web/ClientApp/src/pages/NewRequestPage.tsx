import {
  type FormEvent,
  useEffect,
  useRef,
  useState,
} from "react";

import { ApiError, apiRequest } from "../api/client";
import {
  type AccessRequestDraft,
  type ApiFieldError,
  type CreateAccessRequestRequest,
  type CreateAccessRequestResponse,
  type PrepareRequestDraftRequest,
  type PrepareRequestDraftResponse,
  type ProductionRole,
  productionRoles,
} from "../api/contracts";

type RequestActivity = "preparing" | "submitting";

interface DraftForm {
  clientId: string;
  environmentId: string;
  requestedRole: ProductionRole | "";
  durationMinutes: string;
  justification: string;
  incidentId: string;
}

interface PageError {
  message: string;
  code?: string;
  correlationId?: string;
}

type FieldErrors = Record<string, string[]>;

const emptyDraftForm: DraftForm = {
  clientId: "",
  environmentId: "",
  requestedRole: "",
  durationMinutes: "",
  justification: "",
  incidentId: "",
};

const preparationOutcomeMessages: Record<
  Exclude<PrepareRequestDraftResponse["outcome"], "Prepared" | "Incomplete">,
  string
> = {
  MalformedModelOutput:
    "Draft assistance returned an unreadable result. Complete the structured form manually or revise the description and try again.",
  Timeout:
    "Draft assistance timed out. Complete the structured form manually or try again.",
  Cancelled:
    "Draft preparation was cancelled. Complete the structured form manually or try again.",
  Unavailable:
    "Draft assistance is unavailable. Complete the structured form manually or try again later.",
};

export function NewRequestPage() {
  const [intent, setIntent] = useState("");
  const [intentError, setIntentError] = useState<string>();
  const [draft, setDraft] = useState<DraftForm>();
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [pageError, setPageError] = useState<PageError>();
  const [preparationMessage, setPreparationMessage] = useState<string>();
  const [activity, setActivity] = useState<RequestActivity>();
  const [createdRequest, setCreatedRequest] =
    useState<CreateAccessRequestResponse>();
  const activeRequest = useRef<AbortController | undefined>(undefined);

  useEffect(
    () => () => {
      activeRequest.current?.abort();
      activeRequest.current = undefined;
    },
    [],
  );

  async function prepareDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const normalizedIntent = intent.trim();
    if (normalizedIntent.length === 0) {
      setIntentError("Describe the temporary production access you need.");
      return;
    }

    setIntentError(undefined);
    setPageError(undefined);
    setPreparationMessage(undefined);
    setCreatedRequest(undefined);
    setDraft(undefined);
    setFieldErrors({});

    const controller = beginRequest("preparing");
    try {
      const response = await apiRequest<
        PrepareRequestDraftResponse,
        PrepareRequestDraftRequest
      >("/api/request-drafts/prepare", {
        method: "POST",
        body: { intent: normalizedIntent },
        signal: controller.signal,
      });

      if (controller.signal.aborted) {
        return;
      }

      if (response.outcome === "Prepared") {
        setDraft(toDraftForm(response.draft));
        setPreparationMessage(
          "Draft prepared. Review every value before submitting it for human approval.",
        );
        return;
      }

      if (response.outcome === "Incomplete") {
        const form = toDraftForm(response.draft);
        setDraft(form);
        setFieldErrors(validateDraft(form).fieldErrors);
        setPreparationMessage(
          "Draft preparation needs more information. Complete the highlighted fields before submission.",
        );
        return;
      }

      const form = { ...emptyDraftForm };
      setDraft(form);
      setFieldErrors(validateDraft(form).fieldErrors);
      setPreparationMessage(preparationOutcomeMessages[response.outcome]);
    } catch (error: unknown) {
      if (!isAbortError(error)) {
        applyRequestError(error);
      }
    } finally {
      finishRequest(controller);
    }
  }

  async function submitRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (draft === undefined) {
      return;
    }

    setPageError(undefined);
    const validation = validateDraft(draft);
    setFieldErrors(validation.fieldErrors);
    if (validation.request === undefined) {
      setPageError({
        message: "Correct the highlighted fields before submitting the request.",
        code: "client_validation_failed",
      });
      return;
    }

    const controller = beginRequest("submitting");
    try {
      const response = await apiRequest<
        CreateAccessRequestResponse,
        CreateAccessRequestRequest
      >("/api/requests", {
        method: "POST",
        body: validation.request,
        signal: controller.signal,
      });

      if (!controller.signal.aborted) {
        setCreatedRequest(response);
        setPreparationMessage(undefined);
        setFieldErrors({});
      }
    } catch (error: unknown) {
      if (!isAbortError(error)) {
        applyRequestError(error);
      }
    } finally {
      finishRequest(controller);
    }
  }

  function beginRequest(nextActivity: RequestActivity): AbortController {
    activeRequest.current?.abort();
    const controller = new AbortController();
    activeRequest.current = controller;
    setActivity(nextActivity);
    return controller;
  }

  function finishRequest(controller: AbortController) {
    if (activeRequest.current === controller) {
      activeRequest.current = undefined;
      setActivity(undefined);
    }
  }

  function applyRequestError(error: unknown) {
    if (error instanceof ApiError) {
      setPageError({
        message: error.detail,
        code: error.code,
        correlationId: error.correlationId,
      });
      setFieldErrors(groupFieldErrors(error.fieldErrors));
      return;
    }

    setPageError({
      message: "The request could not be completed. Try again.",
      code: "unexpected_client_error",
    });
  }

  function updateDraft<Field extends keyof DraftForm>(
    field: Field,
    value: DraftForm[Field],
  ) {
    setDraft((current) =>
      current === undefined ? current : { ...current, [field]: value },
    );
    setFieldErrors((current) => {
      if (!(field in current)) {
        return current;
      }

      const next = { ...current };
      delete next[field];
      return next;
    });
  }

  function startAnotherRequest() {
    activeRequest.current?.abort();
    activeRequest.current = undefined;
    setIntent("");
    setIntentError(undefined);
    setDraft(undefined);
    setFieldErrors({});
    setPageError(undefined);
    setPreparationMessage(undefined);
    setActivity(undefined);
    setCreatedRequest(undefined);
  }

  const busy = activity !== undefined;

  return (
    <main className="new-request-page" aria-labelledby="new-request-title">
      <header>
        <p className="eyebrow">Temporary production access</p>
        <h1 id="new-request-title">Prepare a governed access request</h1>
        <p>
          Describe the work, review the structured scope, and submit it for two
          human approval stages. Preparing a draft never grants access.
        </p>
      </header>

      <section aria-labelledby="request-intent-title">
        <h2 id="request-intent-title">1. Describe the need</h2>
        <form onSubmit={prepareDraft}>
          <label htmlFor="request-intent">Access request description</label>
          <p id="request-intent-hint">
            Include the client, production environment, role, duration,
            justification, and incident when applicable.
          </p>
          <textarea
            id="request-intent"
            name="intent"
            rows={5}
            value={intent}
            aria-describedby={
              intentError === undefined
                ? "request-intent-hint"
                : "request-intent-hint request-intent-error"
            }
            aria-invalid={intentError !== undefined}
            onChange={(event) => {
              setIntent(event.target.value);
              setIntentError(undefined);
            }}
            disabled={busy}
          />
          {intentError !== undefined && (
            <p id="request-intent-error" className="field-error">
              {intentError}
            </p>
          )}
          <button type="submit" disabled={busy}>
            {activity === "preparing" ? "Preparing draft…" : "Prepare draft"}
          </button>
        </form>
      </section>

      {pageError !== undefined && (
        <div className="problem-summary" role="alert">
          <h2>Request not completed</h2>
          <p>{pageError.message}</p>
          {pageError.code !== undefined && <p>Code: {pageError.code}</p>}
          {pageError.correlationId !== undefined && (
            <p>Correlation ID: {pageError.correlationId}</p>
          )}
        </div>
      )}

      {preparationMessage !== undefined && (
        <p className="preparation-status" role="status" aria-live="polite">
          {preparationMessage}
        </p>
      )}

      {draft !== undefined && createdRequest === undefined && (
        <section aria-labelledby="draft-review-title">
          <h2 id="draft-review-title">2. Review and correct the draft</h2>
          <p>
            These values are untrusted draft input. The server validates every
            identifier and business rule against current stored data when you
            submit.
          </p>

          <form onSubmit={submitRequest} noValidate>
            <DraftTextField
              id="client-id"
              label="Client ID"
              value={draft.clientId}
              errors={fieldErrors.clientId}
              onChange={(value) => updateDraft("clientId", value)}
              disabled={busy}
            />

            <DraftTextField
              id="environment-id"
              label="Production environment ID"
              value={draft.environmentId}
              errors={fieldErrors.environmentId}
              onChange={(value) => updateDraft("environmentId", value)}
              disabled={busy}
            />

            <div className="form-field">
              <label htmlFor="requested-role">Requested role</label>
              <select
                id="requested-role"
                name="requestedRole"
                value={draft.requestedRole}
                aria-invalid={fieldErrors.requestedRole !== undefined}
                aria-describedby={errorDescriptionId(
                  "requested-role",
                  fieldErrors.requestedRole,
                )}
                onChange={(event) =>
                  updateDraft(
                    "requestedRole",
                    event.target.value as ProductionRole | "",
                  )
                }
                disabled={busy}
              >
                <option value="">Select a role</option>
                {productionRoles.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
              <FieldErrorList
                id="requested-role-errors"
                errors={fieldErrors.requestedRole}
              />
            </div>

            <div className="form-field">
              <label htmlFor="duration-minutes">Duration in minutes</label>
              <p id="duration-minutes-hint">
                DevOps may reduce this duration but cannot increase it.
              </p>
              <input
                id="duration-minutes"
                name="durationMinutes"
                type="number"
                min="1"
                step="1"
                inputMode="numeric"
                value={draft.durationMinutes}
                aria-invalid={fieldErrors.durationMinutes !== undefined}
                aria-describedby={descriptionIds(
                  "duration-minutes-hint",
                  "duration-minutes-errors",
                  fieldErrors.durationMinutes,
                )}
                onChange={(event) =>
                  updateDraft("durationMinutes", event.target.value)
                }
                disabled={busy}
              />
              <FieldErrorList
                id="duration-minutes-errors"
                errors={fieldErrors.durationMinutes}
              />
            </div>

            <div className="form-field">
              <label htmlFor="justification">Justification</label>
              <p id="justification-hint">Use 10 to 2,000 characters.</p>
              <textarea
                id="justification"
                name="justification"
                rows={5}
                maxLength={2000}
                value={draft.justification}
                aria-invalid={fieldErrors.justification !== undefined}
                aria-describedby={descriptionIds(
                  "justification-hint",
                  "justification-errors",
                  fieldErrors.justification,
                )}
                onChange={(event) =>
                  updateDraft("justification", event.target.value)
                }
                disabled={busy}
              />
              <FieldErrorList
                id="justification-errors"
                errors={fieldErrors.justification}
              />
            </div>

            <DraftTextField
              id="incident-id"
              label="Incident ID (optional)"
              value={draft.incidentId}
              errors={fieldErrors.incidentId}
              onChange={(value) => updateDraft("incidentId", value)}
              disabled={busy}
            />

            <button type="submit" disabled={busy}>
              {activity === "submitting"
                ? "Validating and submitting…"
                : "Validate and submit request"}
            </button>
          </form>
        </section>
      )}

      {createdRequest !== undefined && (
        <section className="submission-success" aria-labelledby="request-created-title">
          <h2 id="request-created-title">Request submitted</h2>
          <p>
            Request <strong>{createdRequest.requestId}</strong> is immutable and is
            awaiting business approval. No access has been granted. Submit a new
            request if this scope needs correction.
          </p>
          <p>Correlation ID: {createdRequest.correlationId}</p>
          <p>
            <a href={`/requests/${createdRequest.requestId}`}>View request details</a>
          </p>
          <button type="button" onClick={startAnotherRequest}>
            Prepare another request
          </button>
        </section>
      )}
    </main>
  );
}

interface DraftTextFieldProps {
  id: string;
  label: string;
  value: string;
  errors?: string[];
  disabled: boolean;
  onChange: (value: string) => void;
}

function DraftTextField({
  id,
  label,
  value,
  errors,
  disabled,
  onChange,
}: DraftTextFieldProps) {
  return (
    <div className="form-field">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        name={toCamelCaseName(id)}
        type="text"
        value={value}
        aria-invalid={errors !== undefined}
        aria-describedby={errorDescriptionId(id, errors)}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
      />
      <FieldErrorList id={`${id}-errors`} errors={errors} />
    </div>
  );
}

function FieldErrorList({ id, errors }: { id: string; errors?: string[] }) {
  if (errors === undefined) {
    return null;
  }

  return (
    <ul id={id} className="field-errors">
      {errors.map((error) => (
        <li key={error}>{error}</li>
      ))}
    </ul>
  );
}

function toDraftForm(draft: AccessRequestDraft): DraftForm {
  return {
    clientId: draft.clientId ?? "",
    environmentId: draft.environmentId ?? "",
    requestedRole: draft.requestedRole ?? "",
    durationMinutes:
      draft.durationMinutes === null ? "" : String(draft.durationMinutes),
    justification: draft.justification ?? "",
    incidentId: draft.incidentId ?? "",
  };
}

function validateDraft(form: DraftForm): {
  request?: CreateAccessRequestRequest;
  fieldErrors: FieldErrors;
} {
  const fieldErrors: FieldErrors = {};
  const clientId = form.clientId.trim();
  const environmentId = form.environmentId.trim();
  const justification = form.justification.trim();
  const incidentId = form.incidentId.trim();
  const durationText = form.durationMinutes.trim();

  requireValue(fieldErrors, "clientId", clientId, "Client ID is required.");
  requireValue(
    fieldErrors,
    "environmentId",
    environmentId,
    "Production environment ID is required.",
  );

  if (form.requestedRole === "") {
    fieldErrors.requestedRole = ["Select a requested role."];
  }

  const durationMinutes = Number(durationText);
  if (
    durationText.length === 0 ||
    !Number.isInteger(durationMinutes) ||
    durationMinutes <= 0
  ) {
    fieldErrors.durationMinutes = [
      "Duration must be a positive whole number of minutes.",
    ];
  }

  if (justification.length < 10) {
    fieldErrors.justification = [
      "Justification must contain at least 10 characters.",
    ];
  } else if (justification.length > 2000) {
    fieldErrors.justification = [
      "Justification cannot exceed 2,000 characters.",
    ];
  }

  if (Object.keys(fieldErrors).length > 0 || form.requestedRole === "") {
    return { fieldErrors };
  }

  return {
    fieldErrors,
    request: {
      clientId,
      environmentId,
      requestedRole: form.requestedRole,
      durationMinutes,
      justification,
      incidentId: incidentId.length === 0 ? undefined : incidentId,
    },
  };
}

function requireValue(
  errors: FieldErrors,
  field: string,
  value: string,
  message: string,
) {
  if (value.length === 0) {
    errors[field] = [message];
  }
}

function groupFieldErrors(errors?: ApiFieldError[]): FieldErrors {
  if (errors === undefined) {
    return {};
  }

  return errors.reduce<FieldErrors>((grouped, error) => {
    grouped[error.field] = [...(grouped[error.field] ?? []), error.message];
    return grouped;
  }, {});
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException || error instanceof Error) &&
    error.name === "AbortError"
  );
}

function errorDescriptionId(id: string, errors?: string[]): string | undefined {
  return errors === undefined ? undefined : `${id}-errors`;
}

function descriptionIds(
  hintId: string,
  errorId: string,
  errors?: string[],
): string {
  return errors === undefined ? hintId : `${hintId} ${errorId}`;
}

function toCamelCaseName(id: string): string {
  return id.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase());
}
