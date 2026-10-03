# isTargetSleeping improvement plan

**Status: PLAN — NOT IMPLEMENTED.** Written on October 3, 2026.

**Baseline: 1.5.2, published on October 3, 2026.** Its [service and task validation](backend-validation-1.5.2.md#en) is documented separately. This plan's functional review starts from the 1.5.1 code and the capabilities retained by 1.5.2. Appearing in this plan does not mean a proposal is implemented. See the [1.5.2 release](https://github.com/Tykillita/isTargetSleeping/releases/tag/v1.5.2).

[Plan equivalente en español](improvements-plan.es.md)

## Quick read

The proposed direction is to turn isTargetSleeping into a resource manager for local AI: control several instances, preserve available memory for what you are doing, and explain every decision. Its existing controls, privacy and personality remain the product's foundation.

The plan contains **46 improvements**, organized by impact, priority, effort, risk and dependencies. Three bets concentrate the major change:

1. **Memory budgets with a simulator:** decide what can sleep to preserve RAM/VRAM headroom while observing the cost of loading it again.
2. **Multiple-instance manager:** manage Ollama, several llama.cpp instances and LM Studio separately, using the actual capabilities of each version.
3. **Temporary client leases:** let a local integration declare that it still needs a model, with verifiable identity and expiry.

The first delivery should strengthen states, operations and identity. The second can introduce modern engines and multiple instances. Budget management is tested in observation mode first. Leases and community pets remain later pilots.

**Immediate isolation priority:** current task shutdown calls a global search for `ollama serve` servers. Idea 04 must bound that termination before enabling multiple-instance control. This document proposes the fix; it does not implement it.

**Expected result:** fewer incorrect operations, less sustained pressure and an understandable experience. Performance targets are hypotheses that require measurement; this plan does not promise speed, FPS or energy-saving multipliers.

## First five suggested actions

1. **Close the 1.5.2 baseline:** retain build and test results, engine versions and hardware; measure a normal session and one under pressure. This separates actual improvements from changing workloads.
2. **Specify the coordinator and states:** describe who may request each operation, what may be cancelled and which intent takes precedence. Include the global task-termination debt and build isolation tests before connecting every action.
3. **Create capabilities and instance-identity contracts:** adapt Ollama first and add a second test llama.cpp instance. Do not expand providers before demonstrating isolation.
4. **Prototype budgets and the simulator in memory:** replay reference records, produce explainable decisions and verify that simulation performs zero actions.
5. **Deliver a multiple-instance and observation pilot:** collect reloads, pressure and the app's own resource use locally; choose the next phase from the results.

## 1. What exists and what would change

### Current features to preserve

- Unload idle models, put them to sleep manually and control Ollama from the panel, tray, shortcuts, CLI and links.
- Detect app, service, scheduled-task and manual installations; watchdog and game mode that remember manual decisions.
- Elevated agent, configurable cleanup, identified requests, full/partial outcomes and follow-up observations.
- Application summary and process window with filters, grouping, protections and termination confirmation.
- RAM, commit and GPU metrics; local history, approximate client attribution and model management.
- Verified updates with rollback, English/Spanish UI and optional pets with native animations.

Cleanup in 1.5.x retains its configured areas and rules. Future profiles are chosen explicitly. Migrating preferences does not enable another strategy or automatically turn a detected installation into a managed instance.

### Code evidence guiding the plan

| Area | Observed situation | Design consequence |
|---|---|---|
| Engine contracts | [OllamaController](../src/IsTargetSleeping/Core/OllamaController.cs) and [IEngine](../src/IsTargetSleeping/Core/Engines/IEngine.cs) use different paths. | Unify operations and state while retaining specific capabilities. |
| llama.cpp | [LlamaCppEngine](../src/IsTargetSleeping/Core/Engines/LlamaCppEngine.cs) selects the lowest PID and remembers one launch command. | Instance identity and several complete configurations are needed. |
| Task shutdown | In [Backend](../src/IsTargetSleeping/Core/Backend.cs), `Switch.Stop(Task)` calls `KillServers()` after `ScheduledTasks.Stop`. That function iterates `Detector.ServerPids()`, which globally finds `ollama serve` processes without filtering by task, endpoint or ownership. | One task may affect another reachable instance. Bounding the target and its descendants is P0 debt in idea 04, before the multiple-instance manager. |
| LM Studio | [LmStudioEngine](../src/IsTargetSleeping/Core/Engines/LmStudioEngine.cs) uses fixed port 1234 and API v0. | Negotiate port, API and authentication; graduate support using actual evidence. |
| GPU | [Gpu](../src/IsTargetSleeping/Core/Gpu.cs) chooses the adapter with the most dedicated VRAM. | Measure several adapters and avoid counting shared memory twice. |
| Clients | [Clients](../src/IsTargetSleeping/Core/Clients.cs) correlates connections by port and a ten-second window, grouping by name. | Separate instance, identity and attribution confidence. |
| History | [Stats](../src/IsTargetSleeping/Core/Stats.cs) retains ninety days of events; thirty-minute samples live in RAM. | Add summarized persistent series with disk limits. |
| UI | [PanelWindow](../src/IsTargetSleeping/UI/PanelWindow.cs) cannot be resized; [Theme](../src/IsTargetSleeping/UI/Theme.cs) fixes black glass. | Retain the flyout and offer an expanded view and accessible alternatives. |
| Clocks | Controllers, clients, processes, tray and pets sample using separate clocks. | Share captures where safe and measure the app's own resource use. |
| Persistence | Settings/history use atomic replacement; some read/write failures are silently swallowed. | Preserve atomicity and add recovery, a schema and diagnostics. |

This review identifies design needs; it is not an exhaustive security audit or a new measurement of every feature.

## 2. The three major bets

### A. Budget manager

**Usage example:** before opening a game, choose “keep 6 GiB of RAM available.” The panel shows two idle models, a pinned model and a protected application. It proposes putting the least important model to sleep, shows an estimate and explains why it leaves the others alone. You can simulate, accept that action or enable a reversible profile.

**Internally:** an evaluator receives available RAM, commit, VRAM per adapter, models, activity and leases. It produces an explainable `ActionPlan` with scope, observations, estimates, conditions and expiry. The coordinator revalidates identity, activity and protections before execution. Exits and memory evolution are checked; pending actions remain visible.

**Decision criterion:** compare recovered headroom and pressure duration with reloads, latency and paging. Selection may favor unloading a genuinely idle model over repeating trims with no sustained effect. Advanced modes retain their configured operations and protections.

**Limits:** a budget is a goal; other applications may consume that headroom immediately. A model file's size alone cannot predict context, buffers or runtime consumption. The app does not change other clients' request options to enforce a budget.

**Continuation gate:** demonstrate less time under sustained pressure in reference workloads without increasing reloads or latency beyond the agreed cost budget. If the simulator has insufficient data, it must explain that and skip the action.

### B. Multiple-instance manager

**Usage example:** run Ollama for Obsidian, llama.cpp on 8080 for programming and another llama.cpp on 8081 for embeddings. Each card has its own identity, models, consumption and policy. While gaming, stop only the programming engine; the others remain according to your profile.

**Internally:** `EngineInstance` identifies provider, version, endpoint, backend, processes and ownership. `EngineCapabilities` declares supported operations and activity/memory signal quality. Each instance maintains its own state and intent; the coordinator prevents conflicts. The engine reports an API unload or native sleep when available, with a tested fallback for older versions. Before enabling this control, task shutdown stops using the global list as its target set: the plan requires identifying and revalidating the instance, then acting only on its processes and descendants.

LM Studio recommends its [v1 API](https://lmstudio.ai/docs/developer/rest). The current [llama.cpp server](https://github.com/ggml-org/llama.cpp/tree/master/tools/server) documents routing, load/unload operations, events and idle sleep. Exact availability must be negotiated and verified for supported versions rather than assumed on older installations.

**Limits:** a responding endpoint does not prove that its process belongs to this instance. Detected backends may belong to another user or be externally managed. Remote control would require a dedicated security project and is outside the first phases.

**Continuation gate:** three simultaneous instances, isolated operations, recovery of a failed instance and no actions on unrelated processes. Add a case with two Ollama tasks and one manual server: stopping task A preserves task B's and the manual server's identities and responses; repeat with A already closed and with a failure to stop it. If membership cannot be verified, do not expand termination to the global list. The compatibility catalog declares what was tested and which capabilities are unknown.

### C. Temporary client leases

**Usage example:** an analysis script requests that its model stay available for fifteen minutes and renews a lease while working. The app shows “reserved by analysis.py until 14:30.” When the script exits or the lease expires, the regular policy returns. The user can revoke it.

**Internally:** a local per-user channel acquires, renews and cancels leases with verifiable identity, TTL and client limits. A channel with a per-user ACL, such as named pipes, avoids opening a server reachable across the network. A lease expresses a need for use; it does not grant administrative privileges. Script, Stream Deck or MCP tools can later use the same contract.

**Limits:** it does not require routing all inference through the app or recording prompts/responses. A connected process does not get an unlimited lease. Behavior under critical pressure with an active lease must be explicit and visible.

**Continuation gate:** a crashed client leaves no permanent lease; forged requests are rejected; identity, quotas and expiry are tested. Continue the pilot only if real integrations prevent unnecessary unloads at an acceptable resource cost.

## 3. Classification and estimates

- **Priority:** P0 = foundation; P1 = next product evolution; P2 = expansion; P3 = later pilot.
- **Impact:** High = changes reliability or frequent use; Medium = improves specific uses or understanding.
- **Effort:** M = several days; L = several weeks; XL = a cross-cutting project. Includes validation; recalibrate after prototyping.
- **Risk:** Low, Medium or High based on scope over processes, privileges, compatibility and persistence. This is not the probability of an already demonstrated failure.
- **Dependencies:** backlog numbers. These express logical order, not a requirement to deliver every earlier idea.

## 4. Backlog of 46 improvements

### Reliability and architecture

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 01 | Common coordinator for panel, CLI, links, game mode, watchdog, downloads and agent; IDs, states, safe cancellation and limits. | High / P0 | XL / High | Foundation. A thousand generated concurrent-command sequences without incompatible executions or lost intent. |
| 02 | Explicit intent/state: desired, observed, starting, loading, sleeping, inaccessible and failed. | High / P0 | L / High | 01. Every transition identifies its requester and preserves the latest manual decision. |
| 03 | Watchdog aware of startup/loading: phase deadlines and actual health before restarting. | High / P0 | L / High | 02. Slow loading and a busy API cause no false restarts in supported scenarios. |
| 04 | Backend identity and shutdown: executable, PID/creation, service/task, endpoint and ownership; replace `Switch.Stop(Task)` global scope with that instance's targets. | High / P0 | L / High | 01–02. Two tasks and one manual server: stopping A, even already closed or failing, preserves B's and the manual server's identities/responses. Unknown membership does not expand scope. |
| 05 | Versioned settings/history schema, migrations, valid backup and corruption notice. | High / P0 | M / Medium | Old fixtures; interrupted writes preserve the last valid file. |
| 06 | Exportable local diagnostics: actions, codes and versions, previewed with paths/users redacted. | Medium / P1 | M / Medium | 01, 05. Never includes prompts; tested redaction and explicit export. |

### Memory and models

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 07 | Independent budgets for available RAM, commit and VRAM per adapter. | High / P1 | XL / High | 02, 19. Explainable goals; do not present them as guaranteed physical limits. |
| 08 | Observation and simulator: show the actions a policy would have taken. | High / P1 | L / Low | 01, 07, 30. Simulation performs zero actions; results reproducible from a local record. |
| 09 | Legacy, keep-models-warm, balanced and memory-headroom profiles; load/unload hysteresis and cooldown. | High / P1 | L / High | 07–08. Imports retain previous behavior until another profile is selected. |
| 10 | Evaluate sustained effect at 30 s/2 min/5 min and reloads; avoid repetition without demonstrated benefit. | High / P1 | L / Medium | 30 and measurement contracts. Negative changes and external workload remain visible; no automatic causal attribution. |
| 11 | Per-model policy: pinning, idle timeout, priority and acceptable reload cost; respect activity and leases. | High / P1 | L / High | 02, 15. Pinned model protected from normal rules; critical-pressure behavior explicit. Integrate leases when 36 exists. |
| 12 | Preload advisor: observed/estimated memory, context, headroom and expected load time. | Medium / P2 | L / Medium | 19, 30. Separate estimation from measurement and state uncertainty. |

Trimming a working set may remove resident pages that are used again later; page faults and subsequent pressure matter when evaluating results. [Microsoft: Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set).

Ollama exposes active models, size and VRAM, and supports preloading and `keep_alive`. Other clients' requests can override configured residency, so the design must respect that limit. [Active models](https://docs.ollama.com/api/ps), [Ollama FAQ](https://docs.ollama.com/faq).

### Engines and instances

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 13 | Bring Ollama into the common engine contract while retaining its specific features. | High / P0 | XL / High | 01–02. Parity for load, unload, states and previous rules. |
| 14 | Multiple-instance manager with name, endpoint, identity, models and policy per instance. | High / P1 | XL / High | 04, 13. Three simultaneous instances; stable selection after a process restart. |
| 15 | Version-specific capabilities: unload model, stop server, cancel, measure activity, authentication and events. | High / P0 | L / Medium | 13. An unknown capability is unavailable, not improvised using a destructive action. |
| 16 | LM Studio v1 with v0 fallback, configurable port, protected secrets and real tests. | High / P1 | L / High | 15. Version/action matrix; remove experimental only for tested behavior. |
| 17 | Put llama.cpp to sleep through native API/TTL and recognize `sleeping`; stop/restart fallback for older versions. | High / P1 | L / High | 15. Sleep/wake retains arguments; health probes do not wake the model. |
| 18 | Complete launch configuration: executable, arguments, directory, environment and permissions; preview changes. | Medium / P2 | L / High | 04, 14. Equivalent relaunch with spaces/Unicode; secrets absent from logs. |
| 19 | Multiple GPU adapters, shared/dedicated memory and per-adapter attribution. | High / P1 | L / Medium | Valid DXGI/PDH sources. Two GPUs and no GPU tested; no shared-memory double counting. |

### UI and everyday use

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 20 | Compact flyout plus an expanded resizable window for resources, models, instances and history. | High / P1 | L / Medium | 14, 30. Shared state; dimensions restored within the monitor. |
| 21 | Simple/advanced settings with search and explanations of each rule's effects. | High / P1 | L / Low | Preference catalog. Find options by name or effect; English/Spanish parity. |
| 22 | Guided first run: engine, strategy and agent/UAC scope; reversible connection test. | High / P1 | M / Low | 15, 21. Query models without an agent; user understands permissions before activation. |
| 23 | Command palette and configurable shortcuts, visible conflicts and preserved current links. | Medium / P2 | M / Medium | 01. Keyboard actions; occupied hotkey explained. |
| 24 | Accessibility: AutomationProperties, spoken states, focus, contrast and text scaling. | High / P0 | L / Medium | UI inventory. Complete keyboard/Narrator flow, 100–200% scaling and high contrast. |
| 25 | Optional light/system themes; retained black glass and accessible opaque option. | Medium / P2 | L / Medium | 24. Readable in every theme; states recognizable without color alone. |

Retaining WPF and useful native components lets the effort focus on behavior and experience. Windows recommends programmatic access, keyboard navigation and contrast as accessibility pillars. [Windows accessibility](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility).

### Processes and measurement

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 26 | Application trends, large changes and load/unload markers; explain what coincided with a spike. | High / P1 | L / Medium | 30. Optional/bounded history; call it coincidence when causality is unproven. |
| 27 | Persistent protections by executable/user, with a visible reason. | High / P1 | M / Medium | Existing identities. Same path with another owner does not inherit protection accidentally. |
| 28 | Normal closing of windowed applications as a separate action from forced termination. | Medium / P2 | L / High | 01, 04. Confirm exit; do not guarantee preserved work or escalate automatically to kill. |
| 29 | Client attribution by full endpoint and PID/creation, with high/medium/unknown confidence. | High / P1 | L / Medium | 04, 14. Same names do not merge; short requests get no false certainty. |
| 30 | Summarized persistent 1 h/24 h/7 d time series with retention and disk limits. | High / P1 | L / Medium | 05. Restart preserves charts; compaction and limits tested. |
| 31 | Comparable reports: sustained headroom, pressure, reloads and cleanup cost; export CSV/JSON. | Medium / P2 | L / Medium | 10, 30. Do not count the same repeatedly freed memory as guaranteed accumulated savings. |

### Games, profiles and integrations

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 32 | Per-game/app profiles: engines to stop/retain, budget and preview. | High / P1 | L / High | 01, 07, 14. A game using AI can retain its engine without disabling all game mode. |
| 33 | Intent/generation-based restoration: defer, cancel or skip according to later decisions. | High / P0 | L / High | 02; expanded with 32. Switching games and manual shutdown do not restore incorrectly. |
| 34 | Pause automation for 15/30/60 minutes or until restart, stating what stays enabled. | High / P1 | M / Medium | 01, 21. Manual actions remain available; expiry observable. |
| 35 | Battery/plugged-in, locked-session and suspend/resume profiles; revalidate state on return. | Medium / P2 | L / Medium | 02, 37. Real sleep/resume tested; resuming alone does not restore processes. |
| 36 | Local client leases, identity, expiry, cancellation and later integrations. | High / P3 | XL / High | 01, 04, 11. Crashed client releases lease; unauthenticated request executes no privileged actions. |

### Pets and Windows

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 37 | Render/sampling budget: shared captures and adaptive FPS; rest when offscreen or session locked. | High / P1 | L / Medium | Baseline. Compare pet off/asleep/active; no sustained handle drift after an hour. |
| 38 | Per-monitor positions, mixed DPI, reconnection, hidden taskbar and Explorer restart. | Medium / P2 | L / Medium | 37. No offscreen windows; logical position retained across DPI changes. |
| 39 | Pet as optional entry to status/explanations and controlled brief notices. | Medium / P2 | M / Low | 02, 20. Zero accidental cleanup/termination from a gesture; quiet/accessibility respected. |
| 40 | Data-only community packs, frame/image limits, preview and validated format. | Medium / P3 | XL / High | Versioned schema and bounded renderer. Invalid/huge content rejected before consuming resources. |

Animations express useful states. Unnecessary cleanups are not rewarded, and private information is not exposed through desktop notices. Pets remain optional.

### Validation, delivery and privacy

| ID | Proposal and result | Impact / Priority | Effort / Risk | Dependencies and acceptance |
|---|---|---|---|---|
| 41 | Public matrix of tested Windows, architecture, engine and wrapper; distinguish physical/VM/compiled. | High / P0 | L / Low | Isolated harness. Every tested label links precise evidence and limits. |
| 42 | Generative tests for elevated protocols, hostile inputs, reused PIDs and failure recovery. | High / P0 | L / High | 01 and current contracts. Bounded fuzzing, no actions on unrelated processes; unknown format rejected. |
| 43 | Visual/UI Automation suite: English/Spanish, DPI, themes, keyboard and critical-flow screenshots. | High / P1 | L / Medium | 24–25. Detect functional, translation and layout regressions; do not rely only on pixel equality. |
| 44 | Release provenance and operational Authenticode signing; validate signatures, hashes, architecture and rollback. | High / P1 | L / Medium | CI and certificate custody. Evaluate cost; do not promise removal of SmartScreen notices. |
| 45 | Feature/compatibility catalog generating docs and checking versions, links, screenshots and translations. | High / P1 | L / Low | 15, 41. Block inconsistent publishing; version videos and make regeneration reproducible. |
| 46 | Local-data center: retention, export, delete, diagnostic review and secret protection. | High / P1 | L / Medium | 05–06, 30. Verified deletion; optional export; no implicit telemetry. |

## 5. Proposed architecture and migration

```text
Panel / CLI / links / rules / game mode / watchdog / integrations
                              |
                     OperationCoordinator
                       intent + identity
                              |
              EngineInstance + EngineCapabilities
                 |             |             |
               Ollama       llama.cpp     LM Studio
                              |
             SnapshotStore -> PolicyEvaluator -> ActionPlan
                              |
                  observation / validated execution
                              |
              outcomes + metrics + local history
```

Candidate prototype contracts: `EngineInstanceId`, `EngineCapabilities`, `OperationRequest`, `OperationState`, `PolicyDecision`, `ActionPlan`, `ResourceSnapshot` and `ClientLease`. Reuse `ProcessIdentity`, agent outcomes and current contracts where appropriate. An adapter-based migration allows parity testing before replacing older paths.

Elevated operations retain specific scope, strict validation and checks inside the agent. Read capability does not grant execution permission. Termination continues to require the appropriate confirmation flow; an automatic profile does not authorize killing arbitrary applications. Global enumeration may support observation, but does not define targets for an instance-directed command. A task failing or already exiting does not authorize a global sweep of servers either.

Job Objects can be evaluated for processes launched and managed by the app, with compatibility checks and fallbacks. Windows inheritance and breakaway behavior prevents assuming universal control over external descendants. [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

New secrets use per-user protected storage and are excluded from logged arguments, exports and screenshots. Community extensions are validated data; loading third-party code is outside the initial proposal.

## 6. Hypotheses to validate

| Hypothesis | Experiment | Decision evidence |
|---|---|---|
| Sleeping idle models is more useful than repeating trims under certain workloads. | Repeat a fixed workload with current policy, a no-action control and the proposal. | Sustained pressure, reloads, latency and page faults; retain negative changes. |
| Understandable budgets reduce manual interventions. | User prototype: set headroom and explain three decisions. | Completed tasks, interpretation errors and undone actions. |
| An instance can be managed without affecting others. | Three engines/ports, two tasks plus a manual server, exits, Stop errors, reused PIDs and external restarts. | Each instance's states, identities and API before/after; only the requested target changes. |
| Native APIs/TTL improve sleep/wake on current engines. | Test newer/older versions with generation, sleep and reload. | Declared compatibility, actual outcomes and no unnecessary restarts. |
| Sharing samples lowers the app's own cost. | Same workload/hardware with existing and coordinated clocks. | CPU, sampling time, handles, memory and responsiveness. |
| Confidence-based attribution is more useful than an approximate definitive name. | Simultaneous clients, short connections and same-name processes. | Precision where measurable; unknown for unattributable cases. |
| Temporary leases prevent unwanted unloads without blocking resources forever. | Clients that renew, exit, hang and forge requests. | Expired leases, rejected inputs and per-client cost. |

Local records contain only the signals needed for these experiments and remain bounded. Sending results to the maintainer is an explicit user action.

## 7. Phases and delivery gates

Indicative estimates for a small team with continuous validation and access to the required environments. They assume one dedicated developer and QA support; partial availability, obtaining hardware or resolving compatibility can extend the schedule. These are planning horizons, recalculated after prototype measurement, and exclude some later experiments. IDs indicate work areas: each phase selects a coherent increment and may split large improvements; it does not promise to close every backlog item in those areas in one delivery. Do not add task sizes as though everything could run in parallel.

| Phase | Indicative horizon | Scope | Completion gate |
|---|---|---|---|
| 0. Measure and design | 1–2 weeks | Published baseline, states, costs, reference hardware and repeatable workloads. | Reproducible record/current metrics; case list and invariants. |
| 1. Reliable foundation | 4–8 weeks | 01–05, 13, 15, 24, 33, 41–42; core contracts and gradual adapters. | Verified parity, migration, concurrency, cancellation, identity and intent; isolated task shutdown. |
| 2. Modern engines | 4–8 weeks | 14, 16–19, 29; initial 21–22 improvements. | Three real instances, older versions and precise capabilities catalog. |
| 3. Budgets and simulator | 4–8 weeks | 07–11, 27, 30–32, 34; pilot before expansion. | No-action observation, reproducible decisions and comparative cost study. |
| 4. Experience and efficiency | 4–8 weeks | Increments of 20, 23, 25–26, 31, 35, 37–39, 43, 45–46. | Accessibility, app resource use, export and documentation consistency for the selected increment. |
| 5. Later pilots | Approve after measurement | 12, 36 and 40; complete 06/44 according to support/distribution needs. | Each pilot has a budget, users, stop/continue criteria and its own validation. |

Improvements 06 and 44 can move earlier where needed for support or publishing. Protecting data and verifying packages do not need to wait until the end.

Deliver small increments with documentation and rollback. Name versions by actual scope and workspace SemVer: compatible features can be minor; a public breaking change requires major. Do not assign one future version to every backlog idea.

## 8. Measurable targets

These are engineering gates in declared environments, not universal guarantees for every PC.

- **Isolation:** zero actions on unrelated instances in control, game, watchdog and recovery tests; two-task plus manual-server case mandatory for 04.
- **Activity:** zero automatic unloads during detectable active generation in reference workloads. Unknown activity remains labeled and uses the chosen conservative behavior.
- **Intent:** restart, lock, denied UAC and updates do not erase preferences or manual decisions.
- **Simulator:** zero execution calls; the same record/policy produces the same plan.
- **Stability:** one-hour tests and an eight-hour session; no sustained handle growth after warmup and bounded resource-cost regression from baseline.
- **Sampling:** initial p95 target below 200 ms with five hundred processes on the reference machine; confirm/recalibrate after measurement. Filtering/sorting a thousand processes remains responsive.
- **UI:** initial p95 target below 250 ms to open a warm panel; define measurement and background workloads.
- **Effect:** compare pressure duration, available memory, reloads, latency and paging under the same sequence. Do not infer FPS or energy from a RAM drop.
- **Accessibility:** complete keyboard/Narrator flows, contrast and 100–200% scaling in both languages.
- **Compatibility:** public matrix distinguishes physical machine, VM, compilation and simulated fallback; every tested claim has evidence.
- **Privacy:** secrets excluded from logs/exports; retention/deletion tested; migration adds no telemetry.
- **Delivery:** version, packages, hashes, bilingual README files, release notes, screenshots and website checked together. Link downloads after publication.

### Measurement method

Phase 0 fixes hardware, Windows version, engines, models, context and configuration. Repeat each comparable scenario at least three times; record warmup and background workload. Measure CPU as CPU time and a fraction of one core, private/resident memory separately, handles and data size. Retain the distribution, p95 and variability; set the allowed budget before evaluating a delivery.

Harnesses use identified test processes, tasks, services and ports and clean up only their own resources. The comparative study uses a no-action control, current policy and proposal over the same sequence; note any interference. The 200/250 ms goals are initial targets: if the baseline contradicts feasibility, recalibrate with evidence and record why. Compiling an architecture does not become a physical test of it.

## 9. Risks and mitigations

| Risk | Proposed mitigation | Gate |
|---|---|---|
| Unload/reload cycles worsen latency. | Hysteresis, cooldown, priority and cost budgets; start in observation. | Reload/latency comparison with baseline. |
| Action affects a different process/backend. | Identity, ownership, revalidation and explicit-scope operations. | Adversarial PID, port and name tests. |
| Current `Switch.Stop(Task)` extends shutdown to the global server list. | Idea 04 P0: resolve verifiable target/descendants before termination; do not expand scope on error or unknown identity. | Two tasks plus a manual server, successful/failed Stop and an already closed target; others retain identity and response. |
| Slow loading mistaken for a hang. | Phase state, provider signals and measured deadlines. | Long loading and real generation without false restart. |
| Engine APIs change between versions. | Negotiation, adapters, fallbacks and declared matrix. | Newer/older versions and partial responses tested. |
| More sampling/series consumes resources. | Shared captures, compaction, limits and optional retention. | CPU/RAM/handles/disk within agreed budget. |
| Export discloses private data. | Field redaction, preview, excluded secrets and explicit action. | Fixtures containing paths, users and tokens. |
| Leases/pet packs introduce untrusted input. | Identity/TTL/quotas; data without code and resource limits. | Fuzzing, expiry and hostile-file rejection. |
| Cross-cutting refactor breaks current behavior. | Adapters, parity, gradual migration and rollback. | Existing/new harnesses pass before replacement. |

## 10. What not to prioritize now

- **Rewriting WPF in another framework:** this would require rebuilding native integration, accessibility and tests; the proposed benefits can be achieved using the current architecture.
- **Promising fixed reclaimed memory or faster performance on any PC:** shared memory, buffers and external workload change outcomes. Measure reproducible workloads first.
- **Automatically killing arbitrary processes to meet a budget:** this adds risk of lost work; prioritize managed resources and explicit controls.
- **Changing Windows-wide services, startup or power policies:** this expands permissions and scope. Initial profiles coordinate only the app's resources.
- **Remote control, accounts or cloud sync in the first stage:** these require new security, identity and data policies and do not address the first local debts.
- **Mandatory inference proxy or conversation logging:** this makes external clients depend on the app and increases data exposure. Leases are optional.
- **Plugins containing downloaded code:** signature review and sandboxing would be a separate project. Initial community formats accept bounded data only.
- **Gamifying a reclaimed-RAM number:** this may encourage unnecessary trims. Pets should communicate states and respect ongoing work.
- **Measuring energy savings from CPU or RAM alone:** an energy claim needs appropriate methods/hardware; initial goals are app resource use and pressure.

## 11. Sources and maintenance rules

Primary sources checked on October 3, 2026; recheck during implementation because engines evolve:

- [Ollama: active models](https://docs.ollama.com/api/ps) — model and memory signals.
- [Ollama: FAQ](https://docs.ollama.com/faq) — preload, residency and client-control limits.
- [llama.cpp server](https://github.com/ggml-org/llama.cpp/tree/master/tools/server) — routing, APIs, events and native sleep.
- [LM Studio REST](https://lmstudio.ai/docs/developer/rest) — API v1, models and authentication.
- [Microsoft Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set) — residency and page faults.
- [Microsoft Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects) — process grouping and exceptions.
- [Microsoft Windows accessibility](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility) — keyboard, programmatic access and contrast.
- [Reviewed Backend code](../src/IsTargetSleeping/Core/Backend.cs) — evidence of current global scope in `Switch.Stop(Task)`, `KillServers` and `Detector.ServerPids`.
- [Workspace rules](../CLAUDE.md) — versions, bilingual README files, website, publishing and delivery.

When turning a proposal into work, record scope, design, tests, decisions and delivery status. Keep both plans equivalent; do not mark an idea implemented merely because a prototype exists. The 1.5.2 integration results belong in its validation record, not this plan.
