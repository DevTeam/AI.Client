# Desktop performance profiling

Run the `Profile AI Desktop (LLM)` configuration in Rider to collect startup and interaction data. It builds a Release Desktop app into `artifacts/profile-desktop`, then starts it under `dotnet-trace`. Switch chats and branches, then close the Desktop window. The build target waits for trace finalization and writes a new directory under `.trace/`:

- `desktop.nettrace`: original EventPipe trace; keep it for further analysis.
- `desktop.speedscope.json`: sampled stacks in a JSON format that can be inspected with Speedscope or processed by an LLM.
- `top-exclusive.txt` and `top-inclusive.txt`: the 100 hottest methods by self time and total time.
- `session.json`: profiler and launch details.

For a dotTrace CPU snapshot, run `Profile AI Desktop (dotTrace Sampling)`. It saves `desktop.dtp` for Rider. For thread scheduling, waits, and other Timeline data, run `Profile AI Desktop (dotTrace Timeline)` instead. It saves `desktop.dtt`. Open either snapshot in Rider's dotTrace viewer. Timeline requires the JetBrains ETW Host Service with administrator privileges on Windows; this configuration requests elevation. JetBrains' command-line Reporter does not support Timeline snapshots, so this mode does not generate a text report.

The Rider configurations use the application's default data directory: `AI_CLIENT_DATA_DIRECTORY` when set, otherwise `%LOCALAPPDATA%\AI` on Windows. Close another application using that directory before profiling. To profile a different directory, add `--data-dir <path>` to the configuration. To run outside Rider:

```powershell
dotnet run --project build/build.csproj -- profile-desktop --profiler eventpipe
dotnet run --project build/build.csproj -- profile-desktop --profiler dottrace
dotnet run --project build/build.csproj -- profile-desktop --profiler dottrace-timeline
```

The profile starts before the Desktop runtime starts. Close the application normally, then wait for `Profile saved to ...` in the Run window. Stop the Rider configuration only if the application cannot be closed; abrupt termination can leave an incomplete snapshot. Each run has a timestamped directory, and `.trace/` is ignored by Git. The EventPipe and dotTrace modes run separately so the measurements do not include the overhead of two profilers at once.

These profiles capture managed .NET execution. They do not capture JavaScript execution inside the embedded WebView; use the WebView developer tools performance recorder if the .NET traces do not explain a UI pause.
