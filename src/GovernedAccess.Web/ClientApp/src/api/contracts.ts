export const productionRoles = [
  "ProductionReadOnly",
  "ProductionSupport",
] as const;

export type ProductionRole = (typeof productionRoles)[number];

export interface AccessRequestDraft {
  clientId: string | null;
  environmentId: string | null;
  requestedRole: ProductionRole | null;
  justification: string | null;
  incidentId: string | null;
}

export interface CompleteAccessRequestDraft extends AccessRequestDraft {
  clientId: string;
  environmentId: string;
  requestedRole: ProductionRole;
  justification: string;
}

export type DraftPreparationFailureOutcome =
  | "MalformedModelOutput"
  | "Timeout"
  | "Cancelled"
  | "Unavailable";

export interface PrepareRequestDraftRequest {
  intent: string;
}

export type PrepareRequestDraftResponse =
  | {
      outcome: "Prepared";
      draft: CompleteAccessRequestDraft;
    }
  | {
      outcome: "Incomplete";
      draft: AccessRequestDraft;
    }
  | {
      outcome: DraftPreparationFailureOutcome;
      draft: null;
    };

export interface CreateAccessRequestRequest {
  clientId: string;
  environmentId: string;
  requestedRole: ProductionRole;
  justification: string;
  incidentId?: string | null;
}

export interface CreateAccessRequestResponse {
  requestId: string;
  status: "AwaitingBusinessApproval";
  correlationId: string;
}

export type RequestStatus =
  | "AwaitingBusinessApproval"
  | "AwaitingDevOpsApproval"
  | "Rejected"
  | "ProvisioningFailed"
  | "Active";

export interface RequestListItemResponse {
  requestId: string;
  clientId: string;
  environmentId: string;
  requesterId: string;
  status: RequestStatus;
  lastModifiedAt: string;
  // Presentation hint only; the server authorizes every protected action.
  actionable: boolean;
}

export interface RequestListResponse {
  items: RequestListItemResponse[];
}

export type ApprovalStage = "Business" | "DevOps";
export type ApprovalOutcome = "Approved" | "Rejected";
export type ProvisioningOperationStatus = "Pending" | "Succeeded" | "Failed";
export type AccessGrantOutcome = "Succeeded";
export type AuditEventType =
  | "RequestCreated"
  | "ValidationFailed"
  | "BusinessDecision"
  | "DevOpsDecision"
  | "AuthorizationRejected"
  | "InvalidTransitionRejected"
  | "ProvisioningAttempted"
  | "ProvisioningSucceeded"
  | "ProvisioningFailed"
  | "DuplicateRetryReturned";

export interface RequestValidationResponse {
  isValid: boolean;
  fieldErrors: ApiFieldError[];
}

export interface ApprovalDecisionResponse {
  decisionId: string;
  requestId: string;
  stage: ApprovalStage;
  decision: ApprovalOutcome;
  approverId: string;
  approvedRoleId: ProductionRole | null;
  comment: string | null;
  decidedAt: string;
  correlationId: string;
}

export interface ProvisioningOperationResponse {
  requestId: string;
  environmentId: string;
  roleId: ProductionRole;
  status: ProvisioningOperationStatus;
  attemptCount: number;
  lastOutcomeCode: string | null;
  createdAt: string;
  lastAttemptAt: string;
}

export interface RequestGrantResponse {
  grantId: string;
  requestId: string;
  requesterId: string;
  environmentId: string;
  roleId: ProductionRole;
  activatedAt: string;
  expiresAt: string;
  outcome: AccessGrantOutcome;
  correlationId: string;
  isExpired: boolean;
}

export interface RequestAuditEventResponse {
  eventId: string;
  requestId: string;
  eventType: AuditEventType;
  actorId: string | null;
  occurredAt: string;
  correlationId: string;
  outcomeCode: string;
  details: Record<string, unknown>;
}

export interface RequestDetailResponse {
  requestId: string;
  requesterId: string;
  clientId: string;
  environmentId: string;
  requestedRoleId: ProductionRole;
  justification: string;
  incidentId: string | null;
  status: RequestStatus;
  createdAt: string;
  lastModifiedAt: string;
  // Presentation hints only; the server authorizes every protected action.
  availableActions: string[];
  validation: RequestValidationResponse;
  decisions: ApprovalDecisionResponse[];
  provisioningOperation: ProvisioningOperationResponse | null;
  grant: RequestGrantResponse | null;
  auditEvents: RequestAuditEventResponse[];
}

export interface ProvisioningRetryResponse {
  requestId: string;
  status: "Active";
  grant: RequestGrantResponse;
}

export const demoPrincipalKeys = {
  requester: "requester",
  clientAlphaApprover: "client-alpha-business-approver",
  clientBetaApprover: "client-beta-business-approver",
  devOpsApprover: "devops-approver",
} as const;

export type DemoPrincipalKey =
  (typeof demoPrincipalKeys)[keyof typeof demoPrincipalKeys];

export type PrincipalKind =
  | "Requester"
  | "BusinessApprover"
  | "DevOpsApprover";

export type SessionCapability =
  | "createRequest"
  | "decideBusinessRequests"
  | "decideDevOpsRequests"
  | "retryProvisioning";

export interface DemoSessionRequest {
  principalKey: DemoPrincipalKey;
}

export interface SessionPrincipalView {
  id: string;
  displayName: string;
  kind: PrincipalKind;
  clientId: string | null;
}

export type SessionView =
  | {
      authenticated: false;
      principal: null;
      capabilities: [];
    }
  | {
      authenticated: true;
      principal: SessionPrincipalView;
      // Presentation hints only; the server authorizes every protected action.
      capabilities: SessionCapability[];
    };

export interface ApiFieldError {
  field: string;
  code: string;
  message: string;
}

export interface ApiProblemDetails {
  status: number;
  title: string;
  detail: string;
  code: string;
  correlationId?: string;
  fieldErrors?: ApiFieldError[];
}
