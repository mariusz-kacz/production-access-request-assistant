# Research: Bounded Live-Model Outcome Evaluation

## R1. Black-box evaluation boundary

**Decision**: Execute the real `RequestIntakeService.PrepareAsync` path but grade only
its final application-owned result and selected final facts.

**Rationale**: This measures user-visible end-to-end behavior while preserving model
schema validation and authoritative application validation. It avoids coupling the
evaluator to MAF iterations, MCP call representation, or provider payloads.

**Alternatives considered**:

- Observe MCP and model execution: deferred because it adds substantial code and is
  primarily diagnostic rather than necessary for first-iteration correctness.
- Call the model directly: rejected because it bypasses application validation.
- Use an LLM judge: rejected because it adds another stochastic dependency.

## R2. Scenario correctness

**Decision**: Compare normalized final result kind plus only scenario-declared final
facts: canonical candidate identifiers, clarification target/options when relevant,
validation codes, and fields that must be preserved or cleared.

**Rationale**: Checking only `Ready` would allow a wrong environment or role to pass.
The selected final facts keep grading meaningful without inspecting internal calls or
exact response text.

**Alternatives considered**:

- Grade result kind only: rejected because incorrect scope could pass.
- Compare complete assistant text: rejected as brittle and not application-owned.
- Compare internal proposals: deferred with model observation.

## R3. Latency measurement

**Decision**: Measure wall-clock elapsed time once per scenario, from its first turn
until its final result or typed failure. Record milliseconds but do not gate v1 pass
status on a performance threshold.

**Rationale**: Scenario latency is simple and useful, while deployment and network
variation make an initial universal threshold misleading. Existing per-turn timeouts
remain the hard bound.

**Alternatives considered**:

- Provider-reported latency or token usage: rejected because it requires a decorator
  and is not consistently available.
- Per-tool and per-model-call timing: deferred with internal observation.

## R4. Execution and isolation

**Decision**: Add one explicit mode to the existing Web executable. It starts a
loopback-only host with the real MCP endpoint, uses a uniquely named disposable SQLite
database, runs scenarios sequentially, and never invokes confirmation.

**Rationale**: This preserves production-shaped intake behavior, process-local history,
and the single-executable constraint without exposing normal Teams/browser or workflow
surfaces.

**Alternatives considered**:

- New console/test project: rejected as another executable and unnecessary structure.
- Credentialed xUnit category: rejected because automated tests must remain live-free.
- Normal application database: rejected because evaluation state must be isolated.

## R5. Pass policy and safety check

**Decision**: Pass a full run only when all 20 scenarios are semantically correct and
requests, approval decisions, provisioning operations, and grants all remain zero.
For focused diagnosis, permit one exact case-sensitive scenario selection and require
that scenario to pass with the same zero-side-effect rule.

**Rationale**: Every checked-in scenario represents required behavior, so any semantic
failure must fail the run. The side-effect check protects the pre-confirmation boundary
without inspecting internal model or MCP behavior. A focused run likewise requires its
selected scenario to pass.

## R6. Result artifacts

**Decision**: Create one immutable run result, serialize it to JSON, and render a
concise Markdown summary from the same object. Include scenario latency and
failure-only expected-versus-observed final facts.

**Rationale**: A single source prevents disagreement while keeping the human report
small. Raw prompts, prose, transcripts, endpoints, provider/MCP data, and usage are
unnecessary.

## R7. Credential-free verification

**Decision**: Use two evaluation fixtures in the existing integration project. One
owns dataset, grading, and artifact behavior; one owns command composition, fake-model
execution, isolation, timing, cancellation, cleanup, and side effects.

**Rationale**: This covers the feature without a new test project or a large fixture
matrix. Existing MCP and interpreter suites remain authoritative for internal tool and
provider behavior.
