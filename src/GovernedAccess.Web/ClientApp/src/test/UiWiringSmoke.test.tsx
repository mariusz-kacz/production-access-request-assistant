import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { apiRequest } from "../api/client";
import type { RequestDetailResponse } from "../api/contracts";
import { BUSINESS_DECISION_ACTION } from "../components/BusinessDecisionPanel";
import { DEVOPS_DECISION_ACTION } from "../components/DevOpsDecisionPanel";
import { NewRequestPage } from "../pages/NewRequestPage";
import { RequestDetailPage } from "../pages/RequestDetailPage";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();

  return {
    ...actual,
    apiRequest: vi.fn(),
  };
});

const mockedApiRequest = vi.mocked(apiRequest);
const requestId = "3f7ff05a-7e2e-496d-bf85-c9ae1c210aa3";
const requestDetail: RequestDetailResponse = {
  requestId,
  requesterId: "requester",
  clientId: "client-alpha",
  environmentId: "client-alpha-production",
  requestedRoleId: "ProductionSupport",
  justification: "Investigate the active production incident.",
  incidentId: "INC-1042",
  status: "AwaitingBusinessApproval",
  createdAt: "2026-07-20T08:00:00Z",
  lastModifiedAt: "2026-07-20T08:00:00Z",
  availableActions: [BUSINESS_DECISION_ACTION],
  validation: { isValid: true, fieldErrors: [] },
  decisions: [],
  provisioningOperation: null,
  grant: null,
  auditEvents: [],
};

