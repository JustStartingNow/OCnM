# OCnM: Open, Copy 'n' Move

Drop files onto a donut slice to copy or move them into a folder. 9 slices, 9 folders.

OCnM (`ocnm`) is a small desktop app whose whole UI is a **donut with 9 slices**. You give each slice a folder. The
name says what a slice does:

- **Open**: click a slice (or press **1–9**) and its folder opens in your file manager.
- **Copy**: drop files or folders on a slice and they are copied into its folder, with progress and an Undo button.
- **Move**: hold **Shift** while dropping, or set a slice's drop action to *Move*, and they are moved instead.

Inspired by the donut UX of [Filee](https://github.com/KnifeLemon/Filee) (MIT). OCnM is original code and does not
convert files. No network access, no telemetry, no auto-update.

Built with C# / .NET 10 and Avalonia 12.1. Primary targets are Windows 10/11 and Linux on Wayland, with
Niri as a first-class compositor.

## Using it

| Action | How |
|---|---|
| Assign a folder | Click an empty slice (folder picker), or drop a folder from the file manager onto an empty slice, or right-click → *Set folder…* |
| Copy / move files | Drop files and folders onto an assigned slice. Default is **Copy**. Hold **Shift** while dropping to move, **Ctrl** to copy |
| Open a folder | Click the slice, or press **1–9** |
| Keyboard | **Arrow keys** move a focus ring between slices, **Enter/Space** opens, **Menu** or **Shift+F10** shows the slice menu, **Tab** reaches the buttons |
| Configure a slice | Right-click: *Change folder, Rename, Change color, Change icon, Drop action (default/copy/move), Open folder, Clear slice* |
| Undo the last drop | **Undo** in the toast below the donut (deletes the copies it made, or moves items back). Toasts dismiss themselves after 5–9 s and stay while the pointer is on them |
| Move the window | Drag the center hub (Windows/X11; on Niri use Mod+drag) |
| Keep it open while dragging | The **pin** button (top-left). Needed on Wayland if you click another window *before* dragging |
| Hide | **Esc**, click outside the ring, focus loss (when not pinned), or automatically a few seconds after a successful drop |
| Settings | The sliders button (top-right), `ocnm --settings`, or the tray menu |

Slice 1 starts at 12 o'clock; the rest follow clockwise. A slice whose folder disappeared turns amber with a warning
icon. Click it or drop a folder on it to re-assign it.

### Safety rules for drops

- Existing files are **never overwritten silently**. The default conflict policy renames the new item to `name (1).ext`.
  The other policies are *Skip* and *Ask* (Keep both / Replace / Skip / Cancel, optionally for all). *Replace* keeps the
  old file as a hidden backup until the operation can no longer be undone, so Undo can bring it back.
- A folder can't be dropped into itself or one of its subfolders (symlinks are resolved too).
- Files are written to a hidden `.name.xxxx.ocnm-partial` file first and renamed into place when complete, so an
  interrupted copy never leaves a truncated file under the real name.
- **Move** on the same drive is a rename. **Move across drives** is copy → flush → SHA-256 verify → delete the source.
  If any part of a folder fails to copy, nothing is deleted and the incomplete copy is removed.
- OCnM always reports a *copy* to the drag source, so the file manager never deletes anything itself.
- Undo keeps files you edited after the drop and reports them instead of deleting them.

## Install

OCnM has no download page or installer yet. You build a self-contained binary once with the .NET SDK. It includes
the .NET runtime, so the machine that *runs* it needs nothing else. You can build every platform's binary from any
OS; for example, build the Windows `.exe` on Linux and copy it over.

### Step 1: build the binary (any OS)

Install the **.NET 10 SDK**:

| OS | Command |
|---|---|
| Arch / CachyOS / Manjaro | `sudo pacman -S dotnet-sdk` |
| Fedora | `sudo dnf install dotnet-sdk-10.0` |
| Ubuntu / Debian | see [Microsoft's instructions](https://learn.microsoft.com/dotnet/core/install/linux) (`dotnet-sdk-10.0`) |
| Windows | `winget install Microsoft.DotNet.SDK.10` |
| macOS | `brew install --cask dotnet-sdk` |

Then, in the project folder, publish for the target you need:

```bash
dotnet publish src/OCnM.App -c Release -r <RID> --self-contained -p:PublishSingleFile=true -o publish/<RID>
```

| Target | `<RID>` | Output |
|---|---|---|
| Linux, Intel/AMD | `linux-x64` | `publish/linux-x64/ocnm` |
| Linux, ARM | `linux-arm64` | `publish/linux-arm64/ocnm` |
| Windows, Intel/AMD | `win-x64` | `publish/win-x64/ocnm.exe` |
| Windows, ARM | `win-arm64` | `publish/win-arm64/ocnm.exe` |
| macOS, Apple Silicon | `osx-arm64` | `publish/osx-arm64/ocnm` |
| macOS, Intel | `osx-x64` | `publish/osx-x64/ocnm` |

Each binary is a single file of about 50 MB. All six targets build. Only `linux-x64` has been run so far
(see *Platform support*).

### Linux

**1. Install for your user** (no root needed):

```bash
install -Dm755 publish/linux-x64/ocnm ~/.local/bin/ocnm
install -Dm644 packaging/linux/ocnm.desktop ~/.local/share/applications/ocnm.desktop
install -Dm644 packaging/ocnm.svg ~/.local/share/icons/hicolor/scalable/apps/ocnm.svg
update-desktop-database ~/.local/share/applications 2>/dev/null || true
```

`~/.local/bin` must be on your `PATH`. Check with `which ocnm`. If it isn't:
- fish: `fish_add_path ~/.local/bin`
- bash/zsh: add `export PATH="$HOME/.local/bin:$PATH"` to `~/.bashrc` / `~/.zshrc`

OCnM then appears in your app launcher as **OCnM**.

**2. Runtime requirements.** Normally all of these are already installed on a desktop:
- `xdg-utils` (`xdg-open`, used to open folders in your file manager)
- `xdg-desktop-portal` plus a backend (`-kde`, `-gnome`, `-gtk`, …), used by the folder picker
- `libicu` and `fontconfig`
- on X11 only: `libxcb`, used by the global hotkey

**3. Open it with a key.** This depends on your desktop:

| Desktop | How |
|---|---|
| **Niri** | Add the keybind and window rule from the [Niri section](#niri) (`spawn "ocnm" "--toggle"`) |
| **Hyprland** | `bind = SUPER ALT, S, exec, ocnm --toggle` and `windowrulev2 = float, class:^(ocnm)$` |
| **Sway** | `bindsym $mod+Alt+s exec ocnm --toggle` and `for_window [app_id="ocnm"] floating enable` |
| **KDE Plasma (Wayland)** | System Settings → Keyboard → Shortcuts → *Add New* → *Command or Script* → `ocnm --toggle` |
| **GNOME (Wayland)** | Settings → Keyboard → View and Customize Shortcuts → Custom Shortcuts → `ocnm --toggle` |
| **Any X11 session** | Nothing to do: the built-in global hotkey **Ctrl+Alt+Space** works, and so does the tray icon |

The compositor snippets show the usual syntax. Window-rule syntax in particular changes between versions, so check
your compositor's wiki. On Wayland, check first that the key isn't already bound to something else. On Niri, for example, the default config
binds `Super+Alt+S` to the Orca screen reader.

**4. Start at login.** Pick one:
- Niri: `spawn-at-startup "ocnm" "--hide"` in `config.kdl`
- Hyprland: `exec-once = ocnm --hide`. Sway: `exec ocnm --hide`
- KDE / GNOME / XFCE: tick **Settings → Start at login** in OCnM. This writes `~/.config/autostart/ocnm.desktop`.

Avoid starting `ocnm` from a terminal you later close, because closing the terminal ends OCnM. Use the keybind or
the launcher instead.

**Uninstall:**

```bash
ocnm --quit
rm ~/.local/bin/ocnm ~/.local/share/applications/ocnm.desktop \
   ~/.local/share/icons/hicolor/scalable/apps/ocnm.svg ~/.config/autostart/ocnm.desktop
rm -r ~/.config/ocnm        # settings and log (optional)
```

Also remove any keybind / `spawn-at-startup` lines you added to your compositor config.

### Windows 10 / 11

**1. Install for your user.** Run in PowerShell from the project folder, or copy `ocnm.exe` from a Linux build first:

```powershell
$dir = "$env:LOCALAPPDATA\Programs\OCnM"
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item .\publish\win-x64\ocnm.exe $dir

# Start Menu shortcut
$lnk = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\OCnM.lnk")
$lnk.TargetPath = "$dir\ocnm.exe"
$lnk.Save()

# Optional: make `ocnm --toggle` etc. work in any terminal (open a new terminal afterwards)
[Environment]::SetEnvironmentVariable("Path", [Environment]::GetEnvironmentVariable("Path", "User") + ";$dir", "User")
```

**2. First start.** Open **OCnM** from the Start Menu. The binary isn't code-signed, so SmartScreen may say *"Windows
protected your PC"*. Click **More info → Run anyway**. You only need to do this once.

**3. Use it.**
- **Ctrl+Alt+Space** (change it in Settings) shows the donut at the mouse cursor.
- The **tray icon**: left-click toggles the donut. Right-click gives *Settings*, *Start at login* and *Quit*. Windows
  may hide new tray icons under the **^** arrow; drag it onto the taskbar to keep it visible.
- **Start at login**: tick it in the tray menu or in Settings. This adds `HKCU\…\CurrentVersion\Run\OCnM`.

**Uninstall:** untick *Start at login*, then *Quit* from the tray. Then:

```powershell
Remove-Item -Recurse "$env:LOCALAPPDATA\Programs\OCnM"
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\OCnM.lnk"
Remove-Item -Recurse "$env:APPDATA\OCnM"        # settings and log (optional)
```

If you added the folder to `Path`, remove it under *Settings → System → About → Advanced system settings →
Environment Variables*.

### macOS (best effort, not tested yet)

There is no `.app` bundle yet; OCnM runs as a command-line-launched binary.

```bash
# Apple Silicon: osx-arm64 · Intel: osx-x64
dotnet publish src/OCnM.App -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/osx-arm64

sudo install -m755 publish/osx-arm64/ocnm /usr/local/bin/ocnm
# A binary built on another machine is unsigned: sign it ad hoc and clear the quarantine flag.
sudo codesign --force --sign - /usr/local/bin/ocnm
sudo xattr -d com.apple.quarantine /usr/local/bin/ocnm 2>/dev/null || true

ocnm --hide &      # starts in the background; then:
ocnm --toggle
```

- To get a global key, bind `ocnm --toggle` with a launcher (Raycast, Alfred, Hammerspoon, BetterTouchTool).
  OCnM's own global hotkey is not implemented on macOS.
- **Start at login** in Settings writes `~/Library/LaunchAgents/app.ocnm.plist`.
- To uninstall: `ocnm --quit`, then remove `/usr/local/bin/ocnm`, `~/Library/LaunchAgents/app.ocnm.plist` and
  `~/Library/Application Support/OCnM`.

### Updating

Quit the running copy (`ocnm --quit`, or *Quit* in the tray), replace the binary with a newly published one, and
start it again. Settings live in the config file (see below) and are kept.

### Upgrading from Slotty

OCnM used to be called **Slotty**. On its first start, OCnM copies your old settings folder (`~/.config/slotty`,
`%APPDATA%\Slotty` or `~/Library/Application Support/Slotty`) to the new one, unless the new folder already exists.
The old folder is left untouched; delete it yourself once you're happy. A *Start at login* entry made by Slotty is
replaced by an OCnM one. `SLOTTY_*` environment variables are still read when the `OCNM_*` ones are not set.

Things OCnM can't change for you:
- compositor keybinds and window rules that run `slotty` or match `app-id="slotty"` / `title="Slotty"`
  (use `ocnm` and `OCnM`; see the [Niri section](#niri))
- the old binary (`~/.local/bin/slotty`, `slotty.exe`), the Start Menu shortcut and desktop entries named Slotty

### Develop

```bash
dotnet build            # warnings are errors (Directory.Build.props)
dotnet test
dotnet run --project src/OCnM.App
```

The project file turns on native-library self-extraction, compression and no symbols for single-file publishes.

## Command line

OCnM is single-instance. A second launch forwards its command to the running instance over a Unix domain socket
(`$XDG_RUNTIME_DIR/ocnm.sock`, mode 0600) or a per-user named pipe on Windows, then exits. If nothing is running,
the first launch starts the app.

```
ocnm               start (or show the running donut)
ocnm --toggle      show / hide the donut
ocnm --show
ocnm --hide        (when starting: start hidden)
ocnm --settings
ocnm --quit
ocnm --backend x11|wayland     Linux backend for this start (also OCNM_BACKEND=…)
```

Exit codes: 0 = OK, 1 = an instance exists but isn't answering, 2 = bad arguments.

## Platform support

| | Windows 10/11 | Linux, Wayland (Niri) | Linux, X11 session | macOS |
|---|---|---|---|---|
| Donut, assign, drop copy/move, undo | ✔ | ✔ | ✔ | best effort |
| Transparent borderless window | ✔ | ✔ (solid fallback if not granted) | ✔ with a compositor | best effort |
| Open at cursor | ✔ (tray + hotkey) | ✘ by design: the compositor places it | ✔ | centered |
| Global hotkey (default Ctrl+Alt+Space) | ✔ `RegisterHotKey` | ✘ use a compositor keybind → `ocnm --toggle` | ✔ `XGrabKey` (XCB) | ✘ |
| Tray icon | ✔ | ✔ if a StatusNotifierItem host runs | ✔ if a tray host runs | best effort |
| Always on top | ✔ | ✘ (use a window rule) | ✔ | best effort |
| Start at login | ✔ HKCU `Run` key | `~/.config/autostart/ocnm.desktop` (see Niri note) | same | LaunchAgent |
| Single instance / CLI | ✔ named pipe | ✔ Unix socket | ✔ Unix socket | ✔ Unix socket |

**What was tested.** Everything Linux/Wayland was exercised on CachyOS + Niri 26.04 with Dolphin: both backends,
transparency, IPC commands, keys 1–9 and Esc, context menu, color, rename, drop-to-assign, copy, auto-rename,
the *Ask* prompt with Replace, Undo, the portal folder picker, tray registration with Noctalia, and the published
single-file binary. The Windows code (Win32 hotkey, cursor, registry autostart, named pipe) compiles and
cross-publishes, and the named-pipe channel shares tests with the socket channel, but it **has not been run on
Windows** yet. The same goes for the X11-session hotkey and cursor placement, and for macOS.

## Linux backends: native Wayland vs X11

Avalonia 12.1 ships an experimental native Wayland backend (`Avalonia.Wayland`). It isn't selected automatically, so
OCnM selects it itself:

| Setting | Backend |
|---|---|
| `--backend wayland`, `OCNM_BACKEND=wayland`, or Settings → Linux backend → Native Wayland | native Wayland (falls back to X11 if no compositor answers) |
| `--backend x11` / `OCNM_BACKEND=x11` / Settings → X11 | X11 (XWayland on a Wayland desktop) |
| Automatic (default) | native Wayland inside a Wayland session, X11 otherwise |

Precedence: `--backend` > `OCNM_BACKEND` > Settings. Both backends set the app id / WM class to `ocnm`.

**Which works better for drag-and-drop on Niri?** The **native Wayland backend**. Tested on Niri 26.04 + xwayland-satellite,
dragging a file from Dolphin:

| Backend | Drop from Dolphin | Notes |
|---|---|---|
| Native Wayland | **works** | drag-over highlight, path in the hub, copy, undo |
| X11 via xwayland-satellite | **doesn't work** | the window never receives the drag from native Wayland apps |

So keep the default (Automatic) on Niri. X11 can still be useful if the experimental backend misbehaves on another
compositor, or with an X11 file manager.

## Niri

On Wayland a client can't read the global cursor, place its own windows, grab global keys or stay on top. On Niri,
OCnM is therefore a normal **fixed-size floating window** (440×500) with app id `ocnm`, placed by the compositor and
opened with a keybind that runs `ocnm --toggle`.

Add to `~/.config/niri/config.kdl`. This snippet was checked with `niri validate` on Niri 26.04; **check the
[Niri wiki](https://github.com/YaLTeR/niri/wiki) for the exact syntax of your Niri version**:

```kdl
// Start OCnM hidden at login (XDG autostart is not run by every Niri setup).
spawn-at-startup "ocnm" "--hide"

binds {
    // Pick any free key; Mod+S is just an example.
    Mod+S hotkey-overlay-title="OCnM donut" { spawn "ocnm" "--toggle"; }
}

// The donut: floating, focused, no border, focus ring or shadow, top-center of the screen.
window-rule {
    match app-id="^ocnm$" title="^OCnM$"
    open-floating true
    open-focused true
    default-floating-position x=0 y=120 relative-to="top"
    border { off; }
    focus-ring { off; }
    shadow { off; }
    draw-border-with-background false
}

// Optional: Settings as a floating window too.
window-rule {
    match app-id="^ocnm$" title="^OCnM Settings$"
    open-floating true
    default-column-width { fixed 980; }
}
```

Niri already floats the donut without a rule (it has a fixed size), but the rule removes the border, focus ring
and shadow around the transparent window and sets a position.

**Dragging files from the file manager on Niri** — two ways, both tested:

1. Start dragging a file in the file manager, press your keybind while still holding the mouse button, and drop on
   a slice. The donut ignores the brief focus change Niri makes while a drag is active.
2. Or open the donut first and click the **pin** (top-left), then drag from the file manager. Without the pin, the
   click into the file manager counts as focus loss and hides the donut. The pin state is remembered.

Other Niri notes:
- The global-hotkey setting is disabled on Wayland. Use the keybind above.
- The tray icon appears if your bar hosts StatusNotifierItems (Waybar, Noctalia, …). The tray is never required.
- To start at login, prefer `spawn-at-startup`. OCnM's *Start at login* checkbox writes an XDG autostart entry,
  which only runs if your session runs the systemd XDG autostart generator.

## Windows

- Left-click the tray icon to toggle the donut at the cursor. Right-click for *Show / hide*, *Settings…*,
  *Start at login*, *Quit*.
- The global hotkey (default **Ctrl+Alt+Space**, configurable in Settings) shows the donut at the cursor, clamped to the
  monitor's work area.
- The app manifest enables long paths and per-monitor DPI awareness.

## Configuration file

A single JSON file:

| OS | Path |
|---|---|
| Linux | `$XDG_CONFIG_HOME/ocnm/config.json` (usually `~/.config/ocnm/config.json`) |
| Windows | `%APPDATA%\OCnM\config.json` |
| macOS | `~/Library/Application Support/OCnM/config.json` |

`OCNM_CONFIG_DIR` overrides the directory. A small log (`ocnm.log`, rotated at 512 KB) sits next to it.

```json
{
  "version": 1,
  "settings": {
    "defaultAction": "copy",
    "conflictPolicy": "autoRename",
    "hotkey": "Ctrl+Alt+Space",
    "theme": "system",
    "launchAtLogin": false,
    "hideOnFocusLoss": true,
    "hideAfterDropMs": 4000,
    "pinned": false,
    "showTrayIcon": true,
    "reduceMotion": false,
    "linuxBackend": "auto"
  },
  "slices": [
    { "index": 0, "label": null, "path": "/home/me/Pictures", "color": "#2F80ED", "icon": "images", "action": "inherit" }
  ]
}
```

- There are always exactly 9 slices. Missing ones are added empty, extras are ignored.
- Unknown fields are ignored and invalid values fall back to their defaults. Comments and trailing commas are accepted.
- A file that isn't valid JSON is copied to `config.json.bak` and recreated with defaults. The app shows a notice
  and never crashes on it.
- Writes are atomic (temp file + rename).
- Values: `defaultAction` copy|move · `conflictPolicy` autoRename|skip|ask · `theme` system|light|dark ·
  `linuxBackend` auto|x11|wayland · slice `action` inherit|copy|move · `icon` folder, documents, images, music, videos,
  downloads, archive, code, star, heart, home, work.

## Look and feel

- The donut opens with a short scale-and-fade (190 ms, decelerating) and closes faster (120 ms). Hover, drag and focus
  highlights blend over 120 ms. The slice under a drag lifts outwards and the hub shows *Copy to* / *Move to* with the path.
- **Reduce animations** (Settings → Accessibility, `"reduceMotion": true`) makes every change instant. OCnM also follows
  the OS setting where it can read it: Windows "Show animations", KDE `AnimationDurationFactor=0`, GNOME
  `enable-animations=false`.
- Text uses the bundled Inter font, so it looks the same on every platform. Colors are semantic theme tokens
  (`App.axaml`), with normal text pairs at ≥ 4.5:1 contrast in both themes. Icons are one stroked vector family, and
  status toasts pair an icon with the color.

## Environment variables

The old `SLOTTY_*` names still work as a fallback.

| Variable | Effect |
|---|---|
| `OCNM_BACKEND` | `x11` or `wayland` (Linux) |
| `OCNM_CONFIG_DIR` | Use another config directory |
| `OCNM_SOLID=1` | Force the solid rounded background instead of transparency |

## Project layout

```
OCnM.sln
src/OCnM.Core/    models, config store, transfer engine + undo, donut geometry, CLI/IPC contracts, platform interfaces
src/OCnM.App/     Avalonia app: DonutMenu control, windows, view models, per-OS platform implementations
tests/OCnM.Tests/ xUnit: hit-testing, config, conflicts, copy/move/undo, nesting, IPC, hotkey parsing, …
packaging/          .desktop file and SVG icon
docs/ARCHITECTURE.md
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together.

## Troubleshooting

- **Nothing happens on `ocnm --toggle`.** Check `~/.config/ocnm/ocnm.log`. A stale socket from a crash is
  replaced automatically on the next start.
- **The donut has a border or shadow on Niri.** Add the window rule above.
- **Black corners instead of transparency.** The compositor didn't grant a transparent surface. OCnM switches to a solid
  rounded background on its own; `OCNM_SOLID=1` forces it.
- **Drops don't arrive on Linux.** Make sure the native Wayland backend is in use (Settings shows the backend in the
  footer), or start with `--backend wayland`.

## License

MIT. See [LICENSE](LICENSE).
