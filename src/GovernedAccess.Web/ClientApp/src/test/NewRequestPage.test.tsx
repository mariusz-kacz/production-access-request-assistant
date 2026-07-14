import "@testing-library/jest-dom/vitest";

import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { NewRequestPage } from "../pages/NewRequestPage";

const primaryIntent =
  "I need four hours of read-only access to Client Alpha production for " +
  "INC-1042 to investigate the incident.";

const preparedDraft = {
  clientId: "client-alpha",
  environmentId: "PROD-ALPHA-EU",
  requestedRole: "ProductionReadOnly",
  durationMinutes: 240,
  justification: "Investigate the active production incident.",
  incidentId: "INC-1042",
  missingFields: [],
};

const fetchMock = vi.fn<typeof fetch>();

describe("NewRequestPage", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
    document.cookie = "XSRF-TOKEN=test-antiforgery-token; Path=/";
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("prepares a reviewable draft containing authoritative stable identifiers", async () => {
    mockApi({
      preparation: {
        outcome: "Prepared",
        draft: preparedDraft,
        validationMessages: [],
      },
    });
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/request intent/i), primaryIntent);
    await user.click(screen.getByRole("button", { name: /prepare draft/i }));

    expect(await screen.findByLabelText(/^client id$/i)).toHaveValue("client-alpha");
    expect(screen.getByLabelText(/^environment id$/i)).toHaveValue("PROD-ALPHA-EU");
    expect(screen.getByLabelText(/^requested role$/i)).toHaveValue("ProductionReadOnly");
    expect(screen.getByLabelText(/^duration.*minutes/i)).toHaveValue(240);
    expect(screen.getByLabelText(/^justification$/i)).toHaveValue(
      "Investigate the active production incident.",
    );
    expect(screen.getByLabelText(/^incident id$/i)).toHaveValue("INC-1042");

    const preparationCall = findFetchCall("/api/request-drafts/prepare");
    expect(preparationCall).toBeDefined();
    expect(preparationCall?.[1]?.method).toBe("POST");
    expect(JSON.parse(String(preparationCall?.[1]?.body))).toEqual({
      intent: primaryIntent,
    });
  });

  it("shows correction guidance and enables submission after missing fields are supplied", async () => {
    mockApi({
      preparation: {
        outcome: "Incomplete",
        draft: {
          ...preparedDraft,
          durationMinutes: null,
          justification: null,
          missingFields: ["durationMinutes", "justification"],
        },
        validationMessages: [
          "Provide the requested duration in minutes.",
          "Provide a business justification.",
        ],
      },
    });
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/request intent/i), primaryIntent);
    await user.click(screen.getByRole("button", { name: /prepare draft/i }));

    expect(
      await screen.findByText("Provide the requested duration in minutes."),
    ).toBeVisible();
    expect(screen.getByText("Provide a business justification.")).toBeVisible();
    const submit = screen.getByRole("button", { name: /submit request/i });
    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText(/^duration.*minutes/i), "240");
    await user.type(
      screen.getByLabelText(/^justification$/i),
      "Investigate the active production incident.",
    );

    expect(submit).toBeEnabled();
  });

  it.each([
    ["MalformedModelOutput", /could not produce a valid draft/i],
    ["Timeout", /draft preparation timed out/i],
    ["Unavailable", /draft preparation is unavailable/i],
  ])("displays the typed %s preparation failure safely", async (outcome, expectedText) => {
    mockApi({
      preparation: {
        outcome,
        validationMessages: ["Review the request details and try again."],
      },
    });
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/request intent/i), primaryIntent);
    await user.click(screen.getByRole("button", { name: /prepare draft/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(expectedText);
    expect(alert).toHaveTextContent("Review the request details and try again.");
    expect(screen.getByLabelText(/request intent/i)).toHaveValue(primaryIntent);
    expect(screen.getByRole("button", { name: /prepare draft/i })).toBeEnabled();
    expect(screen.queryByRole("button", { name: /submit request/i })).not.toBeInTheDocument();
  });

  it("cancels in-progress draft preparation through AbortSignal", async () => {
    let observedSignal: AbortSignal | null = null;
    fetchMock.mockImplementation(async (input, init) => {
      const path = getPath(input);

      if (path === "/api/security/antiforgery") {
        return emptyResponse();
      }

      if (path === "/api/request-drafts/prepare") {
        observedSignal = getSignal(input, init);
        return await new Promise<Response>((_, reject) => {
          observedSignal?.addEventListener(
            "abort",
            () => reject(new DOMException("The request was cancelled.", "AbortError")),
            { once: true },
          );
        });
      }

      throw new Error(`Unexpected fetch request: ${path}`);
    });
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/request intent/i), primaryIntent);
    await user.click(screen.getByRole("button", { name: /prepare draft/i }));
    await waitFor(() => expect(observedSignal).not.toBeNull());
    await user.click(screen.getByRole("button", { name: /cancel preparation/i }));

    expect(observedSignal?.aborted).toBe(true);
    expect(await screen.findByRole("status")).toHaveTextContent(/preparation cancelled/i);
    expect(screen.queryByRole("button", { name: /submit request/i })).not.toBeInTheDocument();
  });

  it("submits only the corrected draft fields and displays the created request", async () => {
    mockApi({
      preparation: {
        outcome: "Prepared",
        draft: preparedDraft,
        validationMessages: [],
      },
      creation: {
        requestId: "65cabda1-08be-46db-8706-473dec627ba7",
        version: 1,
        status: "AwaitingBusinessApproval",
        correlationId: "correlation-created-request",
      },
    });
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/request intent/i), primaryIntent);
    await user.click(screen.getByRole("button", { name: /prepare draft/i }));
    await screen.findByLabelText(/^client id$/i);
    await user.click(screen.getByRole("button", { name: /submit request/i }));

    const confirmation = await screen.findByRole("status");
    expect(confirmation).toHaveTextContent(/request submitted/i);
    expect(confirmation).toHaveTextContent("65cabda1-08be-46db-8706-473dec627ba7");
    expect(confirmation).toHaveTextContent("AwaitingBusinessApproval");

    const creationCall = findFetchCall("/api/requests");
    expect(creationCall).toBeDefined();
    expect(creationCall?.[1]?.method).toBe("POST");
    const submittedBody = JSON.parse(String(creationCall?.[1]?.body));
    expect(submittedBody).toEqual({
      clientId: "client-alpha",
      environmentId: "PROD-ALPHA-EU",
      requestedRole: "ProductionReadOnly",
      durationMinutes: 240,
      justification: "Investigate the active production incident.",
      incidentId: "INC-1042",
    });
    expect(submittedBody).not.toHaveProperty("requesterId");
    expect(submittedBody).not.toHaveProperty("approverId");
    expect(submittedBody).not.toHaveProperty("roles");
  });
});

