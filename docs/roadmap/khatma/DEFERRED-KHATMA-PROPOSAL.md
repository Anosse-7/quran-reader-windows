# Deferred proposal intake: Intelligent Khatma System

Status: **PLANNED; implementation requires separate authorization.** The original intake predated UI-1.2I.1 engineering closure. Its handoff now records engineering verification at Release source 30d90c0 for the tested scenarios; user visual approval and external release gates remain pending. Current cross-feature status and LAN-1.0 dependencies are maintained in the coordinated roadmap. This metadata update does not alter the supplied specification or authorize implementation.

Cross-feature status, state distinctions and shared KH-1.3/BM-1.0 dependencies live in [the coordinated roadmap](../../ROADMAP.md). The text below is the original specification, preserved verbatim; future instructions inside it apply only after the entry gate opens.

Original attachment SHA-256: 4c21f41a368c25976b18379088a21a9c99566659cae0f9f62f8ef88ee81b0fdb

---

# CODEX — QUR’AN READER: FUTURE DEVELOPMENT ROADMAP
## Intelligent Khatma Planner, Adaptive Recovery, Smart Reading Reminders and Exact Resume

### Mission: Establish approved future goals after closure of the current UI phase

You are continuing the development of an existing Windows-native Qur’an Reader application.

The user and product designer have agreed on a major future capability: an intelligent, personalized Qur’an completion system that helps readers plan, maintain and finish a Khatma in a maximum target period of 40 days.

This system must integrate with the existing Qur’an Reader, not become a separate application.

**Important: This assignment is for future-goal definition, architecture planning and development preparation. It is not authorization to begin implementing the Khatma system immediately.**

The current UI development phase must be completed and verified first.

---

# 1. Repository and current development state

Expected repository:

`<project-root>`

Confirm the actual repository location, branch, Git HEAD and working-tree state.

Read:

- Root `AGENTS.md`.
- All existing repository-specific development and UI skills.
- Existing architecture documentation.
- Content integrity and licensing requirements.
- Current reading-position and bookmark services.
- User-state persistence architecture.
- Latest quality-gate reports.
- Current milestone and development roadmap.

Do not assume historical commits are still current.

Do not overwrite inherited work or interfere with another active Codex agent.

## Current task — mandatory completion gate

The latest known active assignment is:

**Phase UI-1.2I.1 — Tafsir Edge Drawer, Premium Controls and Flicker-Free Navigation.**

Its goals are:

1. Eliminate blank frames during adaptive leaf navigation.
2. Introduce smooth, reliable RTL page transitions.
3. Implement a right-edge Tafsir pull-tab and responsive drawer.
4. Display a proper no-selection state when no ayah is selected.
5. Improve button appearance and mouse interactions.
6. Preserve content and reading-state integrity.

Before preparing the future Khatma implementation roadmap:

- Confirm the phase's actual completion status.
- Read its final handoff and verification evidence.
- Confirm whether implementation is committed and tested.
- Identify remaining failures or unresolved acceptance requirements.
- Distinguish technical completion from user visual approval.

If the active development task is still in progress, **do not interrupt or modify its files**.

Record this specification as a deferred future proposal only if doing so is safe and does not conflict with concurrent development. Otherwise, return a handoff identifying the pending prerequisite.

Do not claim the active phase is completed simply because a build passes.

---

# 2. Product vision

Create an intelligent companion that helps the reader complete the entire Qur’an through a personalized, achievable schedule.

The system must consider:

- Available days.
- Workdays and days off.
- Reading time available each day.
- Different ayah lengths.
- Personal reading speed.
- Missed sessions.
- Incomplete daily assignments.
- Progress ahead of or behind schedule.
- Reading postponements.
- Changing availability.
- The remaining time before the Khatma deadline.
- Exact reading-position restoration.
- Helpful, nonintrusive reminders.

The goal is not merely to display progress statistics.

**The goal is to reduce the effort required to start, continue and complete a consistent Qur’an reading plan.**

The planner should remain respectful, calm, accurate, accessible and motivating.

---

# 3. Religious framing and the 40-day target

The feature is inspired in part by the narration concerning reciting the Qur’an in forty days, reported in Jami' at-Tirmidhi 2947.

Verify the relevant source, grading, wording and suitable presentation using authoritative references before incorporating religious explanations into the product.

Do not present a 40-day Khatma as a universal religious obligation.

The application may offer 40 days as the central completion goal and allow shorter goals where feasible.

If the available reading capacity cannot support the selected deadline, the application must say so accurately.

Offer adjustments to daily availability or a longer completion period if the reader wishes.

