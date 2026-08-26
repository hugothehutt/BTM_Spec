# ADR-009 — Term ownership matrix; the composer enforces no double-counting

**Status:** Accepted **Reversibility:** Moderate

## Context

The composer applies terms in a fixed order:

```
tariff → opp-cost + λ_SOC → pPoiRealisedPeakMw (last) → validate
```

with the stated reason that peak headroom must be priced on top of a battery
whose energy is already marked with its opportunity cost, and that reversing the
order would double-count.

That reasoning is right, but as an ordering convention it is fragile: it is a
fact about the code that must be remembered. The failure mode — an economic
effect counted twice — produces a plausible number, passes every type check, and
biases the strategy in a direction that looks like profit. It is exactly the
class of error that should be structurally impossible rather than documented.

## Context, sharpened

Double-counting risk here is concrete, not theoretical:

- Energy is priced by `OppCost` (as internal cost) *and* by the spot views (as
  market value). If both mark the same MWh, arbitrage appears free.
- aFRR capacity reserves SOC headroom that `IdOption` may also plan to use.
- The peak term prices POI import that the tariff term also prices per MWh. These
  are legitimately two different charges — but only if each is applied to its own
  base.
- Degradation may be charged by `OppCost` per throughput and again inside `V(SOC)`
  if the value function was fitted on net-of-degradation cashflows.

## Decision

**Every emitted term declares what it prices, and the composer rejects any bundle
in which one (effect, decision variable, slot) triple is priced more than once.**

Each term carries an `EconomicEffect` tag from a closed enumeration:

```
SpotEnergyValue · IdEnergyValue · ReserveCapacityRevenue · ReserveEnergyRevenue
ImbalanceCost · NetworkPeakCharge · NetworkVolumetricCharge · LeviesAndTaxes
EnfgLevies · SubsidyRevenue · CycleDegradation · StoredEnergyContinuation
ActivationRisk
```

The normative owner for each effect is the matrix in `02-layers/L2-valuation.md`
§5. Every effect maps to exactly one owning **view** — never to the Planner,
which adds no economics of its own (L3 §2).

The **term ownership matrix** (`02-layers/L2-valuation.md` §5) is a normative
table mapping each effect to exactly one owning view. The composer asserts:

1. **Coverage** — every effect in the active configuration is claimed by exactly
   one view. Unclaimed effects are a warning; the tick proceeds with the effect
   priced at zero and the omission logged, because silently missing revenue is
   safer than silently double-counting cost.
2. **Exclusivity** — no effect is claimed twice for the same `(variable, slot)`.
   Violation is a hard failure (`INV-V-01`), never a warning.
3. **Base correctness** — terms declare the *base* they apply to
   (`PoiImport`, `BatteryThroughput`, `MarketVolume`, `TerminalSoc`,
   `ReserveCapacity`, `Delineation`). A volumetric
   network charge on `PoiImport` and a peak charge on `PoiImport` are both legal
   because they are different effects on the same base; two `NetworkPeakCharge`
   terms on the same base are not.

   `LeviesAndTaxes` on `PoiImport` and `EnfgLevies` on `Delineation` are likewise
   legal and are **not** a double count: they are disjoint component lists on
   disjoint bases (ADR-017). The reducible EnFG components are charged on `(21)`;
   what a delineation regime cannot reduce stays on `PoiImport`. Relief is never
   booked as revenue — `(20)` is a diagnostic quantity, not an effect.

**Ordering becomes a declared, verified property rather than a convention.** The
composition pipeline declares each stage's preconditions and postconditions:

| Stage | Requires | Establishes |
|---|---|---|
| 1. Tariff | nothing | POI energy is fully marked |
| 2. OppCost + V(SOC) | POI energy marked | battery energy marked; terminal value attached |
| 3. Reserve coupling | battery energy marked | SOC corridor constraints present |
| 4. Peak (epigraph) | POI energy marked **and** battery energy marked | peak variable bounded below by realised peak |
| 5. Validate | all above | bundle is well-formed and complete |

If a stage's preconditions are unmet, composition **fails loudly**. Running the
stages in the wrong order therefore produces an exception, not a wrong number.
That is the entire point of this ADR.

## Consequences

- The ordering rule is enforced by the machine, so it survives refactoring,
  new contributors, and agent-authored changes.
- Adding a view requires updating the ownership matrix, which forces the
  "what does this actually price, and who else prices it?" conversation at the
  right moment.
- The matrix doubles as the basis for Settlement's P&L attribution (`C4`/`L5`):
  realised P&L is decomposed into the *same* effect enumeration, so planned and
  realised value are comparable term by term. This is what makes the four-bucket
  error decomposition possible.
- Cost: a tag on every term and a matrix to maintain.

## Rejected

- **Documented ordering convention.** The current state. Correct but unenforced.
- **A single monolithic pricing function.** No double-count risk, but untestable
  and unextendable — the failure the layered design exists to avoid.
- **Post-hoc reconciliation only.** Catches double-counting after the fact, in
  aggregate, long after the bad decisions were taken.
