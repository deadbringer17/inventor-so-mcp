# Spline tension99 — owned Inventor plugin probe

PC-only NamedPipe product probe; no HTTP or bootstrap. It creates one new
Inventor process with dedicated Part/Sheet fixtures and pins frozen producer99
SHA256 `0D7801E7332B0743FCDB00C343CBDE888B9BBD8B1A70281DB14CD200CECC22E1`.
No ROT attachment, process kill, ordinary add-in replacement or access to
preexisting user documents is allowed by the ownership guards.

The full matrix contains 60 existing-spline edits: Part/Sheet, all three native
fit methods, open/closed, and tension 0/25/50/75/100, with five constructor
points. Each case checks actual typed-reference preview/Abort and revision,
commit and ONE first product snapshot before native geometry inspection,
native curve/point identity, tension readback and an independently created
direct-API tension oracle. Geometry/domain tolerances remain 1e-5 mm/1e-10.
The oracle transaction is always aborted, then the full captured native state
including spline definitions, Dirty and DatabaseRevisionId is compared exactly.

Sixteen negative cases cover stale references, CV spline, subpart, foreign
document reference, negative/decimal tension, unknown option and rollback after
a valid tension edit followed by an invalid second operation. The foreign
reference deliberately names a real line in the other document with declared
type spline; the original reference and actual native type are recorded.
It tests rejection at document binding, not a resolved foreign spline edit.
The original foreign revision is preserved. Because production checks revision
before document binding, the expected step code is computed from that actual
revision and recorded explicitly; the test never rewrites it to force a code.
All refusals require structured rollback, the expected step and exact full
native state/event revision preservation.

The report is `spline-tension-edit99.json` plus 272 raw state sidecars:
four per positive case and two per refusal. Preparation/cleanup comparison
excludes only the target document Dirty/DatabaseRevisionId after legitimate
fixture writes. All other captured contents remain exact. Original disk files
are never overwritten. Worker/module/PID/start and ordinary installation
attestations are archived by `scripts/run-s1-spline-tension99.ps1` from the
repository root. `scripts/verify-s1-spline-tension99.ps1` independently checks
the saved matrix, sidecar hashes, rollback, revisions and sampled oracle geometry.

This is a bounded plugin test, not full spline parity. Desktop controls,
constraints/handles, associative updates, persistence, Core HTTPS, Unity,
Quest and physical use remain open. Compile/readiness is not a native PASS.
Native suites must run serially; start this only after the prior owned suite
is terminal and its process absence/installation restoration are verified.