interface MockApiOptions {
  preparation: unknown;
  creation?: unknown;
}

function renderPage() {
  return render(
    <MemoryRouter>
      <NewRequestPage />
    </MemoryRouter>,
  );
}

function mockApi(options: MockApiOptions) {
  fetchMock.mockImplementation(async (input) => {
    const path = getPath(input);

    if (path === "/api/security/antiforgery") {
      document.cookie = "XSRF-TOKEN=test-antiforgery-token; Path=/";
      return emptyResponse();
    }

    if (path === "/api/request-drafts/prepare") {
      return jsonResponse(options.preparation);
    }

    if (path === "/api/requests" && options.creation !== undefined) {
      return jsonResponse(options.creation, 201);
    }

    throw new Error(`Unexpected fetch request: ${path}`);
  });
}

function findFetchCall(path: string) {
  return fetchMock.mock.calls.find(([input]) => getPath(input) === path);
}

function getPath(input: RequestInfo | URL) {
  const value =
    typeof input === "string"
      ? input
      : input instanceof URL
        ? input.toString()
        : input.url;
  return new URL(value, "https://localhost").pathname;
}

function getSignal(input: RequestInfo | URL, init?: RequestInit) {
  if (init?.signal !== undefined && init.signal !== null) {
    return init.signal;
  }

  return input instanceof Request ? input.signal : null;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function emptyResponse() {
  return new Response(null, { status: 204 });
}
