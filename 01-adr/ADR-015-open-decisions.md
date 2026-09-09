# ADR-015 — Register of deliberately deferred decisions

**Status:** Open **Reversibility:** —

A decision reaches this register when an accepted ADR has identified a gap,
stated why it is not closing it, and would otherwise leave that gap discoverable
only by reading the ADR that opened it. The point is that a known gap and an
oversight look identical six months later unless one of them is written down.

Each row names what is undecided, the ADR that opened it, and what has to exist
before it can be decided. A row leaves this register by becoming an ADR of its
own, not by being closed here.

| # | Open decision | Opened by | Blocked on |
|---|---|---|---|
| 015-1 | **How `V` prices the out-of-horizon peak remainder.** Charging the full rate above the floor over-charges an excursion that a later one would have dominated. `ADR-007` conditions `V` on `peakState`, but `C2` §4 ships `V` as one curve in SOC with `conditionedOn` selecting which curve *before* the solve, so `V` is not a function of the in-solve `zPeak` and cannot carry the remainder however well it is fitted. | [ADR-020](ADR-020-peak-charge-carries-no-proration.md) | A domain and a discretisation for `peakBuckets` — `L0`:132-133 is its only occurrence in the corpus and states neither; and a decision on whether `V` enters `L3`'s objective jointly in `(socTerminal, zPeak)` or stays `V(socTerminal)`. `W7` as scoped delivers qualification-state conditioning only (`P0`:178). |
| 015-2 | **Whether the peak epigraph is scenario-indexed.** `TN-01` claim 10 rules the single unindexed `z_peak` of `ADR-008`:36-41 the *deterministic* term, not the risk-averse one `L2`:61-66 describes, and its §9 asks for one of the two to go. `ADR-020` changed the term's coefficient and deliberately left its row count and variable set alone. | [ADR-020](ADR-020-peak-charge-carries-no-proration.md) | A cost estimate for `z_s` per scenario — `TN-01` puts it at ≈12k rows per regime at `S=64, H=192` — measured against a real solve rather than counted. |
