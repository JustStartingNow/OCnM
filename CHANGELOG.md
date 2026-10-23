# Changelog

## 1.2.0

- **Folders open in a tab instead of a new window** when a file manager window is already open:
  - **Linux + Dolphin:** the folder opens as a new tab in the Dolphin window you used last (or switches to its tab
    if it's already open), and that window is brought to the front (on Niri through Niri's IPC).
  - **Windows 11:** folders are opened through the shell, so File Explorer's *"Open desktop folders and external
    folder links in new tab"* option applies. Windows 10 has no tabs and still opens a window.
  - Other file managers, macOS, or no open window: a new window opens as before.
  - Turn it off in *Settings → Window and startup → Opening folders*.

## 1.1.0

- **Custom slice colors.** A color picker with a hex box and Hue / Saturation / Brightness sliders, next to the
  existing palette. Available in Settings and as *Change color → Custom color…* in the donut's right-click menu.
- **Smoother reordering in Settings.** The dragged row lifts (shadow, accent outline) and the other rows slide into
  place; the up/down buttons animate the same way. *Reduce animations* turns this off.
- **No more gray flash** when moving the mouse over the slice rows in Settings.
- **Save keeps Settings open**, so you can save several changes in a row. The *Cancel* button is now *Close*.
- Settings is wider at its minimum size (900 px), so the Folder column and its Browse button no longer overlap.

## 1.0.0

First release as **OCnM: Open, Copy 'n' Move** (formerly Slotty).
