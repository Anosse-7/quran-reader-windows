# LAN-1.0 — Local-First Desktop/Mobile Synchronization

Status: **PLANNED. Documentation and initial architecture outline only; implementation
requires separate authorization.** No mobile application/framework, synchronization
host, listener, service, firewall rule, scheduled task or permission is created.

Intake date: 3 November 2026; starting HEAD
`afc9fd2cb562574150da109ca3cafb8fa15bbb6e`.
UI-1.2I.1 is engineering-verified for tested scenarios at Release source
`30d90c0`, with supplemental diagnostics at `a514975`. User visual approval and
external acceptance/release gates remain pending; see the
[handoff](../../architecture/STATUS.md) and
[content provenance](../../../CONTENT-AND-ASSET-RIGHTS.md).

The [coordinated roadmap](../../ROADMAP.md) owns cross-feature status, sequencing
and shared-state distinctions. The supplied brief below owns LAN's detailed
requirements. Its historical stabilization gate is satisfied for this intake;
its repeated implementation prohibitions remain in force. The final section adds
explicitly proposed planning notes, not approved protocol or deployment decisions.
Separate architecture, pairing and threat-model documents are deferred to the
later authorized planning stage required by section 13.

Preserved chat brief SHA-256 (UTF-8, LF, final newline; not an attachment or
transport-byte fingerprint): `eaf6463861f1ec12ca4a39dabf848fbc352167f00e132881c3aaca76cda687b9`.

---

# CODEX ROADMAP ADDENDUM — LAN-1.0
## Local-First PC ↔ Mobile Qur’an Synchronization

### Status: Future development proposal — PLANNED

This is an additional product requirement for the existing Windows-native Qur’an Reader project.

**Do not implement this feature now. Do not interrupt Phase UI-1.2I.1 stabilization.**

After the current phase achieves its required engineering closure, incorporate this proposal into the coordinated future-development roadmap alongside KH-1.0–KH-1.3 and BM-1.0.

No mobile application has been established by this proposal. Design the synchronization architecture for a possible future Android and/or iOS companion application without prematurely choosing its framework.

## 1. Product objective

Enable a future Qur’an Reader mobile application and the existing Windows application to synchronize user reading state privately across the same reachable local network.

The system must work without a cloud account or public internet connection.

The Windows PC acts as the local synchronization coordinator through a lightweight service.

Both devices retain independent offline storage.

When the mobile device leaves the network, reading must remain fully functional.

When connectivity returns, the devices exchange and reconcile all pending changes.

Synchronization must be bidirectional.

## 2. Required scenarios

**Scenario A — Reading on the phone**

The user leaves home and continues reading on a phone.

The phone records confirmed reading progress, bookmarks and reading position locally.

After returning to the home network, the mobile client discovers the paired PC and synchronizes.

The PC receives the newly confirmed progress and adjusts the Khatma schedule if required.

**Scenario B — Reading on the computer**

The user continues reading on Windows while the phone is disconnected.

When the phone reconnects, the phone receives the PC's new reading progress and other synchronized changes.

**Scenario C — Changes on both devices**

The user makes changes independently on the PC and phone while disconnected.

On reconnection, the system merges compatible changes without losing progress or creating duplicates.

**Scenario D — PC unavailable**

The PC is sleeping, powered off or its synchronization service is stopped.

The phone continues functioning offline.

Pending changes remain safely stored until a later successful connection.

## 3. Shared synchronization model

Reuse the project's authoritative `AyahId` and stable reading-position concepts.

Synchronize user-owned state, not visual coordinates.

Candidate synchronized entities:

- Last reading position.
- Confirmed Khatma progress.
- Khatma plan parameters and amendments.
- Saved ayah bookmarks.
- Primary ribbon bookmark identity.
- Reading sessions and completion records.
- Selected reminder preferences where appropriate.

Distinguish:

1. The last viewed position.
2. The primary ribbon's explicitly saved position.
3. Ordinary saved bookmarks.
4. Confirmed completed Khatma ranges.

Never interpret viewing, selecting, scrolling, bookmarking or synchronizing as proof of completed recitation.

The mobile and desktop applications must each compute their own page layout from stable content anchors.

Do not synchronize adaptive leaf numbers or absolute on-screen positions as durable reading identities.