Never promise that every user can complete the Qur’an within 40 days regardless of their available time and reading speed.

---

# 4. Future milestone KH-1.0 — Intelligent Khatma Planner

## 4.1 Initial configuration

Design a setup experience allowing the user to choose:

- Khatma start date.
- Target completion period, up to 40 days.
- Regular workdays.
- Regular days off work.
- Reading time on workdays.
- Reading time on days off.
- Specific days on which no reading is planned.
- Optional date-specific availability changes.
- Preferred daily reading sessions.
- Reading-speed calibration method.

Support different reading budgets for different days.

Distinguish a day off work with extra free time from a day deliberately reserved without reading.

The initial setup should remain simple; advanced scheduling can be optional.

## 4.2 Reading workload calculation

A fundamental requirement is that **not all ayat have equal reading length**.

Do not calculate the entire plan by dividing 6,236 ayat evenly across the selected days.

Use the existing verified Hafs scripture data to estimate workload.

Consider:

- Word counts.
- Arabic script characteristics where relevant.
- Estimated reading time.
- Personal reading pace.
- Actual reading-session results.
- Particularly long ayat.

Use verified source identities and do not alter Qur’anic text.

Investigate appropriate treatment of words containing Arabic diacritics and Uthmani-specific marks.

Avoid treating Unicode code-point count as a direct measure of recitation effort.

A basic estimation model may begin with:

`Estimated reading minutes = Ayah word count / Calibrated reading words per minute`

However, support more accurate calibration when actual reading evidence becomes available.

Do not claim that word count alone perfectly predicts recitation time.

## 4.3 Personal pace calibration

Design a calibration process in which the reader completes a suitable Qur’anic sample while the system measures active reading time.

Use multiple samples or successive actual sessions to improve estimates.

Separate reading speed from idle time.

Do not assume a user was reading continuously merely because the application remained open.

Allow manual pace entry or recalibration.

Preserve the confidence or uncertainty of estimates.

## 4.4 Daily assignment generation

Generate chronological reading assignments from the verified Qur’anic source.

Each assignment should contain:

- Starting surah and ayah.
- Ending surah and ayah.
- Estimated ayah count.
- Estimated reading workload.
- Estimated time.
- Daily available time.
- Expected completion contribution.

Distribute workload proportionally to available reading capacity, not equally by ayah count.

Respect full ayah boundaries where practical.

For unusually long ayat, consider support for a precise in-ayah resume checkpoint, but never split or alter the authoritative content itself.

Ensure assignments cover the entire Qur’an exactly once in the intended reading order.

Never omit, duplicate or silently skip scripture.

## 4.5 Feasibility calculation

Before activating a plan, compare:

- Estimated total reading workload.
- Total available reading time.
- Personal reading pace.
- Selected completion deadline.

Classify plans as feasible, uncertain or infeasible using documented criteria.

When the plan is unrealistic, provide actionable alternatives instead of an inaccurate promise.

---

# 5. Future milestone KH-1.1 — Adaptive Recovery Engine

The user wants the planner to respond intelligently when daily or weekly progress falls behind.

This is a core feature, not an optional afterthought.

## 5.1 Track unfinished workload

At every planning checkpoint, determine:

- Expected progress.
- Confirmed completed progress.
- Remaining unread scripture.
- Current schedule deviation.
- Remaining reading capacity.
- Time left before the deadline.

Track unfinished scripture content, not only missing minutes.

## 5.2 Carry missed assignments forward

If the reader misses a session or completes less than planned, preserve the unfinished reading assignment.

Redistribute it across future available days.

Do not discard missed ayat or mark them as completed.

If a week ends with unfinished reading, the system should carry that workload into subsequent weeks.

## 5.3 Recovery strategies

Design three user-selectable strategies.

**Balanced Recovery — Default**

Redistribute outstanding workload across all remaining available days, prioritizing days with greater reading capacity.

**Next-Week Recovery**

Prioritize recovering missed reading during the following week without exceeding the user's selected capacity limits.

Any backlog that cannot reasonably fit should be distributed farther into the remaining schedule.

**Gentle Recovery**

Limit additional daily reading burden and transparently report when the original deadline may no longer be achievable.

## 5.4 Preserve the deadline when feasible

Recalculate the completion forecast after each meaningful change.

The system should attempt to preserve the original 40-day target when sufficient capacity remains.

Do not overload every remaining day merely to maintain an impossible target.

Never silently extend the deadline.

When necessary, explain the options and obtain the user's decision.

## 5.5 Handle progress ahead of schedule

The recovery engine must also respond when the reader completes more than planned.

Allow future assignments to become lighter or optionally bring the completion date forward.

