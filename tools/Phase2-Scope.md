# Phase 2 scope and dependency checklist

Design authority: `AI Master command prompt` revision 5, sections 3.4, 4.1-4.3, 6, 17, and 18. Phase 1's accepted KSP API feasibility is the predecessor.

- Pure Phase 2: resource identities and validated stock metadata; provenance-preserving ore lots and exact grade bands; per-resource integrated and dedicated capacities; one-time starter-kit receipts; aggregated recipe limits/dump accounting; atomic, idempotent material commands and versioned pure snapshots.
- Current adapter: verify KSP 1.12.5 `PartResourceLibrary`, `PartResourceDefinition` density/unitCost, `Funding`, and `ConfigNode` signatures before compiling an adapter that reads stock metadata. No player inventory is activated by this phase.
- Explicitly deferred: ReservationBook/floors/escrow (2A); world event scheduler (3); live scenario persistence and deployment (4 and 10); mining (5); power (6); fuel factories (7); depot registration (11); campaign gameplay (15). Phase 2 may expose pure inputs for these callers, not run them.
- Acceptance: independent fixtures for finite/nonnegative boundaries, provenance/grade-band stacking, duplicate recipe legs, output-full and slag dump, density/mass, kit grant/reload identity, specific storage caps, atomic rollback, and duplicate command IDs. Re-run Phase 1 regression tests and package checks.
- In-game checks remain unresolved until the owning gameplay phases. The Phase 2 stock metadata adapter must compile against the local KSP installation; that alone is not an in-game check.

