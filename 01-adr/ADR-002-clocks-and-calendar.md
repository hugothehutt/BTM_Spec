# ADR-002 — Four clocks; the market calendar is data

**Status:** Accepted **Reversibility:** Moderate

## Context

A naive design runs one loop over 15-minute slots. Real operation has at least
four rhythms with different information sets and different irreversibility:
accounting periods, a slow value-function loop, market gates, and event-driven
intraday ticks. Gate times, product definitions and auction structures change by
regulation and by venue.

## Decision

Four clocks as defined in `00-overview/01-system-model.md` §3: `C_accounting`,
`C_slow`, `C_gate`, `C_tick`.

**All calendar and product structure is data, loaded from a versioned
`MarketCalendar` artefact.** This includes: gate closure times, auction
schedules, product block definitions (aFRR block length and symmetry), delivery
period definitions, exchange holidays, tick and lot sizes, and the
Hochlastzeitfenster table per DSO per year.

**The calendar artefact is part of the backtest manifest** and is replayed with
the run. A backtest of 2024 uses the 2024 calendar, not today's.

**The tzdata version is pinned and recorded** in the run manifest, because civil
time itself is versioned data.

## Consequences

- A regulatory or venue change is a data change plus a test fixture, not a code
  change. Given that AgNes changes the German network charge structure with
  effect from 2029 (ADR-011), this is not hypothetical.
- The engine cannot hardcode "96 slots per day". Every horizon is expressed in
  `SlotSpan` derived from the calendar. DST days are test fixtures, not edge
  cases discovered in production.
- Backtests across a rule change are honest: the engine sees the rules that
  applied at the time.
- Cost: an indirection on every time question, and a calendar artefact that must
  itself be validated (`T1` conformance: monotone gates, no overlapping blocks,
  every delivery slot covered by exactly one product block).

## Rejected

- **Gate times as constants.** They change, and a wrong gate time silently makes
  a backtest optimistic — the worst possible failure because it looks like alpha.
- **A single tick loop with `if (isGateTime)`.** Conflates information sets; the
  Planner then cannot express "this decision is binding, that one is not".
- **System timezone.** Non-reproducible across machines and across tzdata
  releases.