## 4. Bidirectional synchronization and conflicts

Design a durable synchronization protocol with:

- Stable device identities.
- Persistent per-device change records.
- Unique operation identifiers.
- Revision or causal-order metadata.
- Idempotent application of repeated changes.
- Acknowledgments and retry.
- Interrupted-transfer recovery.
- Explicit handling of deletions.
- Versioned schema and protocol negotiation.
- Conflict detection.

Avoid using wall-clock timestamps alone as the source of truth.

Establish entity-specific merge rules.

Confirmed progress must never be counted twice, skipped, or silently rolled backward.

Bookmark removals must remain effective after previously disconnected devices reconnect.

If two devices change incompatible Khatma configuration values, retain both candidates and require a clear resolution policy.

Do not silently overwrite the user's changes.

After synchronization, rerun adaptive scheduling only when relevant confirmed progress or plan inputs have changed.

Keep all calculations deterministic and auditable.

## 5. Secure device pairing

Design an explicit user-approved pairing process.

A proposed experience is:

1. Enable Local Sync in the Windows application.
2. Display a QR code or short-lived pairing code.
3. Open the future mobile companion.
4. Pair with the selected PC.
5. Establish an authenticated trusted-device relationship.
6. Display paired-device status and last successful synchronization.

Use appropriate authenticated encrypted transport, with secure key storage and server identity verification.

Investigate TLS and device-specific trust establishment suitable for an entirely local system.

Do not transmit sensitive synchronization data over unauthenticated plaintext HTTP.

Do not place long-lived secret credentials inside an unprotected QR code.

Support device unpairing, credential revocation and recovery from lost phones.

Do not allow other devices to access progress merely because they share the same Wi-Fi network.

## 6. Local discovery and reconnection

Investigate standards-based local discovery, including mDNS/DNS-SD where suitable.

The phone should reconnect opportunistically when:

- Its paired PC is reachable.
- The required local-network permission is available.
- Both devices permit synchronization.
- The mobile operating system permits the relevant activity.

Provide a safe manual connection fallback where discovery is unavailable.

Support PCs connected by Ethernet while the phone uses Wi-Fi on the same reachable LAN.

Document that guest Wi-Fi isolation, router settings, VPN routing and firewall restrictions can prevent discovery or connection even when devices appear to use the same Wi-Fi name.

Do not continuously scan the network searching for phones.

Prefer mobile-initiated discovery and synchronization with a PC endpoint that is available when explicitly enabled.

Do not promise immediate reconnection during arbitrary mobile background states.

## 7. Lightweight Windows synchronization host

Investigate the best deployment strategy for the actual Windows reader.

Potential approaches include:

- An in-process local service while Qur’an Reader runs.
- An optional user-scoped background helper.
- A separately installed worker or service if genuinely necessary.

Choose based on the existing packaged/unpackaged deployment model, permissions, security and resource impact.

Requirements:

- Local-network-only exposure by default.
- Explicit opt-in.
- Minimal CPU and memory usage.
- No impact on page navigation or rendering responsiveness.
- Controlled startup and shutdown.
- Clear network and firewall permission behavior.
- No silent elevation to administrator.
- No unnecessary public internet listener.
- Visible sync status and a simple disable control.

If synchronization while the main application is completely closed is desired, design a suitable separately managed background process.

Do not falsely claim that a terminated WinUI application can continue hosting its own in-process server.

## 8. Mobile platform constraints

Investigate the current Android and iOS requirements independently.

Address:

- Local-network permissions.
- Service discovery.
- Background execution restrictions.
- Secure credential storage.
- Battery efficiency.
- App suspension and termination.
- Offline data persistence.
- Reconnection and retries.

Treat automatic background synchronization as best effort where the OS limits execution.

Guarantee correctness of durable offline state, not uninterrupted network availability.

## 9. Offline-first persistence

Both devices must maintain an independent durable local database.

The synchronization protocol must tolerate:

- Disconnection during transfer.
- PC shutdown.
- Mobile app termination.
- Duplicate messages.
- Delayed messages.
- Network address changes.
- Different device clocks.
- Database migration.
- Reconnection after weeks offline.

Use transactions and recoverable synchronization checkpoints.

Preserve user state if a synchronization attempt fails.

Do not introduce a dependency on the PC for opening the mobile Mushaf or reading offline.

