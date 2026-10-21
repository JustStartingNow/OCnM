# Architecture

```
 slotty --toggle ──► IInstanceChannel (Unix socket / named pipe) ──► CommandSink ──► AppController
 tray / hotkey ───────────────────────────────────────────────────────────────────►      │
                                                                                          ▼
                         DonutWindow ◄─── DonutViewModel ───► TransferService ──► FileTransferEngine (Core)
                         (DonutMenu)          │   ▲                                   copy · move · verify · undo
                                              ▼   │
                                         ConfigService ──► ConfigStore (Core, config.json)
```

## Slotty.Core (no UI dependencies)

- `Models/` — `SlottyConfig` (version, `AppSettings`, exactly 9 `SliceConfig`), enums.
- `Config/ConfigStore` — tolerant reader: each field falls back to its default on its own. Only unparseable JSON counts as
  corrupt; that file is backed up to `.bak`. Writes are atomic.
- `Geometry/DonutGeometry` — angle (clockwise from 12 o'clock) + radius hit-testing; `ScreenMath` clamps a window to a
  work area.
- `FileOps/FileTransferEngine` — runs on the thread pool; reports `TransferProgress`; asks through a `ConflictPrompt`
  delegate; returns per-item results and an `UndoRecord` (created files/dirs with size+mtime, moves, replaced-file
  backups). `PathRules` handles self-nesting (symlink-aware), same-volume detection (mount points), and normalization.
  `ConflictNaming` produces `name (n).ext`.
- `Ipc/` — `CommandLine` parsing, the one-line `slotty/1 <command>` protocol.
- `Platform/` — interfaces the App implements per OS: `IFolderOpener`, `IAutostartService`, `IGlobalHotkeyService`,
  `ICursorLocator`, `ITrayService`, `IInstanceChannel`; `HotkeyGesture` parsing; `SessionDetector`.

## Slotty.App (Avalonia)

- `Program` — parses the CLI, forwards to a running instance, claims the IPC channel, loads the config, and picks the
  backend (`UsePlatformDetect()`, plus `UseWaylandWithFallback()` with app id `slotty` for native Wayland).
- `AppController` — window lifetime, show/hide/toggle, placement at the cursor (only where the platform allows it),
  delayed hide after a drop, tray, hotkey, autostart sync.
- `Controls/DonutMenu` — custom control. It draws the slices as `StreamGeometry` sectors, hit-tests with
  `DonutGeometry`, and handles pointer and drag-and-drop. It raises routed events (`SliceClicked`,
  `SliceContextRequested`, `FilesDropped`, `OutsideClicked`) and knows nothing about files. Slices are `IDonutSlice`.
- `ViewModels/` — `DonutViewModel` (slice actions, drops, toast, overlays), `SliceViewModel`, `SettingsViewModel`.
  Rename and the conflict question are inline overlays, not extra windows, so they behave the same on Wayland.
- `Platform/` — `Windows/` (RegisterHotKey thread, GetCursorPos, HKCU Run), `Linux/` (XCB `XGrabKey` +
  `QueryPointer`, used only in real X11 sessions; XDG autostart), `Mac/` (LaunchAgent), `Ipc/` (Unix socket, named
  pipe), `AvaloniaTray`, `AppIcon` (drawn at runtime).

## Focus and hiding

The donut hides on Esc, an outside click, or focus loss. Focus loss counts only if the window has been active since it
was shown and more than 1 s has passed. That is because Niri focuses a window mapped during a drag and then hands
focus back to the drag source right away. Pinned, busy, or with an overlay or menu open, the donut never hides on
focus loss.
