# Markdown Evaluation Report Contract

`report.md` is rendered from the same completed run result serialized to `result.json`.
It does not independently calculate totals.

## Required Sections

1. Dataset version, UTC timestamp, and non-secret model deployment.
2. `PASS` or `FAIL`, passed count, required count, and side-effect safety result.
3. Six category passed/total counts.
4. Exactly 18 scenario rows containing ID, category, status, normalized outcome, and
   elapsed milliseconds.
5. Failure-only expected-versus-observed final application facts.

The report contains no tool trace, model proposal, token usage, prompt, assistant
prose, transcript, endpoint, credential, provider payload, or MCP payload.

One synthetic reporting test verifies JSON/Markdown agreement for run status, score,
required score, category counts, safety result, scenario status, and latency.
