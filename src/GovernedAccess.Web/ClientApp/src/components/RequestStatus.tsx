import type { RequestStatus as RequestStatusValue } from "../api/contracts";

type StatusTone = "pending" | "critical" | "positive";
type WorkflowStepState = "complete" | "current" | "upcoming";

interface StatusPresentation {
  label: string;
  tone: StatusTone;
  toneLabel: string;
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
    description: "Waiting for the assigned business approver.",
  },
  AwaitingDevOpsApproval: {
    label: "Awaiting DevOps approval",
    tone: "pending",
    toneLabel: "Pending",
    description: "Business approved. Waiting for DevOps.",
  },
  ProvisioningFailed: {
    label: "Provisioning failed",
    tone: "critical",
    toneLabel: "Recovery required",
    description: "The existing provisioning operation needs attention.",
  },
  Active: {
    label: "Active",
    tone: "positive",
    toneLabel: "Complete",
    description: "Access is active for the approved eight-hour window.",
  },
  Rejected: {
    label: "Rejected",
    tone: "critical",
    toneLabel: "Closed",
    description: "This request is closed. Changes require a new request.",
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
      </div>

      <div className="request-status__workflow">
        <h3>Workflow</h3>
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
