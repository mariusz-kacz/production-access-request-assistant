export const productionRoles = [
  "ProductionReadOnly",
  "ProductionSupport",
] as const;

export type ProductionRole = (typeof productionRoles)[number];

export interface AccessRequestDraft {
  clientId: string | null;
  environmentId: string | null;
  requestedRole: ProductionRole | null;
  durationMinutes: number | null;
  justification: string | null;
  incidentId: string | null;
}

export interface CompleteAccessRequestDraft extends AccessRequestDraft {
  clientId: string;
  environmentId: string;
  requestedRole: ProductionRole;
  durationMinutes: number;
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
  durationMinutes: number;
  justification: string;
  incidentId?: string | null;
}

export interface CreateAccessRequestResponse {
  requestId: string;
  version: 1;
  status: "AwaitingBusinessApproval";
  correlationId: string;
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
  | "editOwnRequests"
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
  currentVersion?: number;
}
