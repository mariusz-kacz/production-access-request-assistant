# Live-Model Evaluation Report Contract

`report.md` is rendered from the same completed run result serialized to `result.json`.
It does not independently calculate totals.

## Required Sections

1. Dataset version, UTC timestamp, and non-secret model deployment.
2. `PASS` or `FAIL`, passed count, required count, and side-effect safety result.
3. Six category passed/total counts for a full run or the selected scenario's
   category count for a focused run.
4. One scenario row for a focused run or exactly 20 rows for a full run, each
   containing ID, category, status, normalized outcome, and elapsed milliseconds.
5. Failure-only expected-versus-observed final application facts.
6. Failure-only observed application state: deterministic reason summary, normalized
   outcome, safe application codes, canonical candidate facts, clarification target,
   environment option identifiers when present, and the final schema-validated model
   response message when present.

The report contains no tool trace, candidate proposal payload, token usage, prompt,
transcript, endpoint, credential, provider payload, MCP payload, or exception text.
Only a failed scenario may include its final bounded, schema-validated model response
message.