Avoid repeatedly assigning already completed ayat.

## 5.6 Replanning reliability

Create a deterministic, testable scheduling algorithm.

The same inputs should produce the same schedule.

Ensure that replanning:

- Preserves confirmed completion.
- Preserves reading order.
- Does not duplicate assignments.
- Does not create missing content ranges.
- Respects unavailable days.
- Handles changing daily capacity.
- Keeps an auditable history of changes.

---

# 6. Future milestone KH-1.2 — Smart Reading Reminder Engine

Design an integrated Windows notification and session-scheduling system.

The user wants reminders before reading time, at the scheduled time, and when postponing an unfinished session.

## 6.1 Upcoming-session reminders

Support user-configurable advance reminders, such as:

- 5 minutes before.
- 10 minutes before.
- 15 minutes before.
- Custom advance interval.

Allow the user to disable advance reminders.

The reminder should identify the planned reading session and offer a straightforward path to resume.

## 6.2 Scheduled-time notification

At the scheduled time, display an actionable Windows notification.

Possible actions:

- Start Reading.
- Read Later.
- Dismiss.

The notification should be compact, respectful and recognizable.

Avoid notification spam.

Do not repeatedly notify the user after a session is already completed or explicitly dismissed.

## 6.3 Optional automatic application launch

The user proposes automatically opening Qur’an Reader when a scheduled reading session arrives.

Design this as a separately enabled, explicit opt-in feature.

Investigate the correct supported Windows mechanism for the current application packaging and deployment model.

Potential options may include Windows notification activation, a user-scoped scheduled task, or another officially supported mechanism.

Verify the actual capabilities and limitations from current Microsoft documentation.

Do not assume a scheduled notification alone can automatically launch an application without user interaction.

If automatic launching is supported and enabled:

- Launch the existing application instance or activate it safely.
- Do not create unnecessary duplicate processes.
- Restore the intended reading session.
- Avoid stealing focus unexpectedly where possible.
- Respect system sleep, lock state and user preferences.
- Avoid requiring administrator privileges.
- Provide a straightforward way to disable or unregister the feature.

Never promise automatic launching when the computer is shut down.

## 6.4 Read Later — Snooze

Support postponing a scheduled reading session.

Suggested choices:

- 15 minutes.
- 30 minutes.
- One hour.
- A specific time.
- A specific date and time.

The user must be able to enter their preferred new reading time.

Validate past dates, unavailable periods and conflicting scheduled reminders.

Reschedule the correct session.

Avoid leaving duplicate outdated notifications.

## 6.5 Planner integration

When the reader postpones a session, distinguish reminder scheduling from actual reading progress.

Postponing within the same day may only require changing the reminder time.

Postponing beyond the available reading window may require replanning unfinished workload.

If the selected new time cannot accommodate the full assignment, explain that accurately.

Never mark a postponed assignment as complete.

## 6.6 Closing the application early

When the reader closes the application during an active and unfinished planned session, offer an optional exit experience:

- Save and Close.
- Remind Me Later.
- Continue Reading.

If Read Later is selected, allow a new time to be specified.

Respect the user's choice to close without reminders.

Do not obstruct normal system shutdown, crash recovery or forced application termination.

If an exit prompt is technically inappropriate in a particular shutdown context, persist the session safely and use only previously authorized reminder behavior.

## 6.7 Notification reliability and privacy

Investigate:

- Application-running and application-closed delivery.
- Notification activation after restart.
- Missed notification behavior.
- Sleep and lock state.
- Windows Do Not Disturb.
- Time-zone changes.
- Daylight saving transitions.
- Cancellation and updates.
- Scheduled-task lifecycle.
- Notification permissions.
- Duplicate notification prevention.

All notifications and automatic launching must be optional and user-controllable.

No account or cloud connection should be required for the core feature.

---

# 7. Future milestone KH-1.3 — Exact Resume and Progress Monitor

The reader should be able to resume without searching for the previous page or ayah.

## 7.1 Exact position preservation

Design stable reading-position storage using the authoritative Qur’anic content identity.

Store, where appropriate:

- Surah identity.
- Ayah identity.
- Canonical page reference.
- Stable content cursor or offset.
- Reading mode.
- Selected text or ayah state.
- Scroll position.
- Font scale.
- Reading zoom.
- Theme preference.

In Adaptive Continuous Book mode, do not rely solely on transient dynamic leaf numbers.

Recompose the appropriate leaf from the saved stable scripture anchor.

Restore positioning without missing or repeating scripture.

## 7.2 Separate reading position from completion

This distinction is mandatory.

Maintain separate states for:

