import type { ApiFieldError, ApiProblemDetails } from "./contracts";

export type { ApiFieldError, ApiProblemDetails } from "./contracts";

const antiforgeryEndpoint = "/api/security/antiforgery";
const antiforgeryCookieName = "XSRF-TOKEN";
const antiforgeryHeaderName = "X-XSRF-TOKEN";

const safeMethods = new Set(["GET", "HEAD", "OPTIONS", "TRACE"]);

export class ApiError extends Error {
  readonly problem: ApiProblemDetails;

  constructor(problem: ApiProblemDetails) {
    super(problem.detail || problem.title);
    this.name = "ApiError";
    this.problem = problem;
  }

  get status(): number {
    return this.problem.status;
  }

  get title(): string {
    return this.problem.title;
  }

  get detail(): string {
    return this.problem.detail;
  }

  get code(): string {
    return this.problem.code;
  }

  get correlationId(): string | undefined {
    return this.problem.correlationId;
  }

  get fieldErrors(): ApiFieldError[] | undefined {
    return this.problem.fieldErrors;
  }

}

export type ApiRequestOptions<TBody = unknown> = Omit<
  RequestInit,
  "body" | "credentials" | "signal"
> & {
  body?: TBody;
  signal?: AbortSignal;
};

export async function apiRequest<TResponse, TBody = unknown>(
  path: string,
  options: ApiRequestOptions<TBody> = {},
): Promise<TResponse> {
  if (!path.startsWith("/api/")) {
    throw new TypeError("API requests must use a same-origin /api/ path.");
  }

  const method = (options.method ?? "GET").toUpperCase();
  const headers = new Headers(options.headers);
  headers.set("Accept", "application/json, application/problem+json");

  if (!safeMethods.has(method)) {
    const token = await getAntiforgeryToken(options.signal);
    headers.set(antiforgeryHeaderName, token);
  }

  let body: BodyInit | undefined;
  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(options.body);
  }

  const response = await fetch(path, {
    ...options,
    method,
    headers,
    body,
    credentials: "include",
    signal: options.signal,
  });

  if (!response.ok) {
    throw new ApiError(await mapProblemDetails(response));
  }

  if (response.status === 204 || response.status === 205) {
    return undefined as TResponse;
  }

  return (await response.json()) as TResponse;
}

async function getAntiforgeryToken(signal?: AbortSignal): Promise<string> {
  let token = readCookie(antiforgeryCookieName);
  if (token !== undefined) {
    return token;
  }

  const response = await fetch(antiforgeryEndpoint, {
    method: "GET",
    credentials: "include",
    headers: {
      Accept: "application/json, application/problem+json",
    },
    signal,
  });

  if (!response.ok) {
    throw new ApiError(await mapProblemDetails(response));
  }

  token = readCookie(antiforgeryCookieName);
  if (token === undefined) {
    throw new ApiError({
      status: 0,
      title: "Request security token unavailable.",
      detail: "The request security token could not be obtained.",
      code: "antiforgery_token_unavailable",
    });
  }

  return token;
}

function readCookie(name: string): string | undefined {
  if (typeof document === "undefined") {
    return undefined;
  }

  const prefix = `${encodeURIComponent(name)}=`;
  const cookie = document.cookie
    .split(";")
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix));

  if (cookie === undefined) {
    return undefined;
  }

  const value = cookie.slice(prefix.length);
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

async function mapProblemDetails(response: Response): Promise<ApiProblemDetails> {
  let value: unknown;

  try {
    value = await response.json();
  } catch {
    value = undefined;
  }

  const problem = isRecord(value) ? value : {};
  const status = readNumber(problem.status) ?? response.status;
  const title =
    readString(problem.title) ?? (response.statusText || "Request failed.");
  const detail = readString(problem.detail) ?? title;
  const code = readString(problem.code) ?? `http_${status}`;
  const correlationId =
    readString(problem.correlationId) ??
    response.headers.get("X-Correlation-ID") ??
    undefined;
  const fieldErrors = readFieldErrors(problem.fieldErrors);

  return {
    status,
    title,
    detail,
    code,
    ...(correlationId === undefined ? {} : { correlationId }),
    ...(fieldErrors === undefined ? {} : { fieldErrors }),
  };
}

function readFieldErrors(value: unknown): ApiFieldError[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  const errors = value.flatMap((item): ApiFieldError[] => {
    if (!isRecord(item)) {
      return [];
    }

    const field = readString(item.field);
    const code = readString(item.code);
    const message = readString(item.message);
    return field === undefined || code === undefined || message === undefined
      ? []
      : [{ field, code, message }];
  });

  return errors.length === 0 ? undefined : errors;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readString(value: unknown): string | undefined {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

function readNumber(value: unknown): number | undefined {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}
