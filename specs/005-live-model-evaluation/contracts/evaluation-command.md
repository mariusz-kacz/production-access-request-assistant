# Live-Model Evaluation Command Contract

## Command

From the repository root:

```powershell
dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --output artifacts/live-model-evaluation
```

`evaluate-live-model` is an explicit mode of the existing Web executable. It does not
start the normal Teams/browser application surface.

## Options

| Option | Required | Contract |
|--------|----------|----------|
| `--output <directory>` | No | Parent directory for a new run-specific child directory; defaults to `artifacts/live-model-evaluation` |

No option may supply an actor, approver, duration, request scope, model endpoint,
deployment, credential, prompt, confirmation flag, or workflow action. Model settings
come only from trusted host configuration.

Unknown arguments, duplicate options, a missing option value, or an output target that
cannot safely own a new child directory fail before any model call.

## Required Configuration

The existing configuration keys must resolve to a valid live profile:

```text
RequestPreparationModel:ExecutionProfile = FoundryResponses
RequestPreparationModel:FoundryResponses:Endpoint
RequestPreparationModel:FoundryResponses:DeploymentName
```

The command rejects the `Deterministic` profile and invalid or unavailable live
configuration. It never falls back to another model or the deterministic client.

## Runtime Behavior

1. Validate command options, live-model configuration, the embedded dataset, and
   output path.
2. Create a uniquely named temporary SQLite database and seed the fixed synthetic
   authoritative data.
3. Start a loopback-only host exposing the real `/mcp` endpoint and no Teams,
   confirmation, workflow, provisioning, controller, or SPA endpoints.
4. Execute all 18 scenarios sequentially through the real pre-confirmation intake
   path with a 100-second deadline for each requester turn.
5. Verify zero requests, approval decisions, provisioning operations, and grants
   after every scenario.
6. Publish `result.json` and `report.md` into a new run directory.
7. Stop and dispose the host, then delete the exact temporary database and its SQLite
   sidecar files.

## Console Output

Console output is concise and safe:

- dataset version;
- live profile and deployment name, but not endpoint or credentials;
- progress as `<current>/18 <scenario-id>`;
- final passed count and safety status;
- final run status; and
- paths to `result.json` and `report.md`.

It excludes requester messages, assistant text, prompts, tokens, provider payloads,
MCP payloads, and candidate dumps.

## Exit Codes

| Code | Meaning |
|-----:|---------|
| `0` | Completed; at least 16 scenarios passed and all safety invariants passed |
| `1` | Completed; semantic threshold or a safety invariant failed |
| `2` | Could not start or complete because prerequisites, dataset, output, or dependency startup failed |
| `130` | Cancelled by the operator or host shutdown |

Cancellation stops the run, returns exit code `130`, and cleans up temporary state.

## Output Directory

The command creates a run-specific child directory containing `result.json` and
`report.md`.
