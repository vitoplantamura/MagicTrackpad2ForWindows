# HapticsLab

HapticsLab tunes how the Magic Trackpad 2 feels as a Windows haptic touchpad. Every value it edits is in the calibration table of [`common/MagicTrackpad2Haptics.h`](../../common/MagicTrackpad2Haptics.h). It reads that table, lets you try other values on the trackpad, keeps your ratings and notes, and writes your picks back to the file.

It is a developer tool, not part of the driver package.

## Steps

The sidebar lists the steps in the order Windows uses the values.

| Step | Windows setting it tunes | What you see |
|---|---|---|
| Start here | | Connection status, how to read the pictures, and an overview of the steps |
| 1 Click force | Settings > Touchpad > **Click sensitivity** (Light / Medium / Heavy) | A force scale with the three levels. Click a level to make it the trackpad's click. |
| 2 Click feedback | Settings > Touchpad > **Haptic click** slider | The click and release at 25 / 50 / 75 / 100 %. Click a step to try it. |
| 3 Haptic signals | The waveforms Windows and apps play (Collide when a window snaps, Step for the slider preview, ...) | One signal at a time: candidates with pictures, picks, compare two signals, and ratings |
| 4 Signal strength | Settings > Touchpad > **Haptic signals** slider | A grid of every signal at every level. Click a cell to play it, or a name to play all four levels rising. |
| 5 Driver check | | Fires the **installed** driver's signals through the same HID reports Windows uses, and compares them with the header |
| 6 Save to header | | Lists your changes and writes the calibration table section, keeping a `.bak` copy. Also copies a feedback report. |

Each step does three things:

- **Tells you what to feel for:** where Windows uses the values, and a checklist of what to pay attention to. Tick a point once it feels right; the sidebar shows how many are done.
- **Lets you rate each value:** for example "Too weak", "Just right" or "Too similar to another".
- **Keeps notes:** in a box that is saved between sessions.

**Reading the pictures:** every pulse is drawn as three bars. They show S0 (strength, and for the click also how hard you press) in blue, S1 (louder and sharper) in orange, and S2 (louder and deeper) in green. Chained pulses are drawn the gap apart.

**Replaying:** F5, or Space when no text box or button has the focus, replays the last thing you played.

**Sharing what you felt:** on step 6, "Copy feedback report" puts a Markdown report on the clipboard; "Save report as file" writes it to a `.md` file. The report holds:

- **Your PC:** the Windows version and build, whether Windows has the haptic touchpad settings, the trackpad model and connection, and the installed driver version.
- **Your feedback:** changed values, ratings, ticks and notes.
- **The working calibration table.**

Paste it into an issue or pull request.

## Testing a pull request

You don't need to change any values for this. Try what the driver does now and say how it feels; the report collects the rest.

1. **Install the driver from the pull request.** USB is enough to start; Bluetooth too if you can.
2. **Get HapticsLab.** Either:
   - Download the `HapticsLab` artifact from the pull request's Build workflow run (`x64` or `arm64`). It needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
   - Or build it from the checkout: `dotnet build tools\HapticsLab -c Release`.
3. **Run `HapticsLab.exe`** and accept the administrator prompt. On **Start here**, the first two connection lines should be green.
4. **Go through steps 1 to 5.**
   - On each step, read what to feel for, try the values, and tick what feels right.
   - Rate anything that doesn't feel right, and use the notes box for anything else.
   - On step 5, every row should say "same". Fire each signal there; this is exactly what Windows does.
5. **Try Windows itself.** In Settings > Bluetooth & devices > Touchpad, move **Click sensitivity**, **Haptic click** and **Haptic signals**, and click the pad after each change. Snap a window to an edge: you should feel a short tap.
6. **Share the report.** On step 6, press **Copy feedback report** and paste it as a comment on the pull request. Don't press "Write to header".

Closing HapticsLab puts back any click setting it changed for a test.

## Running it

```
dotnet build tools\HapticsLab -c Release
tools\HapticsLab\bin\Release\net8.0-windows\HapticsLab.exe
```

- **Elevation:** it needs administrator rights and asks for them at start. It uses the drivers' admin-only control interfaces: the USB driver's pipe or the Bluetooth filter's control device.
- **Trackpad connection:** USB or Bluetooth, with this project's driver.
- **Locating the header:** it finds `common\MagicTrackpad2Haptics.h` by walking up from its own folder. Outside a source checkout, such as a downloaded build, it uses the copy built into the app. Everything works then except writing to the header.

## Safety

- **Pulse limits:** pulses are limited to S1 ≤ 0x20 and S2 ≤ 0x40, the hardware limits the drivers also enforce.
- **Click tests change your settings temporarily.** Steps 1 and 2 change the pad's own click by writing `FeedbackClick`, `FeedbackRelease` and `OverrideWindowsHaptics` to the drivers' registry keys, then reloading the drivers.
- **Restoring:** the previous values are saved first. They are put back by "Stop testing and restore my click", by the red button in the status bar, and when you close the app. If the app crashes, it offers to restore them at the next start.
- **Session file:** `%LOCALAPPDATA%\HapticsLab\session.json` holds your working values, candidates, picks, ratings, ticks and notes.

## Checking without hardware

```
dotnet tools\HapticsLab\bin\Release\net8.0-windows\HapticsLab.dll --selftest common\MagicTrackpad2Haptics.h
```

This parses the table, regenerates the section, and reports whether the result is byte-identical. It also prints a few mapped values to compare with the drivers.

`HapticsLab.exe --screenshots <folder>` saves every page as a PNG, plus the feedback report, and exits. This is useful for checking the layout. It touches no settings and doesn't save the session. Running it unelevated needs a build without the manifest: `-p:ApplicationManifest=`.
