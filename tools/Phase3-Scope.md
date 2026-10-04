# Phase 3 scope and dependency checklist

Authority: `AI Master command prompt` revision 5, sections 4.3-4.4, 7.1-7.3, 12 and 18. Predecessor: merged Phase 2A (PR #8). No new KSP API is required for this pure world scheduler.

- Add one `WorldSimulator.Advance(targetUT)` clock owner over pure Phase 2 inventory, Phase 2A reservations, converter recipes and owned colony power. Equal-UT events follow section 12 ordering; stable IDs break ties. Earlier event effects influence the rest of a catch-up gap.
- Integrate all active converters for an endpoint over shared intervals, not one converter over the full gap before another. Split at material empty/full, battery empty/full, event, and fixed environment sample boundaries. Live generation can power a converter with an empty battery; power is separate from stock EC. Idle spans are skipped analytically.
- Make the engine deterministic under arbitrary Advance partitions, including a drill-like source feeding a smelter-like converter feeding a shop-like converter and mid-gap arrivals. Preserve reservations on every material delta. Keep an explicit snapshot/cursor and bounded catch-up API; do not mark unapplied time as complete.
- Phase 3 may expose pure event inputs for later construction, shipment, maintenance, route, and campaign callers. It does not implement those systems, stock EC bridging, solar geometry/KSP adapter, live scenario persistence, UI, or Phase 3A's chronological competing-request allocation and dependency reasons.
- Required fixtures: shared stock and power, an empty-battery live-generation case, a production chain, full output later drained, mid-gap arrival, equal-time event order, arbitrary partitions, saved mid-backlog resume, rollback handling, and predecessor regressions/package checks. Unavailable live game checks remain explicitly open.

