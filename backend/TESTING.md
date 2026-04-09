Backend test architecture should follow the same boundaries as the runtime architecture.

Current layers:
- `SimulationAssessment.Core`: shared contracts and host-agnostic serialization helpers.
- `SimulationAssessment.SvrFire`: deterministic SVR Fire replay, scoring, and deficit/report rules.
- `SimulationAssessment.Backend`: backend host library that owns log ingestion, export packaging, optional narrative composition, and the reusable assessment/export application entrypoint.
- `SimulationAssessment.Cli`: thin executable front-end over the backend host library.
- `backend/fixtures/svr`: committed replay fixtures for end-to-end deterministic regression.

Current test surfaces:
- `backend/SimulationAssessment.SvrFire.Tests`: deterministic scenario-layer tests for replay, scoring, and deficit/report rules without CLI or AI dependencies.
- `backend/SimulationAssessment.Tests`: fast backend-host tests for narrative parsing/fallbacks, export packaging, and the direct backend application seam without spawning the CLI executable. These tests stay on backend/core contracts instead of compiling against scenario replay types directly.
- `scripts/run-svr-backend-unit-tests.ps1`: wrapper that builds the backend library, runs both scenario-layer and backend-host unit-style projects, then smoke-builds the thin CLI.
- `scripts/run-svr-assessment-regression.ps1`: fixture-driven regression across the backend replay/export path.
- `scripts/run-svr-narrative-regression.ps1`: quick wrapper for backend narrative-focused tests.

What should stay true as the repo grows:
- Deterministic truth tests should live below the CLI layer. Scoring, replay, and deficit logic should be testable without invoking export or AI code.
- Backend-host tests should validate host concerns only: log loading, export manifests, packaging, application orchestration, and optional narrative boundaries.
- Scenario replay result types should stay below the backend host seam. If backend tests need replay-shaped inputs, use a host-neutral backend envelope rather than taking a direct compile-time dependency on scenario-specific replay classes.
- The CLI should stay thin enough that a build smoke test is usually sufficient once backend-host behavior is covered through `SimulationAssessment.Backend`.
- Fixture regressions should lock canonical evidence -> deterministic assessment behavior, not Unity scene implementation details.
- AI narrative tests should assert grounding, fallback, and append-only behavior. They must not become the place where truth is validated.
- Future Unity editor/playmode tests should validate adapter wiring, authored scenario bindings, and operator flow, but not re-own deterministic assessment truth already covered in backend tests.
- If new scenarios are added, each scenario should get its own deterministic replay fixtures and scenario-layer tests without widening SVR-specific assertions into shared core tests.

Recommended next evolution:
1. Add a small set of contract tests for shared core types if serialization/versioning rules start changing independently of SVR Fire.
2. Keep fixture directories as canonical replay evidence only. Do not mix generated exports back into committed fixture inputs.
3. When a second scenario arrives, mirror the `SimulationAssessment.SvrFire.Tests` pattern instead of broadening SVR assertions into shared projects.
