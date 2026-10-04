# Phase 2A scope and dependency checklist

Authority: `AI Master command prompt` revision 5, sections 4.3-4.4 and 18.1-18.2. Predecessor: merged Phase 2 pure inventory/material model (PR #7). No new KSP API is needed in this pure phase.

- Build one pure `ReservationBook` for committed matter, incoming capacity, abstract slots, protected floors, and physical escrow. Every entry carries stable IDs, owner/type, endpoint, resource/slot, quantity, UT, priority, state, and receipt. Derive reserved totals from entries; keep no competing reserved-total map.
- Query available matter by subtracting existing claims and applicable floors; enforce overlapping ore filters against exact lots. Bound quota floors by remaining objective after already assigned cargo. A forecast is read-only and cannot satisfy a reservation.
- Reserve, commit to escrow, release/return, consume and restore atomically and idempotently. On restore, quarantine duplicate/conflicting claims rather than inventing inventory. Escrow is removed from inventory exactly once and still counts as physical matter owned by its reservation.
- Integrate Phase 2 immediate transfers, consumption and recipe material commits with the same `ReservationBook` so they cannot bypass existing claims or incoming capacity reservations. Do not implement Phase 3A chronological scheduling or any live gameplay.
- Acceptance fixtures: 60-of-100 Metal vs 80 export; overlapping broad/narrow ore floors; capacity contention; two consumers versus stock transfer; escrow mass/conservation and partial release; duplicate commands; restore conflict quarantine; predecessor regression and two-DLL package.
- No Phase 2A in-game caller exists. Live scenario schema and scene handling remain Phase 4, owned power Phase 6, construction Phase 10, and transport Phase 12 onward.

