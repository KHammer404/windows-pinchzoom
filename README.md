# Windows PinchZoom

**KDE-style desktop zoom for Windows 11.** Hold <kbd>Alt</kbd> and pinch on a supported touchpad, then move the pointer to look around. Zoom up to **100×**, with a pixel grid from **15×**.

[**Download the portable x64 ZIP**](https://github.com/KHammer404/windows-pinchzoom/releases/download/v0.1.1/Windows-PinchZoom-v0.1.1-win-x64.zip) · [14-second demo (MP4)](https://github.com/KHammer404/windows-pinchzoom/releases/download/v0.1.0/Windows-PinchZoom-demo.mp4) · [Report compatibility](https://github.com/KHammer404/windows-pinchzoom/issues/new?template=compatibility.yml)

![The desktop zooms in and pans at 8.5×, then zooms to 100× and pans with the pixel grid visible before returning to normal.](docs/demo.gif)

*Recorded CF-SV1 pinch input replayed on a sample desktop.*

## Install and run

The [v0.1.1 pre-release](https://github.com/KHammer404/windows-pinchzoom/releases/tag/v0.1.1) is a portable app for **Windows 11 x64**. It uses your existing drivers.

1. Download [Windows-PinchZoom-v0.1.1-win-x64.zip](https://github.com/KHammer404/windows-pinchzoom/releases/download/v0.1.1/Windows-PinchZoom-v0.1.1-win-x64.zip).
2. In File Explorer, right-click the ZIP and choose **Extract All**. Choose a folder and click **Extract**.
3. Quit any desktop magnifier that is already running. Open the extracted folder and double-click **Windows-PinchZoom.exe**. The app runs in the background.

To remove the app, press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F12</kbd> to exit, then delete the extracted folder.

## Controls

| Action | Control |
| --- | --- |
| Zoom around the pointer | Hold <kbd>Alt</kbd> and spread or pinch two fingers on a supported touchpad. |
| Look around while zoomed in | Finish the pinch, then move the pointer. |
| Zoom with the wheel | Hold <kbd>Alt</kbd> and scroll in standard mode. |
| Zoom with the keyboard | In standard mode, hold <kbd>Ctrl</kbd>+<kbd>Alt</kbd> and press the <kbd>=</kbd> or <kbd>-</kbd> key. |
| Reset to 100% | Press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>0</kbd> in standard mode. |
| Reset and exit | Press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F12</kbd>. |

The app selects the Synaptics input path automatically on the supported CF-SV1. If the browser zooms during the gesture, disable **Pinch Zoom** in Synaptics settings. To use wheel or keyboard controls on the CF-SV1, exit the app and start it with `--standard`. Use `--wheel` to skip Precision Touchpad input and keep the wheel and keyboard controls.

## Compatibility

| Setup | Status |
| --- | --- |
| Panasonic Let's note CF-SV1RDLVS · Synaptics `SYNA0102`, driver `19.5.26.24` · Windows 11 x64 · one active display | Two-finger pinch tested on one laptop. |
| Standard mode · Windows 11 x64 · one active display | Mouse wheel and keyboard zoom handlers verified with simulated input on the test laptop. |
| Windows Precision Touchpad | Experimental. Pinch depends on which contact reports the device exposes. |
| Other legacy touchpads, including other `SYNA0102` models | Untested. The report format still needs to be verified. |
| Multiple displays | Experimental in standard mode. Zoom targets the display under the pointer. |

Trying a different laptop? [Share your model and test result](https://github.com/KHammer404/windows-pinchzoom/issues/new?template=compatibility.yml) so other users can check whether their setup is likely to work.

On a single display, the app uses native Windows magnification at low zoom. The capture renderer draws the high-zoom view and pixel grid in one click-through window. Captured frames stay in memory. Protected video may appear blank.

## Build from source

Run `build.cmd` on Windows x64. It uses the installed .NET Framework compiler and writes the portable executable to `dist`. Run `build.cmd console` to include console diagnostics.