## 10. Integration with existing future milestones

**KH-1.0 — Khatma Planner**

Synchronize plan inputs and assignments as appropriate.

**KH-1.1 — Adaptive Recovery**

Recalculate remaining workload after confirmed progress is reconciled.

**KH-1.2 — Smart Reminders**

Prevent unintended duplicate reminders across devices. Distinguish device-local notification registrations from shared scheduling preferences.

**KH-1.3 — Exact Resume**

Use the shared stable source-anchor and confirmed-progress contracts.

**BM-1.0 — Physical Ribbon Bookmark**

Synchronize the primary bookmark's ayah identity, not its WinUI geometry or animation state.

The mobile companion must render its own visual bookmark using its own layout.

## 11. Content and privacy safeguards

The authoritative Qur’anic text and Tafsir distribution rights remain separate from synchronization.

Transfer user state only, except where carefully justified.

Never modify pinned publisher content.

Do not distribute protected artwork, fonts or source databases through the synchronization service without verified authorization.

No external account, telemetry or cloud dependency is required.

Provide clear controls to enable, pause, disable and unpair synchronization.

Document local data retention and recovery behavior.

## 12. Required future verification

Define test cases for:

- First-time pairing.
- Unauthorized device rejection.
- PC-to-phone synchronization.
- Phone-to-PC synchronization.
- Simultaneous offline changes.
- Conflict resolution.
- Duplicate operation delivery.
- Interrupted synchronization.
- Long offline periods.
- Lost and revoked device credentials.
- Network switching.
- PC sleep and restart.
- Mobile application background restrictions.
- Version and schema mismatches.
- Exact ayah resume.
- No duplicate Khatma progress.
- Bookmark deletion and restoration.
- No scripture integrity changes.
- Low resource consumption.
- No interference with existing UIA, pagination and rendering stability.

Include security threat modeling and protocol-version compatibility before implementation.

## 13. Documentation deliverables

After the current UI stabilization milestone closes, create:

`docs/roadmap/sync/LAN-1.0-LOCAL-MOBILE-SYNC.md`

Also prepare an architecture, device-pairing and threat-model document as part of the later authorized planning stage.

Update the central `docs/ROADMAP.md` to register:

**LAN-1.0 — Local-First Desktop/Mobile Synchronization — PLANNED**

Record dependencies on shared reading-position persistence, KH progress accounting and BM identity.

Do not add implementation code, listeners, firewall rules, scheduled tasks or network permissions during the documentation-only phase.

Preserve the existing repository state and the active stabilization milestone.

## Definition of success

The future system should let a person read Qur’an on Windows or a mobile device, continue safely when disconnected, and have confirmed reading progress and bookmarks synchronize in both directions when a trusted local connection becomes available.

**The reader must never lose progress, duplicate completed scripture or require internet access for ordinary reading.**

Document this future capability now; implementation requires separate authorization.

---

## Intake architecture and dependency outline — proposed, not implemented

This outline makes integration dependencies reviewable. It does not settle a wire
format, cryptographic suite, platform API, port, installer, framework or service
lifetime. Platform/security capability research has NOT been executed in this
intake; Android and iOS must be researched independently when planning is authorized.

### Shared identity and layer boundaries

Use the existing [AyahId/ReadingPosition models](../../../src/QuranReader.Core/Models.cs)
and [separate user-state persistence](../../../src/QuranReader.Data/UserStateStore.cs),
with the KH-1.3/common resume foundation and BM-1.0 identity contract described in
[the roadmap](../../ROADMAP.md). These are foundations to extend; a synchronization
journal, pairing store and confirmed-Khatma ledger are future work, not existing
services. Do not transfer a whole SQLite database or mirror UserState snapshots:
those would mix unrelated device preferences/selection with semantic user changes.

Proposed responsibilities:

- Domain/application: versioned source-anchor and entity identities, immutable
  change semantics, deterministic reconciliation and an explicit confirmed-range
  accounting contract shared with KH-1.0–KH-1.3. Calculations remain independent of
  WinUI; receiving data cannot create a completion confirmation.
- Data: evolve the existing Windows user store through reviewed migrations;
  atomically persist a user edit with its durable outgoing change and persist
  incoming changes with deduplication/checkpoints before acknowledgment. Do not
  create a competing Windows bookmark database. The future mobile client has its
  own independently durable store and equivalent semantic contracts.
