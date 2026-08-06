# Research: Bounded Live-Model Evaluation

## R1. Execution shape

**Decision**: Add an explicit `evaluate-live-model` command mode to the existing
`GovernedAccess.Web` executable. In that mode the process starts a loopback-only Web
host exposing the real `/mcp` endpoint, runs the evaluator, writes artifacts, stops,
and returns a documented exit code. It does not map Teams, browser, confirmation,
approval, provisioning, or fallback endpoints.

**Rationale**: This preserves the one-executable modular-host constraint while
removing unrelated Teams credentials and every state-changing HTTP surface from the
evaluation process. Starting the real MCP endpoint retains catalog discovery,
Streamable HTTP serialization, tool annotations, and typed result behavior.

**Alternatives considered**:

- Add a console or test project: rejected because it creates another executable and
  duplicates host composition.
- Run a credentialed xUnit category: rejected because routine tests must remain
  credential-free and must never incur provider cost.
- Drive the Teams endpoint: rejected because it requires bot authentication and
  exposes confirmation behavior that the evaluation must not use.

## R2. Production-shaped intake boundary and isolated state

**Decision**: Run every requester turn through
`RequestIntakeService.PrepareAsync`, backed by the real MAF interpreter, validator,
EF intake store, authoritative context reader, and fixed synthetic data. Evaluation
mode uses one uniquely named temporary SQLite database, deletes it after all scopes
and connections are disposed, and never invokes `ConfirmAsync`.

**Rationale**: `PrepareAsync` is the existing application-owned pre-confirmation
boundary. It preserves candidate sanitization, focused clarification, readiness,
process-local history, and authoritative validation without duplicating intake logic.
The disposable database faithfully supports multi-turn intake while keeping all
temporary candidate records separate from the developer's application database.

**Alternatives considered**:

- Call the provider or `IChatClient` directly: rejected because it bypasses MAF,
  MCP, proposal parsing, and deterministic application validation.
- Call the interpreter and validator separately from an evaluation orchestrator:
  rejected because it would duplicate `RequestIntakeService` behavior and option
  validation.
- Add an evaluation-specific in-memory intake-store implementation: viable, but
  rejected because correctly reproducing active-intake and save behavior is more code
  than reusing the existing EF store against a disposable database.

## R3. Shared request-preparation composition

**Decision**: Extract the MAF session, coordinator, interpreter, intake store, and
`RequestIntakeService` registrations currently nested in Teams composition into one
focused request-preparation registration used by both normal and evaluation modes.
Replace the interpreter's dependency on Teams options with a focused lazy MCP-endpoint
provider. Normal mode resolves the configured trusted Web URI; evaluation mode reads
the loopback address selected by Kestrel after startup.

**Rationale**: Checked-in Teams settings are intentionally insufficient for a live bot
host, so evaluation must not trigger Teams `ValidateOnStart`. A focused endpoint
dependency removes accidental coupling without introducing a provider or domain
abstraction. Lazy resolution allows a collision-free loopback port selected by the
host.

**Alternatives considered**:

- Supply fake Teams configuration: rejected because it hides the coupling and may
  make invalid authentication settings appear acceptable.
- Reserve a fixed loopback port: rejected because local port collisions make the
  one-command workflow unreliable.
- Copy the MAF registrations into evaluation mode: rejected because the two paths
  would drift.

## R4. Scenario dataset contract

**Decision**: Check in one strict JSON dataset under
`src/GovernedAccess.Web/Evaluation/Datasets/`. Version 1 contains exactly the 18
clarified scenarios and the fixed 5/4/3/3/2/1 category distribution. The loader uses
closed, case-sensitive `System.Text.Json` contracts and validates the version,
complete scenario ID set, category-prefix agreement, unique IDs, ordered non-empty
turns, supported tools and roles, bounded clarification options, and evaluable
expectations before the first model call.

**Rationale**: A visible versioned dataset makes evaluation coverage reviewable and
repeatable without adding a database or general evaluation platform. Strict startup
validation prevents a malformed or incomplete matrix from producing misleading
quality evidence.

**Alternatives considered**:

- Encode cases in C#: rejected because review and version comparison become harder.
- Accept arbitrary user datasets in the baseline command: rejected because the
  feature has one deliberately fixed 18-case baseline.
- Add YAML or another parser dependency: rejected because JSON support already exists
  and the artifacts are JSON.

## R5. Safe model and tool observation

**Decision**: Add an internal evaluation observation scope at the Web AI boundary.
The MAF interpreter records only typed proposal facts and safe failure codes. Its MCP
tool wrappers record attempted sequence, tool name, allowlisted identifier argument,
invoked-or-blocked disposition, typed outcome, and duration. A narrow chat-client
decorator records only latency and provider-reported usage counts. The scope is keyed
by scenario, turn, intake, and correlation IDs and is inactive during normal requests.

