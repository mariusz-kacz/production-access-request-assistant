import type { RequestStatus as RequestStatusValue } from "../api/contracts";

type StatusTone = "pending" | "critical" | "positive";
type WorkflowStepState = "complete" | "current" | "upcoming";

interface StatusPresentation {
  label: string;
  tone: StatusTone;
  toneLabel: string;
  workflowPoint: string;
  description: string;
}

interface WorkflowStep {
  label: string;
  state: WorkflowStepState;
}

const statusPresentations: Record<RequestStatusValue, StatusPresentation> = {
  AwaitingBusinessApproval: {
    label: "Awaiting business approval",
    tone: "pending",
    toneLabel: "Pending",
    workflowPoint: "Business review",
    description:
      "The configured business approver must review the immutable request scope.",
  },
  AwaitingDevOpsApproval: {
    label: "Awaiting DevOps approval",
    tone: "pending",
    toneLabel: "Pending",
    workflowPoint: "DevOps review",
    description:
      "Business approval is recorded. DevOps must review the approved role and duration.",
  },
  ProvisioningFailed: {
    label: "Provisioning failed",
    tone: "critical",
    toneLabel: "Recovery required",
    workflowPoint: "Provisioning recovery",
    description:
      "The approved scope is unchanged, but the deterministic provisioning operation needs attention.",
  },
  Active: {
    label: "Active",
    tone: "positive",
    toneLabel: "Completed",
    workflowPoint: "Access active",
    description:
      "Provisioning completed and the fixed access window is in effect.",
  },
  Rejected: {
    label: "Rejected",
    tone: "critical",
    toneLabel: "Ended",
    workflowPoint: "Workflow ended",
    description:
      "A human reviewer rejected this request. Corrections require a new request.",
  },
};

const workflowStateLabels: Record<WorkflowStepState, string> = {
  complete: "Complete",
  current: "Current",
  upcoming: "Not reached",
};

export interface RequestStatusProps {
  status: RequestStatusValue;
}

export function RequestStatus({ status }: RequestStatusProps) {
  const presentation = statusPresentations[status];
  const workflow = getWorkflow(status);

  return (
    <section
      className={`request-status request-status--${presentation.tone}`}
      aria-labelledby="current-request-status-title"
    >
      <div className="request-status__summary">
        <p className="eyebrow">Current status</p>
        <div className="request-status__heading">
          <h2 id="current-request-status-title">{presentation.label}</h2>
          <span
            className={`status-label status-label--${presentation.tone}`}
          >
            {presentation.toneLabel}
          </span>
        </div>
        <p aria-live="polite">{presentation.description}</p>
        <dl className="request-status__metadata">
          <div>
            <dt>Workflow point</dt>
            <dd>{presentation.workflowPoint}</dd>
          </div>
          {presentation.label !== status && (
            <div>
              <dt>System value</dt>
              <dd className="identifier">{status}</dd>
            </div>
          )}
        </dl>
      </div>

      <div className="request-status__workflow">
        <h3>Workflow progress</h3>
        <ol>
          {workflow.map((step, index) => (
            <li
              className={`workflow-step workflow-step--${step.state}`}
              key={step.label}
              aria-current={step.state === "current" ? "step" : undefined}
            >
              <span className="workflow-step__number" aria-hidden="true">
                {index + 1}
              </span>
              <span className="workflow-step__content">
                <strong>{step.label}</strong>
                <span>{workflowStateLabels[step.state]}</span>
              </span>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}

function getWorkflow(status: RequestStatusValue): WorkflowStep[] {
  if (status === "Rejected") {
    return [
      { label: "Request submitted", state: "complete" },
      { label: "Human review", state: "complete" },
      { label: "Workflow ended", state: "current" },
    ];
  }

  const currentIndex: Record<Exclude<RequestStatusValue, "Rejected">, number> =
    {
      AwaitingBusinessApproval: 1,
      AwaitingDevOpsApproval: 2,
      ProvisioningFailed: 3,
      Active: 4,
    };
  const labels = [
    "Request submitted",
    "Business review",
    "DevOps review",
    status === "ProvisioningFailed"
      ? "Provisioning recovery"
      : "Provisioning",
    "Access outcome",
  ];
  const activeIndex = currentIndex[status];

  return labels.map((label, index) => ({
    label,
    state:
      index < activeIndex
        ? "complete"
        : index === activeIndex
          ? "current"
          : "upcoming",
  }));
}
