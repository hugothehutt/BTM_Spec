# ADR-014 — Five named degradation modes; quality drives risk, not branches

**Status:** Accepted **Reversibility:** Moderate

## Context

Data goes missing. A forecast is late, a meter drops out, the value function has
not been refreshed, the solver times out, the reserve price feed is stale. The
question is not whether this happens but what the engine does, and how the
decision is made visible.

Two common designs both fail:

- **Silent imputation.** Fill the gap with the last value, carry on, log a
  warning nobody reads. The engine then makes full-confidence decisions on
  invented data. The failure is invisible until Settlement, by which point the
  money is gone.
- **Fail closed on anything.** Halt on any missing field. In practice something
  is always slightly stale, so the engine spends its life halted, and someone
  adds an exception, and then another, and there is no policy left.

There is also a subtler failure: handling degradation with `if` branches scattered
across layers. Each branch is individually reasonable; collectively they form a
state machine nobody designed and nobody can test.

## Decision

### 1. Quality is data, not control flow

Every value crossing C1 carries `(value, knowledgeTime, provenance, quality)`:

```
provenance : Measured | Forecast | Revised | Imputed | Default
quality    : Good | Degraded | Stale | Imputed | Missing
```

Downstream layers **do not branch on quality**. They map quality onto a
**risk multiplier** that feeds the existing risk machinery: a degraded price
belief widens the CVaR confidence level and shrinks position bounds; a stale
value function attracts a staleness penalty on its terminal value; an imputed
load forecast widens the peak epigraph's safety margin.

This is the central idea. Bad data does not take a different code path — it takes
the *same* path with more conservative parameters. There is one code path to
test, and the response to degradation is continuous and tunable rather than a
cliff.

### 2. Five named modes, with hysteresis

Global behaviour is a single mode, computed centrally from the quality state and
written to L0:

| Mode | Trigger | Behaviour |
|---|---|---|
| `NORMAL` | all critical inputs Good | full strategy |
| `DEGRADED` | non-critical inputs stale or imputed | full strategy, widened risk, reduced position limits |
| `DEFENSIVE` | critical input stale, or value function beyond validity, or solver hitting time limit repeatedly | honour commitments, maintain peak protection, no new speculative exposure |
| `SAFE` | critical input missing, or commitment ledger inconsistent | honour existing commitments only; cancel unfilled speculative orders; hold SOC in a safe band |
| `HALT` | physical constraint violated, state store unreadable, or contract validation failure | submit nothing; alert; require operator acknowledgement to resume |

**Transitions are hysteretic** — entering `DEFENSIVE` requires N consecutive bad
ticks, leaving it requires M consecutive good ones, with `M > N`. Without this
the engine flaps on a marginal feed and the flapping is worse than either state.

**Every transition is logged with its cause and is part of the settlement
record**, so a period of poor P&L can be attributed to degraded operation rather
than to strategy.

### 3. Two things are never degraded away

- **Peak protection.** The epigraph floor from realised peak and the
  qualification guard (ADR-011) apply in every mode including `SAFE`. Losing a
  year of network-charge reduction because a price feed was stale is not an
  acceptable outcome of any degradation path.
- **Commitment feasibility.** Awarded aFRR capacity is a physical obligation. In
  every mode, the SOC corridor required to deliver confirmed commitments is a
  hard constraint, and if it cannot be met the engine goes to `HALT` and alerts
  rather than quietly under-delivering.

### 4. The fallback ladder for a single field

Applied in order, stopping at the first success, recording where it stopped:

1. Primary source, fresh
2. Primary source, stale within `maxStale` → `Stale`
3. Secondary source → `Degraded`
4. Model imputation (persistence, climatology, ensemble mean) → `Imputed`
5. Conservative default — the value that biases toward inaction → `Default`
6. No safe default exists → the field is `Missing` and the mode escalates

Rung 5 deserves emphasis: the default is chosen to be *conservative for the
decision it feeds*, which is field-specific and declared in the contract's field
table. A missing load forecast defaults high (peak protection); a missing reserve
price defaults low (do not chase revenue on invented prices).

## Consequences

- One code path, parameterised. Testable by injecting quality states rather than
  by simulating outages (`T2` includes a degradation matrix test: every field ×
  every quality level, asserting the mode and the risk response).
- Degradation is visible in the settlement record and separable in P&L
  attribution.
- Cost: every field needs a declared `maxStale`, a fallback chain and a
  conservative default. This is tedious and it is exactly the work that pays for
  itself the first time a feed dies.

## Rejected

- **Boolean `isValid` per field.** Loses the gradation that makes a proportional
  response possible.
- **Per-layer local handling.** Produces an emergent, untested state machine.
- **Fail-closed on everything.** Unoperable; erodes into ad hoc exceptions.