**Rationale**: Existing logs omit arguments and sequence, while raw provider or MCP
capture would violate the security model. Instrumenting the actual function wrappers
also distinguishes an attempted discovery blocked by the exact-lookup fallback gate
from a tool call that reached MCP.

**Alternatives considered**:

- Parse logs: rejected because logs are intentionally incomplete and their wording is
  not a contract.
- Capture provider requests, responses, or complete MCP payloads and redact later:
  rejected because collection itself is unnecessary and increases disclosure risk.
- Observe only the MCP server: rejected because a client-side fallback gate can block
  a model-attempted call before the server sees it.

## R6. Deterministic grading

**Decision**: Grade each turn and scenario with application-owned facts only:
proposal kind and identifiers, normalized application outcome, sanitized candidate,
validation codes, authoritative clarification choices, significant tool-call
requirements, preserved or cleared fields, and prohibited side effects. Do not grade
exact prose or use an LLM judge. A run passes at 16 of 18 scenario passes only when
all safety invariants also pass.

**Rationale**: These facts are stable enough for repeatable comparison while allowing
normal wording variation. The accepted threshold tolerates limited model variance,
and zero-tolerance safety checks preserve the governed boundary.

**Alternatives considered**:

- Exact assistant text: rejected as brittle and unrelated to authorization safety.
- LLM-as-judge: rejected as another stochastic dependency with no authority.
- Require 18 of 18: rejected by the clarified two-tier pass policy.

## R7. Run artifacts and output format

**Decision**: Build one immutable normalized run result, serialize it as indented
camel-case JSON, and render a concise Markdown summary from the same object. Write
both to a run-specific directory under `artifacts/live-model-evaluation/`. The
Markdown contains summaries plus failure details rather than duplicating the full
JSON result.

**Rationale**: One source object keeps human- and machine-readable evidence aligned,
while a small report is easier to inspect. The ignored directory prevents accidental
source control of generated evidence.

**Alternatives considered**:

- Generate Markdown independently: rejected because totals can diverge.
- Persist evaluation results in SQLite: rejected because the feature requires files
  to be the only durable evidence.
- Add a dashboard or centralized store: rejected as disproportionate and explicitly
  out of scope.

## R8. Configuration, timeout, cancellation, and exit status

**Decision**: Require the existing validated `FoundryResponses` profile before
starting a run. Reject `Deterministic`, missing, or invalid configuration without
fallback. Execute scenarios sequentially and link Ctrl+C/host stopping with a
100-second per-turn deadline. Root cancellation marks the active case cancelled and
remaining cases not run; a per-turn deadline is a typed provider timeout. Use exit
codes `0` for pass, `1` for a completed failed evaluation, `2` for prerequisite or
startup failure, and `130` for cancellation.

**Rationale**: Sequential work avoids cost bursts and history cross-talk. Explicit
deadline and cancellation semantics preserve the existing bounded provider behavior
and make automation-friendly command outcomes unambiguous.

**Alternatives considered**:

- Parallel scenarios: rejected because it increases cost spikes and complicates
  correlation and process-local history.
- Fall back to the deterministic client: rejected because it would falsely report a
  live-model evaluation.
- Treat cancellation as a failed semantic case: rejected because unevaluated cases
  must remain distinguishable from model-quality failures.

## R9. Credential-free verification

**Decision**: Keep all automated coverage credential-free under the existing test
projects. Evaluation-focused component tests use scripted and blocking chat clients,
the real MAF interpreter, loopback MCP, validator, disposable SQLite, and artifact
writer. They cover dataset validation, assertion and aggregation policy, observation,
one synthetic report-agreement and sanitization example, configuration failure,
cancellation, and zero workflow side effects. Existing MCP and provider suites retain the
exhaustive failure matrix.

**Rationale**: This verifies the harness deterministically without duplicating mature
negative-path tests or allowing `dotnet test` to access a live deployment.

**Alternatives considered**:

- Duplicate every MCP timeout and malformed-result case in the evaluator: rejected
  because those mechanics are already owned by narrower deterministic suites.
- Add a new test project: rejected because the current unit and integration projects
  already provide the necessary boundaries.

## Sources

- `src/GovernedAccess.Web/Program.cs`
- `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`
- `src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs`
- `src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs`
- `src/GovernedAccess.Core/Application/RequestIntakeService.cs`
- `src/GovernedAccess.Core/Application/RequestValidator.cs`
- `src/GovernedAccess.Mcp/McpRegistration.cs`
- `src/GovernedAccess.Mcp/RequestContextTools.cs`
- `docs/request-intake-orchestration.md`
- `docs/testing-strategy.md`
