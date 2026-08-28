# Teams Layer Refactor

- **Status:** Complete
- **Prepared:** 2026-08-28
- **Branch:** `feature/decouple-teams-approval-flow`
- **Approved direction:** Consolidate the Teams namespace after the initial target-name cleanup.

## Objective

Keep Teams responsible for authenticated activity transport and Adaptive Card
presentation. Move authoritative ready-request review assembly into Core, consolidate
Teams outcome/card presentation behind one presenter, and retain all existing
user-visible behavior and security boundaries.

## Architecture decisions

- `GovernedAccess.Core` returns a presentation-neutral `PreparationReview` assembled
  from current authoritative data.
- `TeamsResponsePresenter` is the single semantic-to-Teams presentation boundary.
- `TeamsRequestHandler` invokes preparation/confirmation use cases and logs safe
  outcomes; `TeamsAccessRequestAgent` remains the SDK transport boundary.
- Adaptive Card JSON construction remains a focused serialization helper because it
  owns the external Teams card contract.

## Tasks

### Task 1: Extract authoritative preparation review

**Acceptance criteria:**

- [x] Core reloads every fact displayed on a ready card and fails closed on mismatch.
- [x] The result contains no Teams or Adaptive Cards SDK type.

**Verification:** focused `PreparationReviewServiceTests`, then unit tests.

**Dependencies:** None.

### Task 2: Consolidate Teams presentation

**Acceptance criteria:**

- [x] One presenter maps all typed turn and confirmation results to text or cards.
- [x] Card JSON, fixed prose, locale handling, and action payload remain unchanged.
- [x] `PreparedRequestCardFactory`, `TeamsReadyCardPresentation`, and the separate
  response renderer are removed.

**Verification:** focused Teams presenter/card tests and integration build.

**Dependencies:** Task 1.

### Task 3: Simplify the request boundary and composition

**Acceptance criteria:**

- [x] The request handler owns use-case invocation, closed action parsing, and safe
  outcome logging; the agent owns only SDK transport and activity updates.
- [x] Existing production and isolated full-host journeys retain their results.
- [x] No migration `Target` name remains in the Teams namespace.

**Verification:** project-mandated build, unit, and integration sequence; review the
diff and run `git diff --check`.

**Dependencies:** Tasks 1 and 2.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Moving authority checks changes fail-closed behavior | Characterize every success and failure path before moving code. |
| Consolidation changes observable Teams output | Preserve existing card/prose assertions and full-host journeys. |
| Large mechanical rename obscures semantic changes | Land Core extraction and Teams consolidation as separate verified commits. |

## Completion evidence

- `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
  passed with zero warnings and zero errors.
- `GovernedAccess.UnitTests` passed all 155 tests.
- `GovernedAccess.IntegrationTests` passed all 223 tests with the three-minute hang
  timeout enabled.
- `git diff --check` passed, and source scans found no retired Teams migration names.