- Optional Windows host: authenticated local transport and negotiation, bounded
  queues, pairing/revocation adapters and coordinator lifecycle. Keep network,
  hashing, database and reconciliation work off the reading UI thread; support
  cancellation, disable and controlled shutdown. Host packaging/lifetime remains
  undecided among the three approaches in section 7.
- App/mobile presentation: device-local layout, selection, theme, viewport and
  ribbon geometry. A received resume candidate must not unexpectedly move the
  active reading viewport or replace its selected Tafsir. Restore through the
  shared validated source-anchor contract at an appropriate explicit resume action.

A future anchor envelope should name its schema, authoritative content/edition
identity, AyahId and offset encoding. ReadingPosition.Offset is the existing local
viewport displacement; it must not be treated as a synchronized source cursor. The existing TextOffset uses UTF-16 source
positions validated at grapheme boundaries; other platforms must translate an
explicit wire representation rather than assume their string indices match.
Never normalize publisher Unicode to reconcile offsets. Reject or stage unknown
content/schema/invalid anchors; do not silently apply an in-ayah offset to another
edition. A disclosed whole-ayah fallback requires an explicitly reviewed compatible
identity/precision policy. Both devices source licensed, verified scripture
independently; this proposal grants no mobile content distribution rights.

### Entity-specific reconciliation candidates

These rules are proposed for later protocol review, not a finalized conflict algorithm.
All durable changes need stable device/entity/plan/session identities, globally
unique operation IDs, causal revisions and acknowledgments. Wall-clock time may
label history but must not be the sole conflict arbiter. Operation replay must be
idempotent even when messages arrive late, out of order or after a restart.

| Entity | Proposed reconciliation / integration contract |
| --- | --- |
| Last viewed position | Retain per-device anchors and concurrent resume candidates. An incomparable pair must not silently overwrite either or teleport a live viewport. Define a clear resume-choice policy. |
| Explicit primary ribbon | Separate identity/revision from last viewed anchor and ordinary bookmarks. Concurrent moves need a visible resolution policy; synchronize no marker coordinates or animation. BM follow/placement visual policy remains separately pending. |
| Saved ayah bookmarks | Causal additions and durable deletion tombstones. Candidate remove-wins semantics prevent stale offline records resurrecting a removal; explicit re-add/restore must be distinguishable from replay. Approve concurrency semantics before coding. |
| Confirmed Khatma ranges / completion records | Deduplicate explicit confirmation operations within stable plan/session identities, then account for their canonical source-range union exactly once. Never infer missing confirmations or credit overlap twice. Corrections/retractions need explicit audited references and a reviewed concurrent-correction policy; no silent rollback. |
| Khatma parameters / amendments | Preserve incompatible concurrent candidates and their causal history until explicit resolution. Assignments derive from identified plan/input revisions; do not merge incompatible day labels as if they were one plan. |
| Reading sessions | Stable session IDs and explicit state transitions; duplicate/end/retry delivery cannot create extra completion credit. A timer, open app or session record alone does not prove recitation. |
| Reminder preferences | Negotiate eligible shared intent; OS registrations, permissions and delivery receipts are device-local. Choose cross-device ownership/suppression and honest disconnected behavior with KH-1.2 before promising duplicate prevention. |

Commit reconciled progress/inputs before asking KH-1.1 to recover the schedule.
Recompute only when the relevant confirmed-range set or plan-input revision changes,
using the same deterministic planner model on a defined authority/version. Preserve
conflicting inputs without running an arbitrary candidate silently. An agreed plan
revision and explicit progress ledger precede LAN synchronization of those entities;
not all KH dashboard, reminder or BM artwork work must precede a narrower authorized
resume/bookmark sync slice. Unsupported entity/version handling must be explicit
and lossless, not silent field dropping.

### Durability, deletion and coordinator availability

A proposed per-device journal/outbox/inbox records operation identity, entity
revision/causal context, durable receipt and recoverable transfer checkpoint.
Acknowledgment means durable acceptance, not a successful socket write; distinguish
staged conflicts from applied changes. Retry after interrupted transfers and
transaction failure without losing pending edits or double-applying progress.
Use versioned snapshots/checkpoints for recovery only under documented causal
frontiers; preserve the operation audit needed to explain merges.

