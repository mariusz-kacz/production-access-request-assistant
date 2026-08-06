# Markdown Evaluation Report Contract

For a completed evaluation, `report.md` is rendered only from the finalized
`EvaluationRunResult` serialized to `result.json`. It does not independently
calculate totals or safety status.

## Required Sections

1. **Run metadata**: dataset version, UTC timestamp, and non-secret model deployment.
2. **Outcome**: `PASS` or `FAIL`, the scenario pass count, required pass count, and
   `Safety: PASS|FAIL`.
3. **Category summary**: one row for each of the six categories with passed and total
   counts.
4. **Scenario summary**: exactly 18 rows in dataset order containing scenario ID,
   category, status, normalized outcome, and safety status.
5. **Failures**: one subsection for every failed scenario containing failed assertion
   IDs, concise expected-versus-observed application-owned facts, safety violations,
   and compact sanitized evidence.

## Sanitized Turn Trace

A failure trace may contain:

- turn and correlation IDs;
- attempted tool sequence, name, allowlisted identifier argument or discovery marker,
  invoked/blocked disposition, safe outcome, and duration;
- proposal kind, candidate identifiers, clarification target, and structured option
  IDs;
- sanitized candidate identifiers;
- normalized application outcome;
- validation and safe failure codes;
- elapsed time.

It must not contain requester messages, assistant or clarification prose, system
prompts, raw provider content, full MCP arguments/results, access tokens, endpoints,
credentials, complete transcripts, or application-database transcripts.

## Agreement Rule

One synthetic completed-run test compares `report.md` with `result.json` for:

- run status;
- passed count and total scenario count;
- required pass count;
- safety status; and
- every category's passed and total counts.
