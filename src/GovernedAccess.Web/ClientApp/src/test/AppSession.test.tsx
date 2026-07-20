import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { App } from "../App";
import { apiRequest } from "../api/client";
import {
  demoPrincipalKeys,
  type DemoPrincipalKey,
  type DemoSessionRequest,
  type RequestDetailResponse,
  type SessionView,
} from "../api/contracts";
import { BUSINESS_DECISION_ACTION } from "../components/BusinessDecisionPanel";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();

  return {
    ...actual,
    apiRequest: vi.fn(),
  };
});

const mockedApiRequest = vi.mocked(apiRequest);

const anonymousSession: SessionView = {
  authenticated: false,
  principal: null,
  capabilities: [],
};

const requesterSession: SessionView = {
  authenticated: true,
  principal: {
    id: demoPrincipalKeys.requester,
    displayName: "Demo Requester",
    kind: "Requester",
    clientId: null,
  },
  capabilities: ["createRequest"],
};

const alphaApproverSession: SessionView = {
  authenticated: true,
  principal: {
    id: demoPrincipalKeys.clientAlphaApprover,
    displayName: "Client Alpha Business Approver",
    kind: "BusinessApprover",
    clientId: "client-alpha",
  },
  capabilities: ["decideBusinessRequests"],
};

const requestId = "3f7ff05a-7e2e-496d-bf85-c9ae1c210aa3";
const requestDetail: RequestDetailResponse = {
  requestId,
  requesterId: demoPrincipalKeys.requester,
  clientId: "client-alpha",
  environmentId: "client-alpha-production",
  requestedRoleId: "ProductionSupport",
  justification: "Investigate the active production incident.",
  incidentId: "INC-1042",
  status: "AwaitingBusinessApproval",
  createdAt: "2026-07-20T08:00:00Z",
  lastModifiedAt: "2026-07-20T08:00:00Z",
  availableActions: [],
};

let activePrincipalKey: DemoPrincipalKey | undefined;

describe("application session wiring", () => {
  beforeEach(() => {
    mockedApiRequest.mockReset();
    activePrincipalKey = undefined;
    window.history.pushState({}, "", "/requests/new");

    mockedApiRequest.mockImplementation(async (path, options = {}) => {
      if (path === "/api/session" && (options.method ?? "GET") === "GET") {
        return anonymousSession as never;
      }

      if (path === "/api/demo/session" && options.method === "POST") {
        const request = options.body as DemoSessionRequest;
        activePrincipalKey = request.principalKey;
        if (request.principalKey === demoPrincipalKeys.requester) {
          return requesterSession as never;
        }

        if (request.principalKey === demoPrincipalKeys.clientAlphaApprover) {
          return alphaApproverSession as never;
        }
      }

      if (path === "/api/demo/session" && options.method === "DELETE") {
        activePrincipalKey = undefined;
        return undefined as never;
      }

      if (path === `/api/requests/${requestId}`) {
        if (activePrincipalKey === demoPrincipalKeys.requester) {
          return requestDetail as never;
        }

        if (activePrincipalKey === demoPrincipalKeys.clientAlphaApprover) {
          return {
            ...requestDetail,
            availableActions: [BUSINESS_DECISION_ACTION],
          } as never;
        }
      }

      throw new Error(`Unexpected API request: ${options.method ?? "GET"} ${path}`);
    });
  });

  it("gates anonymous content and refreshes it across sign-in, switching, and sign-out", async () => {
    const user = userEvent.setup();
    render(<App />);

    const identitySelector = await screen.findByRole("combobox", {
      name: "Demo identity",
    });
    const identityValues = screen
      .getAllByRole("option")
      .map((option) => (option as HTMLOptionElement).value);
    expect(identityValues).toEqual(
      expect.arrayContaining(Object.values(demoPrincipalKeys)),
    );
    expect(
      screen.queryByRole("heading", {
        name: "Prepare a governed access request",
      }),
    ).toBeNull();

    await user.selectOptions(identitySelector, demoPrincipalKeys.requester);
    await user.click(
      screen.getByRole("button", { name: /sign in|switch identity/i }),
    );

    expect(
      await screen.findByRole("heading", {
        name: "Prepare a governed access request",
      }),
    ).toBeTruthy();
    expect(mockedApiRequest).toHaveBeenCalledWith(
      "/api/demo/session",
      expect.objectContaining({
        method: "POST",
        body: { principalKey: demoPrincipalKeys.requester },
      }),
    );

    await user.selectOptions(
      screen.getByRole("combobox", { name: "Demo identity" }),
      demoPrincipalKeys.clientAlphaApprover,
    );
    await user.click(
      screen.getByRole("button", { name: /sign in|switch identity/i }),
    );

    expect(
      await screen.findByText("Signed in as Client Alpha Business Approver"),
    ).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() => {
      expect(
        screen.queryByRole("heading", {
          name: "Prepare a governed access request",
        }),
      ).toBeNull();
    });
    expect(mockedApiRequest).toHaveBeenCalledWith(
      "/api/demo/session",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  it("opens a request URL and refreshes server-computed actions after identity switching", async () => {
    window.history.pushState({}, "", `/requests/${requestId}`);
    const user = userEvent.setup();
    render(<App />);

    expect(
      await screen.findByRole("heading", { name: "Sign in to continue" }),
    ).toBeTruthy();

    await user.selectOptions(
      screen.getByRole("combobox", { name: "Demo identity" }),
      demoPrincipalKeys.requester,
    );
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(
      await screen.findByRole("heading", { name: "Request details" }),
    ).toBeTruthy();
    expect(screen.getByText("client-alpha-production")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Approve request" })).toBeNull();

    await user.selectOptions(
      screen.getByRole("combobox", { name: "Demo identity" }),
      demoPrincipalKeys.clientAlphaApprover,
    );
    await user.click(
      screen.getByRole("button", { name: "Switch identity" }),
    );

    expect(
      await screen.findByRole("button", { name: "Approve request" }),
    ).toBeTruthy();
    const detailCalls = mockedApiRequest.mock.calls.filter(
      ([path]) => path === `/api/requests/${requestId}`,
    );
    expect(detailCalls).toHaveLength(2);
    expect(detailCalls[0]?.[1]).toEqual(
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });
});