Define log/tombstone retention and compaction before implementation. A device
returning after weeks offline must not resurrect removed bookmarks after an
arbitrary TTL. Require acknowledgments from relevant paired peers, or a reviewed
snapshot/rebaseline protocol when history has been compacted. Device reinstall,
cloned database or restored backup must not reuse an operation sequence as new
changes. The exact revision/epoch and resync mechanism remains a design decision.

The PC coordinates exchanges while reachable; it is not the only durable copy.
Offline local reading, bookmarks, pending confirmations and persisted anchors must
survive PC sleep, stopped hosting and mobile suspension/termination. Do not promise
host availability after in-process app exit, instant background mobile reconnection
or duplicate-free cross-device notifications during a partition without a defined
policy. Losing the coordinator must not make scripture or local state unreadable.

### Pairing, discovery and threat-model work still required

Treat LAN reachability, discovery results, QR payloads and all remote input as
untrusted. Discovery advertises minimal connection metadata; it conveys no trust
or progress. The later pairing design must bind short-lived/one-use bootstrap
material and explicit user intent to the authenticated PC identity, resist code
replay/guessing, establish device-specific encrypted trust and store keys securely.
Manual endpoint entry must preserve identity verification rather than bypass it.
No long-lived unprotected QR credential or sensitive plaintext HTTP is acceptable.

The future threat model must cover unauthorized peers, spoofed discovery/PCs,
untrusted Wi-Fi/VPN routing, interception/replay, captured pairing material,
lost/stolen devices and backups, malformed/oversized operations, resource exhaustion,
corrupt or unauthorized progress and incompatible content/schema versions. It must
specify authorization per paired profile/device, payload limits, key lifecycle,
recovery, retention and revocation behavior before implementation. Local-interface
or firewall scope is an exposure restriction, not a substitute for authentication.
Revocation must deny later exchanges without deleting confirmed progress silently;
it cannot guarantee remote erasure from a disconnected lost phone. Define handling
of pending offline edits after revocation/re-pairing, rather than trusting them
because the device name or network address matches.

Investigate mobile-initiated mDNS/DNS-SD and a safe manual fallback. Reachable
Ethernet/Wi-Fi LAN operation is required; guest isolation, router/firewall policies,
VPNs and OS permissions can prevent it. Android and iOS permissions, discovery,
background execution, secure storage and battery behavior need current primary-source
research and real devices later. No framework, background-delivery guarantee,
automatic elevation, fixed port or firewall change is selected here.

### Later authorized planning and implementation gates

| Gate | Required evidence before passing |
| --- | --- |
| Shared state | KH-1.3/common stable resume, BM identity and KH confirmed-progress/plan contracts are documented/versioned for each supported entity; no view/bookmark-to-completion shortcut. |
| Architecture / protocol | Separate architecture, pairing and threat-model documents; final entity merge/deletion/revocation rules; schema/feature negotiation and supported-version compatibility matrix. |
| Platforms / hosting | Actual Windows deployment/lifetime/permissions decision and separate Android/iOS feasibility research; no mobile framework chosen prematurely; explicit opt-in, disable/pause/unpair and truthful offline/background behavior. |
| Implementation authorization | Separate user authorization for a bounded LAN slice, host exposure/lifetime and any mobile work. This intake creates none. |
| Verification | All section 12 cases, both synchronization directions and offline scenarios A–D; repeated/reordered/conflicting operations; transactional/crash/migration recovery; clocks/address changes; tombstone resurrection and explicit restore; incompatible versions/content; exact resume and no duplicate/missing/rollback progress. |
| Quality / release | Measurable CPU/memory/battery/queue/latency budgets set during planning; existing content/persistence/native UIA/pagination/memory gates preserved; actual platform permissions/sleep/background tests; security review and applicable visual/accessibility/rights gates completed. |

All LAN feature, protocol, security, mobile and performance verification is
**NOT EXECUTED**. This documentation does not inherit implementation PASS from
UI-1.2I.1, authorize network exposure or close existing external release gates.
The next recommended implementation foundation, only after separate authorization,
is shared stable position/persistence as an early KH-1.3 slice reused by BM and LAN;
confirmed progress/plan accounting follows before those entities are synchronized.