**Reading Position**

Where the user was last viewing or interacting with the Qur’an.

**Confirmed Completion Checkpoint**

The scripture range the user has actually reported completing.

Clicking an ayah, scrolling past it, opening its Tafsir or leaving the application open must not automatically count that ayah as completed.

Provide a convenient, explicit way to confirm reading progress.

## 7.3 Session restoration

When the reader starts a scheduled session:

1. Locate the saved reading checkpoint.
2. Open the appropriate Qur’an reading mode.
3. Navigate to the exact ayah or stable content position.
4. Restore compatible reading preferences.
5. Display the active Khatma assignment.
6. Allow reading to continue immediately.

Where precise sub-ayah restoration is supported, preserve it safely without modifying or truncating scripture.

Otherwise, resume at the correct complete ayah and disclose that precision limit.

## 7.4 Progress dashboard

Create a dedicated **My Khatma — ختمتي** workspace.

Show:

- Current Khatma goal.
- Start and target completion dates.
- Current day of the plan.
- Overall confirmed completion percentage.
- Today's assignment.
- Today's confirmed progress.
- Remaining estimated reading time.
- Current reading checkpoint.
- Outstanding backlog.
- Upcoming sessions.
- Forecast completion date.
- Recovery status.

Include a useful overview of past completed Khatmas.

Make progress understandable without requiring the user to interpret technical statistics.

## 7.5 Progress statuses

Use calm, respectful labels:

- On Schedule.
- Ahead of Schedule.
- Catch-Up Available.
- Deadline Adjustment Recommended.
- Completed.

Avoid guilt-inducing language, penalties or excessive notifications.

---

# 8. Unified technical architecture

Design the four milestones as one connected domain system with independently testable components.

Evaluate modules such as:

- `KhatmaPlan`
- `ReadingAvailability`
- `ReadingPaceProfile`
- `AyahWorkloadEstimator`
- `AssignmentGenerator`
- `ReadingSession`
- `ConfirmedReadingProgress`
- `RecoveryScheduler`
- `ReminderSchedule`
- `NotificationCoordinator`
- `ReadingResumeService`

These are conceptual module names, not required literal class names.

Reuse existing project architecture and naming conventions.

## Persistence

Prefer extending the existing local SQLite user-state system with versioned migrations.

Do not modify the pinned read-only Qur’anic content database.

Never mix user progress with authoritative publisher scripture data.

Design durable recovery from application crashes, incomplete writes and interrupted sessions.

Use appropriate transactions and data-integrity constraints.

## Algorithms

Produce clear, testable pseudocode or mathematical specifications for:

- Estimating ayah workload.
- Calibrating reading speed.
- Checking deadline feasibility.
- Allocating daily assignments.
- Carrying missed assignments forward.
- Selecting recovery strategies.
- Updating completion forecasts.
- Confirming reading progress.
- Restoring the exact reading anchor.

Provide deterministic test cases and expected outcomes.

## Performance

Keep planning calculations outside performance-critical scripture rendering paths.

Do not recalculate the entire Khatma schedule on every scroll, hover or mouse movement.

Avoid disrupting the smooth navigation work from Phase UI-1.2I.1.

---

# 9. User experience design

Design the Khatma features to complement the existing Mushaf, not overwhelm it.

Introduce a dedicated **My Khatma** entry in the application's navigation or command overflow.

Avoid cluttering the reading toolbar with many new buttons.

Use the existing light, dark and sepia design system.

Support Arabic RTL presentation and accessible interface labeling.

The planner should remain usable by mouse, keyboard and assistive technology.

Prepare design specifications for:

1. First-time Khatma setup.
2. Reading-speed calibration.
3. Generated 40-day plan.
4. Daily reading dashboard.
5. Weekly progress summary.
6. Behind-schedule recovery.
7. Reading reminders.
8. Read Later time selection.
9. Early-close session prompt.
10. Exact reading restoration.
11. Completed Khatma summary.

These are future design artifacts. Do not replace the production UI during this planning phase.

---

# 10. Quality and safety requirements

Update the project's permanent development requirements to include the future Khatma capability.

Create an appropriate requirement family, such as:

**KHP — Khatma Planning and Progress Quality Goals.**

The requirements must cover:

- Quranic content integrity.
- Complete assignment coverage.
- Correct chronological reading order.
- Feasible scheduling.
- Accurate completed-work accounting.
- No duplicated progress.
- Robust rescheduling.
- Correct deadline forecasting.
- Reliable checkpoint persistence.
- Respectful notification behavior.
- Opt-in automatic launching.
- User privacy.
- Accessibility.
- Responsive desktop UI.
- Performance and stability.

