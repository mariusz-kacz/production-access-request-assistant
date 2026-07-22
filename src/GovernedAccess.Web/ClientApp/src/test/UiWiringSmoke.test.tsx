import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { apiRequest } from "../api/client";
import type { RequestDetailResponse } from "../api/contracts";
import { BUSINESS_DECISION_ACTION } from "../components/BusinessDecisionPanel";
import { DEVOPS_DECISION_ACTION } from "../components/DevOpsDecisionPanel";
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

    await user.click(
      await screen.findByRole("button", { name: "Approve request" }),
    );

    expect(mockedApiRequest).toHaveBeenCalledWith(
      `/api/requests/${requestId}/business-decisions`,
      expect.objectContaining({
        method: "POST",
        body: { decision: "Approve" },
        signal: expect.any(AbortSignal),
      }),
    );
    expect(await screen.findByText("AwaitingDevOpsApproval")).toBeTruthy();
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
    const { container } = render(
      <MemoryRouter initialEntries={[`/requests/${requestId}`]}>
        <Routes>
          <Route path="/requests/:requestId" element={<RequestDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(
      await screen.findByRole("button", { name: "Approve and provision" }),
    );

    expect(mockedApiRequest).toHaveBeenCalledWith(
      `/api/requests/${requestId}/devops-decisions`,
      expect.objectContaining({
        method: "POST",
        body: { decision: "Approve" },
        signal: expect.any(AbortSignal),
      }),
    );
    expect(await screen.findByText("Active")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Active grant" })).toBeTruthy();
    expect(
      container.querySelector(`time[datetime="${activatedAt}"]`),
    ).toBeTruthy();
    expect(
      container.querySelector(`time[datetime="${expiresAt}"]`),
    ).toBeTruthy();
  });
});
