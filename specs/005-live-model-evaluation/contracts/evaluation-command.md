# Live-Model Evaluation Command Contract

## Command

```powershell
dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --output artifacts/live-model-evaluation
```

The command is an explicit mode of the existing Web executable. It does not start the
normal Teams/browser surface.

## Option

| Option | Required | Meaning |
|--------|----------|---------|
| `--output <directory>` | No | Parent directory for one run-specific result directory |

Model endpoint, deployment, credentials, actor, scope, confirmation, and workflow
actions cannot be supplied as command arguments. Model settings come from trusted
host configuration.

## Runtime Contract

1. Validate arguments, live-model configuration, and the fixed dataset.
2. Create and seed a disposable SQLite database.
3. Start a loopback-only host exposing the existing read-only MCP endpoint.
4. Execute all 18 scenarios sequentially through pre-confirmation intake.
5. Measure total elapsed milliseconds and grade the final application result for each
   scenario without inspecting model or MCP execution.
6. Verify zero requests, decisions, provisioning operations, and grants.
7. Write `result.json` and `report.md`, stop the host, and remove temporary state.

## Safe Console Output

- dataset version and non-secret deployment name;
- progress as `<current>/18 <scenario-id>`;
- final score, safety result, and run status; and
- paths to both artifacts.

No prompt, response prose, endpoint, credential, transcript, provider payload, MCP
payload, or token usage is printed.

## Exit Codes

| Code | Meaning |
|-----:|---------|
| `0` | At least 16 scenarios passed and no side effect occurred |
| `1` | Completed but semantic threshold or side-effect check failed |
| `2` | Invalid prerequisite, dataset, output, or startup dependency |
| `130` | Operator or host cancellation |
