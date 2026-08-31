# Documented Live-Model Evaluation Runs

- **Status**: Reviewed generated evidence
- **Last reviewed**: 2026-08-31

This directory contains deliberately reviewed, immutable copies of selected
live-model evaluation artifacts. The evaluator's generated `artifacts/` directory
remains ignored; adding a run here requires an explicit review for provenance,
synthetic-only content, credentials, and consequential side effects.
Repository attributes preserve the generated artifact bytes so the recorded hashes
remain stable across platforms.

| Completed | Run | Outcome | Evidence qualification |
|---|---|---|---|
| 2026-08-31 | [`729a0091-b7b1-4d25-bc6c-94c78ebe64e2`](2026-08-31-729a0091b7b14d25bc6c94c78ebe64e2/report.md) ([JSON](2026-08-31-729a0091b7b14d25bc6c94c78ebe64e2/result.json)) | PASS: 14/14 promoted groups, 42/42 variations, absolute safety PASS, zero consequential side effects | Current-dataset, clean-source promotion evidence. The full-inventory run is promotion eligible, the dataset hash matches the golden dataset, and the recorded source commit matched the clean evaluated `HEAD` during retention review. |
| 2026-08-31 | [`ae36feff-01f1-49b6-9b4e-8c5579dcd9e8`](2026-08-31-ae36feff01f149b69b4e8c5579dcd9e8/report.md) ([JSON](2026-08-31-ae36feff01f149b69b4e8c5579dcd9e8/result.json)) | PASS: 14/14 promoted groups, 41/41 variations, absolute safety PASS, zero consequential side effects | Documented passing run. The working tree was not clean, so the recorded `sourceCommit` does not identify the exact evaluated source and this run is not clean-source promotion evidence. |

## Integrity

For current run `729a0091-b7b1-4d25-bc6c-94c78ebe64e2`:

- `result.json` SHA-256:
  `cde1d82097a7d5eb0ab498b28f9ab98a4f5a9e618d0de1e52853346480e4d580`;
- `report.md` SHA-256:
  `622e6acac1d2932fb5f50864ea4f98a411fdfc67acbadb59389c7ee9a0a3ec22`;
- recorded dataset SHA-256:
  `e5e46da41ffa012693570f604635c442c1410508d4d1307a11674bd06ec13df1`;
  and
- recorded source commit:
  `2f4e45c980ec5eef33af854b83ba9df811b9d762`.

The retained files are byte-identical to the reviewed generated artifacts. All 43
requester messages match the checked-in synthetic dataset, the dataset hash matches
the current golden bytes, the source commit matched the clean evaluated `HEAD` during
retention review, and the artifacts contain no failure codes or consequential side
effects.

For earlier run `ae36feff-01f1-49b6-9b4e-8c5579dcd9e8`:

- `result.json` SHA-256:
  `3cbd180a1f34d59693f2273db7c60b4777dcf5f2b4b5f739af7ecf0eeb63c3b6`;
- `report.md` SHA-256:
  `8b17b8b4124b5a480dba81cb50531992ac64694379fd82fd2c2b93968e50c0d8`;
  and
- the recorded dataset SHA-256
  `1d6feb66f74d1bd741c9c0bee3da338100a6cd0a62c7d48f1dd1ab7a9db26c36`
  matches the executable dataset reviewed with the run.

The checked-in golden dataset was edited after the earlier run while retaining the
`deterministic-intake-3.0.1` version label. It no longer matches the recorded dataset
hash, so these immutable artifacts do not validate the current exact inputs and
expectations.

`scope.promotionEligible: true` in a result means the evaluator covered the full
inventory. It does not override the separate clean-source provenance requirement in
the [live-model evaluation guide](../../live-model-evaluation.md).
