# Architecture

OrbitConvert is a .NET 10 solution with a strict split between the conversion engine (no UI, testable
on its own) and the Windows-specific front ends.

```
OrbitConvert.App (WPF, MVVM)        OrbitConvert.Cli (console)
        │   │                               │
        │   └── OrbitConvert.Shell ─────────┼── (Win32/COM: hotkey, Explorer selection,
        │        (net10.0-windows)          │    context menu, single instance, DPI, hooks)
        ▼                                   ▼
             OrbitConvert.Converters  ── ImageMagick, FFmpeg, Pandoc, LibreOffice, PDF, archives
                       │
             OrbitConvert.Infrastructure ── process runner, tool discovery, persistence,
                       │                     safe extraction, downloads, logging
                       ▼
             OrbitConvert.Core ── formats & detection, capability registry, engine, queue,
                                  options, settings & history models (no I/O dependencies
                                  beyond the file system)
```

## Core concepts

| Type | Responsibility |
|---|---|
| `FileFormat`, `KnownFormats` | The formats OrbitConvert knows, with stable ids (`jpeg`, `mp4`, …) |
| `FileTypeDetector` | Identifies files by signature (magic bytes, ISO-BMFF brands, EBML doc type, ZIP contents) |
| `IConverter` | A conversion engine plug-in: declares `ConversionRoute`s, reports runtime availability, executes |
| `ConversionRoute` | One thing a converter can do: target key, accepted inputs, output, kind (`Convert`, `Transform`, `Combine`, `Extract`), limitations, experimental flag |
| `ConversionCapabilityRegistry` | Answers “what can these files become?” for any selection; picks the best available converter; explains why a target is unavailable; builds `ConversionPlan`s |
| `ConversionEngine` | Runs one plan: input checks → private work directory → converter → output validation → atomic commit → cleanup |
| `ConversionQueue` | Background execution with bounded parallelism, pause/resume for waiting jobs, cancel, retry |
| `OutputPlanner` | Collision handling (keep both / replace / skip), protection of input files, atomic moves |
| `OutputValidator` | Rejects missing, empty or wrongly-typed outputs (a JPEG renamed to `.png` fails) |

## A conversion, step by step

1. **Detect** – the registry reads each file's header and assigns a `FileFormat` (or none).
2. **Offer** – `GetTargets` intersects routes across all inputs. Unavailable routes are kept with a
   reason (for the full UI) and filtered out of the radial menu.
3. **Plan** – `CreatePlans` makes one plan per file, or one plan for combine routes (merge, ZIP). Options
   are cloned per plan.
4. **Run** – the queue hands the plan to the engine on a worker thread:
   - inputs must exist and be readable (locked files produce a clear message);
   - the converter writes only inside a fresh `%TEMP%\OrbitConvert\w\<id>` directory;
   - every produced path is checked to be inside that directory and validated by content;
   - outputs are moved to the destination (same volume: atomic rename; other volume: copy to a hidden
     `.partial` file, then rename), never over an input file;
   - the work directory is always deleted, also on failure and cancellation.
5. **Report** – job status/progress are observable; the app's `ConversionCoordinator` records history
   and raises a single notification per batch.

## External processes

`ProcessRunner` starts tools with `ProcessStartInfo.ArgumentList` (no shell, no string concatenation),
redirects output, and assigns each child to a Windows **Job Object** with
`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Cancellation terminates the job (whole process tree); if
OrbitConvert crashes, Windows closes the job handle and kills the tools. Timeouts are enforced per tool.

## Desktop app

- **MVVM** with CommunityToolkit.Mvvm source generators; views are `UserControl`s selected by
  `DataTemplate`s. All services come from `Microsoft.Extensions.DependencyInjection`.
- **Theming**: WPF's built-in Fluent theme (`Application.ThemeMode`) styles standard controls; semantic
  brushes in `Themes/Light.xaml` and `Themes/Dark.xaml` style custom surfaces and are swapped at runtime.
- **Radial menu** (`RadialMenuWindow`): a transparent, top-most window positioned in **physical pixels**
  at the cursor using the target monitor's effective DPI, clamped to that monitor's work area, and
  re-centered on `DpiChanged`. Items are laid out by `RadialMenuViewModel` (8 per inner ring, outer ring
  for more).
- **Single instance**: a per-user mutex plus a named pipe restricted to the current user
  (`PipeOptions.CurrentUserOnly`). Later launches forward their command and pass foreground rights with
  `AllowSetForegroundWindow` so the running instance may show its window.
- **Tray and notifications**: a Windows Forms `NotifyIcon` (balloon tips appear as standard toasts on
  Windows 10/11 and need no app identity).

See [SHELL_INTEGRATION.md](SHELL_INTEGRATION.md) for the activation modes.

## Data locations

| What | Where |
|---|---|
| Settings | `%LOCALAPPDATA%\OrbitConvert\settings.json` |
| History | `%LOCALAPPDATA%\OrbitConvert\history.json` (only when enabled) |
| Logs | `%LOCALAPPDATA%\OrbitConvert\logs\` (14 days) |
| Downloaded tools | `%LOCALAPPDATA%\OrbitConvert\tools\` |
| LibreOffice profile | `%LOCALAPPDATA%\OrbitConvert\libreoffice-profile\` |
| Work directories | `%TEMP%\OrbitConvert\w\` (deleted after each job; stale ones after a day) |

A `portable.txt` file next to `OrbitConvert.exe` moves everything except work directories into `.\data`.
`ORBITCONVERT_DATA` overrides the data folder (used by tests).
