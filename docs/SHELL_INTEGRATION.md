# Shell integration

Windows does **not** offer a supported way for an application to intercept or redirect drag operations
that start in File Explorer, and OrbitConvert does not inject code into Explorer. It therefore provides
four activation modes built on documented mechanisms.

| Mode | Mechanism | Default | Notes |
|---|---|---|---|
| A. Drop zone | A top-most WPF window that is an ordinary OLE drop target | Off | Reliable: Explorer performs a normal drag-and-drop onto a window. Drag the circle to move it |
| B. Global shortcut | `RegisterHotKey` + `Shell.Application` automation to read the selection of the foreground Explorer window | On (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>O</kbd>) | Works for File Explorer windows (including Windows 11 tabs); not for the desktop or third-party file managers |
| C. Context menu | Static verb under `HKCU\Software\Classes\*\shell\OrbitConvert` + a **Send to** shortcut | On (installer feature) | No admin rights; nothing loaded into Explorer |
| D. Shake while dragging | Low-level mouse hook (`WH_MOUSE_LL`) in OrbitConvert's own process | Off, experimental | See below |

## A. Drop zone

`DropZoneWindow` accepts `DataFormats.FileDrop` with `DragDropEffects.Copy` only, so Explorer never
moves or deletes the originals. On drop, the radial menu opens at the drop point. Position is saved
per user and clamped to the virtual screen if a monitor is removed.

## B. Global shortcut

- Registered with `RegisterHotKey` (with `MOD_NOREPEAT`) on a message-only window. No keyboard hook is
  installed. If another application owns the combination, Settings shows the conflict.
- On the hotkey, OrbitConvert asks `Shell.Application.Windows()` for the window whose `HWND` matches the
  foreground Explorer window and reads `Document.SelectedItems()`. On Windows 11, several tabs share one
  window handle; the active tab is matched through `IServiceProvider` → `IShellBrowser::GetWindow` against
  the top `ShellTabWindowClass` child.
- The query runs on a dedicated STA thread with a 3-second timeout, so a busy Explorer cannot freeze
  OrbitConvert. Folders and virtual items (Control Panel, libraries) are ignored.

## C. Context menu and Send To

Registry layout (per user):

```
HKCU\Software\Classes\*\shell\OrbitConvert
    (Default)        = Convert with OrbitConvert
    MUIVerb          = Convert with OrbitConvert
    Icon             = "C:\...\OrbitConvert.exe",0
    MultiSelectModel = Player
    command\(Default) = "C:\...\OrbitConvert.exe" --radial "%1"
```

- For a static verb, Explorer starts one process per selected file. `MultiSelectModel=Player` removes the
  15-item limit. Each process forwards its file to the running instance over a per-user named pipe; the
  running instance waits 350 ms for more requests and opens **one** radial menu for all of them.
- **Send to → OrbitConvert** passes all selected files in a single command line.
- **Windows 11**: classic verbs appear under *Show more options*. A top-level entry in the compact menu
  requires an `IExplorerCommand` handler shipped in a **signed** MSIX or sparse package. That is on the
  roadmap; it needs a code-signing certificate.
- The MSI writes these keys (and removes them on uninstall). The Settings page can add or remove them
  too, which is useful for the portable build. `OrbitConvert.exe --register-shell` / `--unregister-shell`
  do the same from a script.

## D. Shake while dragging (experimental)

When enabled, `DragShakeDetector` installs a low-level mouse hook. Such hooks are called in
OrbitConvert's own process; no DLL is injected into Explorer or any other process. While the left
button is down, four or more left–right direction changes of at least 40 px within 0.9 s show a
temporary drop target under the cursor. It closes when the button is released elsewhere.

Limitations, stated plainly:

- Windows does not tell other applications that a *file* drag is in progress, so any left-button drag
  with a shake shows the target. It ignores drops that are not files.
- Windows removes low-level hooks that respond slowly; the callback only records coordinates.
- Some security software flags applications that install mouse hooks. That is why this mode is off by
  default and labelled experimental.

## Radial menu placement

- The window is positioned in **physical pixels** with `SetWindowPos`, centered on the cursor and
  clamped to the **work area of the monitor under the cursor**, using that monitor's effective DPI
  (`GetDpiForMonitor`). The app manifest declares Per-Monitor V2 DPI awareness; on `DpiChanged` the menu
  re-centers itself.
- Foreground rules: a process that was just launched by Explorer may take the foreground. When it
  forwards a request to the running instance, it calls `AllowSetForegroundWindow` for that instance so
  the menu can receive keyboard focus. If Windows still refuses activation (for example after a drop),
  the menu remains usable with the mouse and still closes on an outside click.

## Manual validation checklist

Automated tests cannot drive Explorer, real drags or multiple physical monitors. Before a release,
check these by hand and note the Windows version, display configuration and result in the release notes.

**Context menu (C)**
- [ ] After installing, right-click one file → *Convert with OrbitConvert* (Windows 11: *Show more options*). The radial menu opens at the cursor.
- [ ] Select 5 and 20 files of the same type → one radial menu showing “5 files” / “20 files”.
- [ ] Select mixed images and videos → only shared targets (e.g. ZIP) appear.
- [ ] *Send to → OrbitConvert* with several files → one radial menu.
- [ ] Uninstall → the entries disappear.

**Shortcut (B)**
- [ ] Select files in an Explorer window and press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>O</kbd> → radial menu for those files.
- [ ] Windows 11 with two Explorer tabs → the active tab's selection is used.
- [ ] Nothing selected / desktop focused → a notification explains what to do.
- [ ] Assign a shortcut used by another app → Settings reports the conflict.

**Drop zone (A)**
- [ ] Enable it in Settings; drag files from Explorer onto it → highlight on hover, radial menu on drop, originals untouched.
- [ ] Drag the zone to another monitor, restart OrbitConvert → it reappears there.

**Radial menu**
- [ ] 100 %, 150 % and 200 % scaling: crisp rendering, centered on the cursor.
- [ ] Two monitors with different scaling: open near each screen edge on both monitors → fully visible, correct size.
- [ ] Keyboard: arrows/Tab move, Enter converts, 1–9 pick, Esc closes. Narrator reads item names.
- [ ] Click outside → closes; a running conversion continues and ends with a notification.

**Shake (D, experimental)**
- [ ] Enable it; drag a file and shake → drop target appears; drop → radial menu; release elsewhere → target disappears.

**Themes**
- [ ] Switch Windows between light and dark with *Use Windows setting* → the app follows.
