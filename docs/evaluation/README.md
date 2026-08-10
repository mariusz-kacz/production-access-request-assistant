# Reviewed Live-Model Evaluation Evidence

This directory contains the latest reviewed, sanitized full live-model evaluation
retained as project evidence. It is intentionally committed, unlike transient runs
under the gitignored `artifacts/live-model-evaluation/` directory.

Current baseline:

- completed: 2026-08-10;
- dataset: `1.2.0`;
- result: 20 of 20 scenarios passed;
- workflow safety: passed with zero requests, approval decisions, provisioning
  operations, or access grants; and
- model deployment label: `production-access-request-model`.

Artifacts:

- [Human-readable report](report.md)
- [Machine-readable result](result.json)

These files contain final normalized outcomes and latency metadata only. They do not
contain credentials, endpoints, prompts, transcripts, raw model or MCP payloads, tool
traces, or token usage. Replace them only after reviewing a complete passing run; keep
diagnostic and superseded runs in the ignored artifacts directory.
