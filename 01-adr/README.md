# Architecture Decision Records

Fifteen decisions that are expensive to reverse. Each records context, the
decision, its consequences, and what was rejected and why. ADR IDs are stable
and referenced throughout the specification.

| ID | Decision | Status | Reversibility |
|---|---|---|---|
| [001](ADR-001-language-and-solver-boundary.md) | C# for the engine; solver behind an abstraction | Accepted | Cheap |
| [002](ADR-002-clocks-and-calendar.md) | Four clocks; market calendar is data | Accepted | Moderate |
| [003](ADR-003-typed-quantities.md) | Typed quantities and two reference frames | Accepted | Expensive later |
| [004](ADR-004-bitemporal-belief-store.md) | Bitemporal, three-tier, content-addressed Belief store | Accepted | Very expensive |
| [005](ADR-005-joint-scenario-ensemble.md) | One joint scenario ensemble, shared scenario axis | Accepted | Very expensive |
| [006](ADR-006-feedback-resolution.md) | Forward within a tick, lagged across ticks via L0 | Accepted | Very expensive |
| [007](ADR-007-value-function-not-lambda.md) | Publish V(SOC), not scalar λ_SOC | Accepted | Expensive |
| [008](ADR-008-linearizable-primitives.md) | Valuation emits linearizable primitives, not prices | Accepted | Very expensive |
| [009](ADR-009-term-ownership.md) | Term ownership matrix; composer enforces no double count | Accepted | Moderate |
| [010](ADR-010-cross-market-tier-ladder.md) | Three-tier co-optimisation ladder with a measured gap | Accepted | Moderate |
| [011](ADR-011-tariff-regime-plugin.md) | Tariff regime is a plug-in; AgNes capacity price is a first-class regime | Accepted | Expensive later |
| [012](ADR-012-order-intent.md) | Planner emits intent; quoting is a separate policy | Accepted | Moderate |
| [013](ADR-013-determinism-and-replay.md) | Content-addressed determinism; every artefact is replayable | Accepted | Very expensive |
| [014](ADR-014-degradation-ladder.md) | Five named degradation modes; quality drives risk, not branches | Accepted | Moderate |
| [015](ADR-015-open-decisions.md) | Register of deliberately deferred decisions | **Open** | — |

## Reversibility as a sequencing guide

"Very expensive" decisions must be implemented first and correctly, because
retrofitting them means rewriting every layer above. In practice this means the
first two workstreams are the Belief store (004, 005) and the contract surface
(006, 008), before any economics or optimisation is written. See
`05-implementation/P0-workstreams.md`.
