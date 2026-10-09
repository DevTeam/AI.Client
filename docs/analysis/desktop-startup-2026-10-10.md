# Desktop startup: what the ~46 s of `S-embedded` is made of

Author: Ada · Profiler. Date: 2026-10-10. Status: analysis only — no measurement run was
started for this report, and no profiler was launched.

Scope: explain `S-embedded` (~46 s to `desktopReady`) against `S-shared` (~1.4 s), decompose the
startup marker into phases, and separate real hot spots from work the merged measurement
infrastructure has already removed.

## 1. Evidence used

| Item | Value |
| --- | --- |
| Trace | `.trace/desktop-20261009-191303-eventpipe/desktop.speedscope.json`, 24,450,029 bytes, Speedscope `evented`, unit ms |
| `session.json` | `profiler=eventpipe`, `dataDirectory=null` (default data dir, not a measurement copy), executable `artifacts/profile-desktop/AI.Desktop.exe` |
| Profiled PID | 68100 (from the trace's own process frame); the process was not killed by this analysis |
| Profile content | 44 thread profiles, 4,571 frames, session ~71 s (thread 28864 is the UI thread) |
| Commit position of the trace | captured 19:13, i.e. **after** `d7b1b8d3` (18:52, run-recovery optimization) and **before** `ce296b0f` (20:04, startup marker) and `caf693b0` (20:42, measurement command) |
| Accepted baseline | `caf693b0` / `fa38e458` tree: `S-embedded` serverReady 46068 (43096–46573), uiReady 46597 (43637–47101), desktopReady 47976 (45005–48569); `S-shared` 194 (185–202), 770 (757–785), 1419 (1417–1606) |

`at` values in the export are absolute times from the session start, not offsets inside a
profile; all windows below are in that time base.

## 2. Phase decomposition of the traced run

Windows are taken from the frame boundaries on the UI thread (`Thread (28864)`) and the marker
semantics of `src/AI.Desktop/StartupTimeline.cs` (`serverReady` → `uiReady` → `desktopReady`).

| Phase | Window (ms) | Duration | Share of `desktopReady` | What closes it |
| --- | --- | --- | --- | --- |
| S — before the server answers | 0 → 19785 | ~19,785 | 89% | `StartWithClassicDesktopLifetime` begins at 19785, right after the synchronous server start returns |
| UI — window built, before first navigation completes | 19785 → 20964 | 1,179 | 5% | `Dispatcher.MainLoop` begins at 20964; `StartupTimeline.UiReady()` is called from `App.axaml.cs:34` |
| Nav — first WebView navigation | 20964 → 22181 | 1,217 | 5% | `MainWindow.OnNavigationCompleted` at 22179–22181 calls `StartupTimeline.UiLoaded()` (`MainWindow.axaml.cs:224`) |

So in the trace `desktopReady` ≈ **22.2 s**, while the accepted baseline on the measurement copy
is 48.0 s. The structure transfers, the absolute values do not: the trace ran on the default data
directory (`session.json` says `dataDirectory=null`) while the baseline runs on a fresh copy under
`D:\AT_tests`, and the two differed in data volume. Percentages below are from the trace and are
the part this report relies on.

Everything after 22181 is steady state: `Dispatcher.MainLoop` runs until 70960 (49,996 ms) and the
process ends at ~71 s. That is application lifetime, not startup cost.

## 3. Why `S-embedded` is ~46 s and `S-shared` ~1.4 s

The difference is entirely in the S phase.

In `S-shared` the Desktop process answers its own `serverReady` from `StartupTimeline.HostReused()`
after a single HTTP probe: `SharedHostLocator.Find` (`src/AI.Desktop/SharedHostLocator.cs:15`)
returns with a `SharedHostState(address)` because the installed Host answered, and everything heavy
already happened in that other process. Measured: 194 ms. The remaining 1.2 s is window creation
plus one navigation.

In `S-embedded` the whole server starts inside the Desktop process, and it starts **on the UI
thread, synchronously, before any window exists**:

```
src/AI.Desktop/DesktopRunner.cs:46
    running = Task.Run(() => server.Server.StartAsync(server, CancellationToken.None)).GetAwaiter().GetResult();
```

The UI thread then waits 18,558 ms (1134 → 19692) with `DesktopRunner.Run` as the innermost real
frame — no window, no dispatcher, nothing drawn. In that same window `CPU_TIME` covers 18,461 of
18,592 ms (99.3%) across ~3 cores: this is not a process waiting for the network, it is the process
burning CPU to read its own data directory before it is willing to serve the UI.

## 4. What is inside those 18.5 s

The synchronous start path is:

```
AiClientServer.StartAsync            src/AI.Server/Hosting/AiClientServer.cs:25,28,29
  → app.StartAsync                   (hosted services)
    → ChatRunHostedService.StartAsync    src/AI.Server/Hosting/ChatRunHostedService.cs:14
      → await dispatcher.WarmUpAsync(...)
        → ChatRunDispatcher.WarmUpAsync          src/AI.Server/Application/Runs/ChatRunDispatcher.cs:79
          → repository.ListAsync                  (line 81)
          → per chat group: chatMutations.LoadForRunRecoveryAsync   (line 88)
            → ChatService.LoadForRunRecoveryAsync            src/AI.Server/Application/Chats/ChatService.cs:266
              → JsonChatRepository.GetAsync                  src/AI.Server/Infrastructure/Storage/JsonChatRepository.cs:64,67
                → SystemFileSystem.ReadTextAsync             src/AI.Contracts/FileSystem/SystemFileSystem.cs:46,56
                  → ChatDocumentSerializer.Deserialize       src/AI.Server/Infrastructure/Storage/ChatDocumentSerializer.cs:61,63
```

Timings, union of frames with that name across all threads inside the S window:

| Step | First start → last end (ms) | Busy on stack |
| --- | --- | --- |
| `WarmUpAsync` | 1640 → 19732 (18,092 ms) | 149 ms |
| `repository.ListAsync` (319 run files) | 1640 → 1804 (164 ms) | 148 ms |
| `LoadForRunRecoveryAsync` (per chat) | 1800 → 19732 (17,932 ms) | 1,122 ms |
| `ReadTextAsync` | 1598 → 70997 | 9,732 ms |
| `StreamReader.ReadBufferAsync` | 1711 → 70997 | 18,993 ms |
| `ChatDocumentSerializer.Deserialize` | 1832 → 70854 | 4,885 ms |

`WarmUpAsync` begins at 1640 — exactly where the first synchronous segment of
`AiClientServer.StartAsync` (1184 → 1654) hands over — and ends at 19732, which is when the UI
thread is released at 19692–19785. The S phase *is* run recovery.

The volume of that recovery, measured on the source data directory
(`C:\Users\Pyanikov_N\AppData\Local\AI`, read-only):

| Metric | Value |
| --- | --- |
| `*.run.json` files | 319 |
| Chat documents they resolve to | 319 (all 319 map; 0 orphaned) |
| Bytes the recovery must read and parse | 1,371,958,679 B ≈ **1,308 MB** |
| Median chat document | 1,235 KB |
| p90 chat document | 11,034 KB |
| Largest chat document | 40.7 MB |
| Whole data directory | 2,112 MB |

For comparison the run list itself is trivial: 164 ms for all 319 files. The cost is not the number
of runs, it is that each run pulls its whole chat document through
`ReadTextAsync → JsonSerializer.Deserialize` — the recovery keeps in-memory state per run, so it
reads 1.3 GB to recover what is, in this directory, a small number of runs.

Concurrently the finalizer thread is busy for almost the whole window: `Gen2GcCallback.Finalize`
1554 → 20252 (18,699 ms) with 18,369 ms of `CPU_TIME` on `Thread (7476)`. That is the garbage the
deserialize step produces, not an independent defect, but it explains why one logical task keeps
three cores occupied.

## 5. Hot-spot rating

Method: sampled stacks (Speedscope `evented`, `O`/`C` events) replayed at 20 ms over the S window
(1140–19732); *inclusive* = share of the window where the frame is on any stack; *exclusive* = the
frame's own time after subtracting its children; phase = `server` / `ui` / `webrender` / `idle`
according to where the work happens, not where the frame is defined. `file:line` points at the
statement that holds the time.

| # | Hot spot | file:line | Phase | Inclusive | Exclusive | Hypothesis | Expected effect | Risk |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `ChatRunHostedService.StartAsync` awaits `WarmUpAsync` inside the host start path | `src/AI.Server/Hosting/ChatRunHostedService.cs:14` | server | 18,092 ms ≈ 97% of S, ≈ 38% of `desktopReady` | ~0 (pure await) | Kestrel cannot answer until the last hosted service returns, and this one reads the whole run store first | Moving recovery off the start path removes essentially the whole S phase; `serverReady`, and therefore `uiReady`/`desktopReady`, drop by the recovery time (~15–18 s of the 46 s baseline) | The server can now answer with a partially recovered run set; the first subscriber's snapshot may be incomplete (mitigated by publishing once at the end, not per run). A client that connects during recovery sees a prefix of the runs |
| 2 | `ChatRunDispatcher.WarmUpAsync` reads and parses every chat that has a run | `src/AI.Server/Application/Runs/ChatRunDispatcher.cs:79` (line 88 per chat) | server | 18,092 ms | 149 ms | Keeps per-run in-memory state, so it must materialize whole chat documents | Same as #1 — this is the body of #1 | Requires the run list and the chat document to stay consistent; a chat read on a background task races a concurrent edit that the old blocking order made impossible |
| 3 | `LoadForRunRecoveryAsync` → `GetAsync` → `ReadTextAsync` → `Deserialize` over 1.3 GB | `ChatService.cs:266`, `JsonChatRepository.cs:64,67`, `ChatDocumentSerializer.cs:61,63` | server | 17,932 ms span, 1,122 ms on stack | drives `ReadTextAsync` 9,732 ms and `Deserialize` 4,885 ms of busy stack time | Recovery reads whole documents to keep a handful of run states | Lazy/partial recovery (run state only, chat loaded on demand) would cut the read volume by an order of magnitude — **not** chosen by Ada or Cleo and not to be done without the lead, because it changes what `GET /api/updates` can observe | Contract 6: a lazy read must not change the observable update state or make a recovered run invisible |
| 4 | Finalizer thread burning one core for the whole window | `System.Gen2GcCallback.Finalize` (framework), consequence of #3 | server | 18,699 ms | 18,369 ms CPU on the finalizer thread | The transient JSON graph of each document becomes garbage immediately | A consequence, not a fix target; it shrinks by itself when #3 shrinks | none on its own |
| 5 | `SharedHostLocator.Find` blocks the UI thread for a fixed HTTP probe before any server work | `src/AI.Desktop/SharedHostLocator.cs:15` (client at line 19, `Timeout = 1 s`) | ui | 1,020 ms (128 → 1148) | 1,020 ms (synchronous `GetResult`) | Mode detection runs before the window exists and pays the full timeout when no Host is listening | ~1 s in `S-embedded` — small next to #1, but it is pure added latency on the critical path and it is the *first* thing that happens | Making the probe asynchronous changes the mode decision point; the lock/ownership behavior must stay identical |

S phase total: 18,592 ms, of which `CPU_TIME` union 18,461 ms (99.3%) and no sample with zero
active work. There is no idle to reclaim in this phase — only work to move or avoid.

## 6. Hot but expected, and hot points that must **not** be reported

- **`WorkspaceFileSearch.Index.Walk` — 10,721 ms, and it is not a startup hot spot.** It runs at
  26.5 s (partial, 66–368 ms) and again at 60.7 s (one instance 10,353 ms), i.e. **well after**
  `desktopReady` at 22.2 s. It is background indexing for the composer's `@` list, triggered by the
  UI, not by startup. An earlier note in this task treated it as a startup hot spot; that was
  wrong and is retracted here.
- `ChatService.ToDetails` / `ToolResultErrorFlag` — 1,131 ms busy, but the frames span to 59.1 s:
  steady-state chat loading while the user works, not startup.
- `Dispatcher.MainLoop` (49,996 ms) and `StartWithClassicDesktopLifetime` (51,175 ms) — the UI
  thread's whole lifetime; they end when the window closes. Expected, not cost.
- `AiClientServer.Build` — 396 ms (1192 → 1588) for the whole ASP.NET builder and endpoint
  mapping. Expected startup work; not worth attacking at this scale.
- `DataDirectoryLock.Acquire` — 2 ms. `BuiltInSkillCatalog..ctor`, `SkillMarkdown.Parse`,
  `JsonSchema.__ModuleInitialization` — below one 20 ms sample each in this window.
- `repository.ListAsync` for the run list — 164 ms for 319 files. Cheap.

## 7. What the already-merged infrastructure removed (and what it did not)

- `fa38e458` (was `caf693b0`) — `build/Targets/DesktopStartupMeasurementTarget.cs`,
  `build/Targets/IDesktopStartupMeasurementTarget.cs`, `build/BuildApplication.cs`,
  `build/Composition.cs`. 4 files, +845/−1.
- `03d1b58e` (was `8a42af8d`) — one file, `build/Targets/DesktopStartupMeasurementTarget.cs`,
  +12/−9: the lock moved from `<worktree>/.trace/measure-desktop.lock` to the single absolute
  machine path `D:\AT_tests\desktop-startup\measure-desktop.lock`.

**Neither of them touches the application's startup path.** They changed only the build harness:
they make the numbers measurable and serialized. No entry in the hot-spot table above has been
removed by them, and none is claimed to be.

The commit that *did* remove startup work is `d7b1b8d3` ("Add `LoadForRunRecoveryAsync` to
`IChatMutations` and optimize chat recovery logic in `ChatRunDispatcher`"), already in `master` and
already in the base of `perf/startup-profiler`. The trace postdates it (19:13 vs 18:52). So:

- The former hot spot of the recovery loading the same large chat more than once **no longer
  exists** and must not appear in any hot-spot list; the 18 s above is the residual of the
  *optimized* recovery.
- The old trace cannot be used to judge anything about `ce296b0f` (marker) or later: the marker
  line and the measurement command did not exist when it was captured, so it contains no
  `Startup: desktop ready` line and its PID 68100 predates every fix under discussion.

## 8. Which phase each of the two teammate fixes moves

`desktopReady` is closed by the first completed WebView navigation
(`StartupTimeline.UiLoaded` from `MainWindow.OnNavigationCompleted`, `MainWindow.axaml.cs:224`),
and both `uiReady` and `desktopReady` are stamped with `??=`, so they can only move *earlier* if
the thing that gates them moves.

| Change | `S-embedded` `serverReady` | `S-embedded` `uiReady` | `S-embedded` `desktopReady` | `S-shared` |
| --- | --- | --- | --- | --- |
| Bo · Desktop-start — show the window before the embedded server is ready | unmoved | **moves earlier** (window exists before the server does) | unmoved — the first navigation still needs an address to load | unmoved |
| Cleo · Server-start — start the Host before run recovery finishes (`9b9404a8`) | **moves earlier** by the recovery time | moves earlier with it | moves earlier with it | **unmoved for Desktop**: Desktop prints `HostReused` after a 194 ms probe and never ran the recovery itself |

The practical consequence for the team: in `S-embedded` Cleo's change is the one that moves the
headline number, and Bo's is the one that gets pixels on screen earlier without moving the metric.
In `S-shared` neither moves `Desktop`'s numbers; Cleo's change belongs to the Host's own
readiness metric, which must be reported separately or it will look like a regression in
`Desktop`'s `serverReady` that never appears.

## 9. Measurement procedure (unchanged and reproducible)

The procedure that produced the accepted baseline, for anyone repeating it:

```
dotnet run --project build/build.csproj -- measure-desktop --scenario embedded|shared --data-dir <source data dir>
```

- 1 warm-up + 3 measured runs per scenario; report the **median** and the **min…max** range of
  `serverReady`, `uiReady`, `desktopReady`.
- Numbers come only from the marker line `Startup: desktop ready mode=...\tserver\tui\tdesktop`
  (`src/AI.Desktop/StartupTimeline.cs:93`). A missing or malformed line is a non-zero error, never
  an estimate. `mode=embedded` is required for `S-embedded` and `mode=shared` for `S-shared`;
  `mode=failed` or a mismatch fails the run.
- A **fresh copy** of the data directory per run, outside the repository, under
  `D:\AT_tests\<scenario>-<label>`; the source is read-only and its fingerprint
  (`settings.json`, `client-settings.json`, `.lock` size and mtime) is reported as unchanged.
- The app and the temporary Host receive the **copy** path via `AI_CLIENT_DATA_DIRECTORY`
  (Host additionally `--data-dir <copy>`); running on the source directory violates contract 1.
- `S-shared` starts a temporary Host on `127.0.0.1:52174` with `--public-web --no-tray` — without
  `--public-web` the `api/bridge/session` route is not mapped and the run fails with
  "the Host did not answer in time". Desktop gets
  `AI_CLIENT_HOST_ADDRESS=http://127.0.0.1:52174/`. The absence of a user Host on 52173 is not an
  error.
- One build and one run at a time on the machine, enforced by the absolute lock
  `D:\AT_tests\desktop-startup\measure-desktop.lock` (a second run exits 1 with
  "Another desktop measurement is already running…").
- Record HEAD, configuration (`Release`, `artifacts/profile-desktop`), mode, PID and duration per
  run.

## 10. Open questions for the lead

1. Hot spot #3 (recovery reading 1.3 GB of whole chat documents) is the real remaining cost, but a
   lazy read changes what `GET /api/updates` can observe. A decision is needed on whether the
   update state may be served from run states alone before any chat document is materialized.
2. `SharedHostLocator.Find` (#5) costs a fixed 1 s on the embedded critical path. Making it
   asynchronous is small but touches the ownership/mode decision; approval is needed before anyone
   changes it.
3. The trace's absolute numbers (~22 s) and the baseline's (~48 s) differ because they ran on
   different data directories. Any comparison of "improvement ≈ N s" should quote the baseline,
   and the trace only for shares.

## 11. Actual deltas versus the prediction in §8

Added 2026-10-10, after both teammates' measurements arrived. The numbers below are theirs, not
produced here: no run and no profiler was started for this section. Each candidate was measured
against the same base commit `03d1b58e`, but in its own measurement session, so the two "before"
columns are not comparable with each other — every Δ is valid inside its own pair.

### 11.1 Bo · Desktop-start (`c8fee7e0`), "before" `03d1b58e` → "after" `f1a0ed65`

Median [min..max] in ms:

| Scenario | Phase | Before | After | Δ median |
| --- | --- | --- | --- | --- |
| `S-embedded` | `serverReady` | 45254 [44792..47929] | 45092 [44437..49973] | −0.4% |
| `S-embedded` | `uiReady` | 45795 [45318..48469] | 1691 [1686..1725] | **−96.3%** |
| `S-embedded` | `desktopReady` | 47100 [46732..49805] | 46007 [45345..51007] | −2.3% |
| `S-shared` | `desktopReady` | 1706 [1514..1778] | 1520 [1464..1558] | −10.9% |

### 11.2 Cleo · Server-start (`9b9404a8`), "before" `03d1b58e` → "after" `381cfacc`

Median in ms (the report this came from gave point values for the pair):

| Scenario | Phase | Before | After | Δ median |
| --- | --- | --- | --- | --- |
| `S-embedded` | `serverReady` | 47108 | 1501 | **−96.8%** |
| `S-embedded` | `uiReady` | 47645 | 2048 | −95.7% |
| `S-embedded` | `desktopReady` | 48928 | 3483 | **−92.9%** |
| `S-shared` | `desktopReady` | 1627 | 1618 | −0.6% |

### 11.3 Prediction vs. fact

| §8 prediction | Fact | Verdict |
| --- | --- | --- |
| Bo leaves `S-embedded` `serverReady` unmoved | 45254 → 45092, −0.4% | confirmed |
| Bo moves `S-embedded` `uiReady` earlier, because the window exists before the server does | 45795 → 1691, −96.3% | confirmed, and larger than §8's wording implied: the window is on screen ~44 s before the embedded server answers |
| Bo leaves `S-embedded` `desktopReady` unmoved — the first navigation still needs an address to load | 47100 → 46007, −2.3%; ranges [46732..49805] and [45345..51007] overlap | confirmed; the −2.3% is inside run-to-run noise |
| Bo leaves `S-shared` unmoved | 1706 → 1520, −10.9%; ranges [1514..1778] and [1464..1558] overlap | confirmed as noise, no real movement |
| Cleo moves `S-embedded` `serverReady` earlier by the recovery time, and `uiReady`/`desktopReady` with it | 47108 → 1501, 47645 → 2048, 48928 → 3483 | confirmed |
| Cleo leaves `S-shared` `desktopReady` unmoved: Desktop prints `HostReused` after its own probe and never ran the recovery itself | 1627 → 1618, −0.6% | confirmed |

**The size prediction was conservative, not wrong in kind.** §4 estimated the recovery hold at
~18.5 s from the trace, and §5/§8 quoted "~15–18 s of the 46 s baseline". Cleo's actual
`serverReady` gain is ~45.6 s (47108 → 1501). The gap is the one already flagged in §2 and §10.3:
the trace ran on the default data directory (`session.json`: `dataDirectory=null`, `desktopReady`
≈ 22.2 s), while the measurement runs on a fresh copy under `D:\AT_tests` whose chat documents are
more numerous and larger, and where the same recovery costs ~45 s instead of ~18 s. The trace was
reliable for *shares* (§2 percentages) and for *where the time is* (§4 stacks), and it
under-predicted the *absolute* prize — which is exactly why the headline number must quote the
baseline, not the trace.

Two consistency checks on the post-fix picture, both derived from §2's structure rather than from
new data:

- After Cleo, `S-embedded` `desktopReady` minus `serverReady` is 3483 − 1501 ≈ 2.0 s, and §2's UI +
  nav phases were 1179 + 1217 ≈ 2.4 s in the trace. That residual is window creation plus one
  navigation — the part no server-side change can remove.
- After Cleo, `serverReady` at ~1.5 s sits where §4 put "before the socket": host build, endpoint
  mapping and the pre-recovery segment. That defines the headroom left on this path.

## 12. Decisions and limitations recorded for this report

1. **Criterion (contract 5) changed.** An improvement is now required in the scenario where the
   optimized segment lies on the startup path; in the other scenarios it is enough to show the
   absence of a regression. Under this criterion Bo's and Cleo's `S-shared` rows are "no
   regression" checks, not targets. Cleo's change is merged (`63b9a569`); Bo's awaits a user
   decision.
2. **Hot spots #1 and #2 are closed by Cleo's change, and #2's estimate is withdrawn.** §5's rows
   #1/#2 describe work that no longer gates `serverReady`; they must not be re-quoted as open
   work.
3. **Candidate #3 (lazy/partial recovery of the 1.3 GB of chat documents) is not authorized at
   this stage.** It changes what `GET /api/updates` can observe (contract 6), and with recovery no
   longer on the path to the socket its cost is no longer a startup cost. Revisit only with
   numbers, and probably as a task with its own contract.
4. **Cleo's second candidate (`Updates/**`) is closed as exhausted:** after the change ~1.5 s
   remain before the socket, and that share of the metric is below the 5% threshold.
5. **`SharedHostLocator.Find` (#5, the fixed ~1 s probe) is not authorized now.** It is ~1 s of 46
   (about 2%), below the 5% threshold on its own, it lives in `src/AI.Desktop/**` (not this
   analysis's zone), and it touches the mode/ownership decision. Kept as a candidate for a
   possible next iteration.
6. **`S-shared` numbers are not a `Desktop` metric for Cleo's change.** In `S-shared` Desktop
   answers `HostReused` after its own probe and never runs the recovery; Cleo's change belongs to
   the Host's readiness and must be reported as its own metric, never as a `Desktop` regression.
7. **The trace cannot judge anything after `ce296b0f`.** It was captured at 19:13 with PID 68100,
   before the marker line and the measurement command existed, and it contains no
   `Startup: desktop ready` line. Every figure in §11 comes from the measurement runs, not from it.

§11 and §12 only add findings; §1–§10 stand unchanged, including the retraction in §6
(`WorkspaceFileSearch.Index.Walk` is not a startup hot spot).