describe("thin UI wiring", () => {
  beforeEach(() => {
    mockedApiRequest.mockReset();
  });

  it("loads the route and sends only decision input for a server-computed action", async () => {
    mockedApiRequest.mockImplementation(async (path) => {
      if (path === `/api/requests/${requestId}`) {
        return requestDetail as never;
      }

      if (path === `/api/requests/${requestId}/business-decisions`) {
        return {
          requestId,
          status: "AwaitingDevOpsApproval",
          correlationId: "correlation-ui-smoke",
        } as never;
      }

      throw new Error(`Unexpected API request: ${path}`);
    });
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={[`/requests/${requestId}`]}>
        <Routes>
          <Route path="/requests/:requestId" element={<RequestDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    const statusRegion = await screen.findByRole("region", {
      name: "Awaiting business approval",
    });
    expect(within(statusRegion).getByText("Pending")).toBeTruthy();
    expect(
      within(statusRegion).getByRole("heading", {
        name: "Workflow progress",
      }),
    ).toBeTruthy();
    const workflow = within(statusRegion).getByRole("list");
    const currentStep = within(workflow)
      .getByText("Business review")
      .closest("li");
    expect(currentStep?.getAttribute("aria-current")).toBe("step");
    expect(within(currentStep as HTMLElement).getByText("Current")).toBeTruthy();

    const approveButton = screen.getByRole("button", {
      name: "Approve request",
    });
    expect(
      screen.getByRole("button", { name: "Reject request" }),
    ).toBeTruthy();
    await user.click(approveButton);

    expect(mockedApiRequest).toHaveBeenCalledWith(
      `/api/requests/${requestId}/business-decisions`,
      expect.objectContaining({
        method: "POST",
        body: { decision: "Approve" },
        signal: expect.any(AbortSignal),
      }),
    );

    const nextStatusRegion = await screen.findByRole("region", {
      name: "Awaiting DevOps approval",
    });
    expect(
      within(nextStatusRegion).getByText("AwaitingDevOpsApproval"),
    ).toBeTruthy();
  });

  it("wires the DevOps action to a safe active-grant summary", async () => {
    const activatedAt = "2026-07-20T09:00:00Z";
    const expiresAt = "2026-07-20T17:00:00Z";
    mockedApiRequest.mockImplementation(async (path) => {
      if (path === `/api/requests/${requestId}`) {
        return {
          ...requestDetail,
          status: "AwaitingDevOpsApproval",
          availableActions: [DEVOPS_DECISION_ACTION],
        } as never;
      }

      if (path === `/api/requests/${requestId}/devops-decisions`) {
        return {
          requestId,
          status: "Active",
          correlationId: "correlation-devops-ui-smoke",
          grant: {
            grantId: "5b70a7a0-31f9-4f46-bab1-7f0bd8bc40ed",
            environmentId: requestDetail.environmentId,
            roleId: requestDetail.requestedRoleId,
            activatedAt,
            expiresAt,
          },
        } as never;
      }

      throw new Error(`Unexpected API request: ${path}`);
    });
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={[`/requests/${requestId}`]}>
        <Routes>
          <Route path="/requests/:requestId" element={<RequestDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    const approveAndProvisionButton = await screen.findByRole("button", {
      name: "Approve and provision",
    });
    expect(
      screen.getByRole("button", { name: "Reject request" }),
    ).toBeTruthy();
    await user.click(approveAndProvisionButton);

    expect(mockedApiRequest).toHaveBeenCalledWith(
      `/api/requests/${requestId}/devops-decisions`,
      expect.objectContaining({
        method: "POST",
        body: { decision: "Approve" },
        signal: expect.any(AbortSignal),
      }),
    );

    expect(
      await screen.findByRole("region", { name: "Active" }),
    ).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Active grant" })).toBeTruthy();
    const grantSummary = screen.getByLabelText("Active access grant");
    expect(
      within(grantSummary).getByText(
        "5b70a7a0-31f9-4f46-bab1-7f0bd8bc40ed",
      ),
    ).toBeTruthy();
    expect(
      within(grantSummary).getByText(requestDetail.environmentId),
    ).toBeTruthy();
    expect(
      within(grantSummary).getByText(requestDetail.requestedRoleId),
    ).toBeTruthy();
    expect(
      grantSummary.querySelector(`time[datetime="${activatedAt}"]`),
    ).toBeTruthy();
    expect(
      grantSummary.querySelector(`time[datetime="${expiresAt}"]`),
    ).toBeTruthy();
    expect(screen.getByText("correlation-devops-ui-smoke")).toBeTruthy();
  });

  it("replaces drafting controls after an immutable request is submitted", async () => {
    mockedApiRequest.mockImplementation(async (path) => {
      if (path === "/api/request-drafts/prepare") {
        return {
          outcome: "Prepared",
          draft: {
            clientId: requestDetail.clientId,
            environmentId: requestDetail.environmentId,
            requestedRole: requestDetail.requestedRoleId,
            justification: requestDetail.justification,
            incidentId: requestDetail.incidentId,
          },
        } as never;
      }

      if (path === "/api/requests") {
        return {
          requestId,
          status: "AwaitingBusinessApproval",
          correlationId: "correlation-request-created",
        } as never;
      }

      throw new Error(`Unexpected API request: ${path}`);
    });
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <NewRequestPage />
      </MemoryRouter>,
    );

    await user.type(
      screen.getByRole("textbox", { name: "Access request description" }),
      "Investigate the active Client Alpha production incident.",
    );
    await user.click(screen.getByRole("button", { name: "Prepare draft" }));
    const submitButton = await screen.findByRole("button", {
      name: "Validate and submit request",
    });
    expect(
      screen.getByRole("heading", { name: "Description captured" }),
    ).toBeTruthy();
    expect(
      screen.queryByRole("textbox", { name: "Access request description" }),
    ).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Prepare draft" }),
    ).toBeNull();
    expect(
      mockedApiRequest.mock.calls.filter(
        ([path]) => path === "/api/request-drafts/prepare",
      ),
    ).toHaveLength(1);

    await user.click(submitButton);

    expect(
      await screen.findByRole("heading", { name: "Request submitted" }),
    ).toBeTruthy();
    expect(
      screen.queryByRole("textbox", { name: "Access request description" }),
    ).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Prepare draft" }),
    ).toBeNull();
    expect(
      screen.queryByRole("button", {
        name: "Validate and submit request",
      }),
    ).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Prepare another request" }),
    ).toBeNull();
    expect(
      screen.getByRole("link", { name: "View request details" }),
    ).toBeTruthy();
  });
});