Document acceptance tests before implementation.

The implementation must not be considered complete merely because its scheduling arithmetic is correct.

It must demonstrate reliable integration with the actual reader.

---

# 11. Development sequence and approval gates

Prepare a phased implementation roadmap.

Recommended order:

### KH-1.0 — Planner Foundation

Implement the domain model, reading workload estimator, personal pace calibration, daily availability and basic Khatma schedule generation.

### KH-1.1 — Adaptive Recovery

Implement progress reconciliation, backlog transfer, weekly replanning and deadline-feasibility forecasting.

### KH-1.2 — Smart Reminder Integration

Implement user-approved Windows reminders, session actions, snooze behavior, close-session handling and optional auto-launch.

### KH-1.3 — Exact Resume and Dashboard

Integrate session restoration, confirmed completion checkpoints, reading-position recovery, dashboard and Khatma history.

Adjust the technical order if genuine dependencies require a shared persistence or anchor foundation earlier.

Explain any changes.

For every phase, establish:

- Scope.
- Dependencies.
- Acceptance requirements.
- Test strategy.
- User-visible outcomes.
- Risks.
- Rollback plan.
- Estimated implementation complexity.
- Explicit approval gate.

Do not automatically begin the next phase because the previous implementation builds successfully.

---

# 12. Deliverables for this planning task

After confirming the current UI phase is safely closed, create an organized future-goals package under an appropriate repository documentation directory, for example:

`docs/roadmap/khatma/`

Include:

- `KHATMA-PRODUCT-VISION.md`
- `KHATMA-USER-REQUIREMENTS.md`
- `KHATMA-DOMAIN-ARCHITECTURE.md`
- `KHATMA-WORKLOAD-AND-CALIBRATION.md`
- `KHATMA-ADAPTIVE-RECOVERY.md`
- `KHATMA-NOTIFICATIONS-AND-AUTO-LAUNCH.md`
- `KHATMA-EXACT-RESUME.md`
- `KHATMA-UX-DESIGN-GOALS.md`
- `KHATMA-QUALITY-AND-ACCEPTANCE.md`
- `KHATMA-IMPLEMENTATION-ROADMAP.md`
- `KHATMA-OPEN-QUESTIONS.md`

Update the existing central project roadmap to reference these deferred future milestones.

Update `AGENTS.md` only where necessary to establish durable, nonredundant engineering rules.

If the repository uses agentic skills, determine which Khatma-specific development guidance belongs in those skills.

Do not generate redundant rules or scatter conflicting instructions across files.

Document any design decisions requiring user approval.

Do not introduce runtime dependencies or implementation code in this roadmap-only task.

---

# 13. Verification and Git discipline

Verify:

- Existing work remains unchanged.
- No authoritative scripture data was modified.
- No user-state databases were overwritten.
- No active development agent's files were disrupted.
- The roadmap is internally consistent.
- Existing requirements correctly reference the new goals.
- Proposed implementation phases have explicit entry and exit conditions.
- Religious claims and Windows platform capabilities have appropriate verification requirements.
- Future goals are clearly labeled PLANNED, not IMPLEMENTED.

Record the current branch, source revision, changed files and Git status.

Create a scoped documentation commit only if safe under the repository's active collaboration and version-control rules.

Preserve inherited uncommitted work.

---

# 14. Final report

Produce a concise but comprehensive handoff containing:

**A. Current project status**

Confirm the state of Phase UI-1.2I.1 and whether future planning was permitted to proceed.

**B. Future feature scope**

Summarize the agreed Khatma Planner, recovery, reminder and exact-resume capabilities.

**C. Architecture**

Explain how the new modules will integrate with the existing reader.

**D. Roadmap**

Show the recommended implementation order and dependencies.

**E. Quality requirements**

Identify the measurable conditions future implementations must satisfy.

**F. Documentation**

Provide exact paths to the created roadmap and specification files.

**G. Open decisions**

Identify only questions that genuinely require user approval.

**H. Next task**

Recommend the first KH implementation milestone, but do not begin it without authorization.

---

# Definition of success

This planning assignment is complete when all recently agreed Khatma ideas have been transformed into a technically coherent, testable and maintainable future development roadmap.

The active UI stabilization work must remain protected.

The existing Qur’anic text, Tafsir services, reader navigation and persistence foundation must remain intact.

The Khatma system must be designed around the user's real schedule, actual reading progress, accurate ayah workload estimates and easy return to reading.

**Do not implement these new capabilities yet. Establish their goals and quality rules now, so future Codex sessions can implement them safely, one approved phase at a time.**