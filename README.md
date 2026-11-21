# Windows PinchZoom

A Windows desktop magnifier controlled by touchpad pinch, mouse wheel, or keyboard. Magnification follows the pointer. Zoom can reach 100×, with a pixel grid at 15× and above.

## Download and use

Download the ZIP from [Releases](../../releases), extract it, and run **Windows-PinchZoom.exe**. The app runs in the background. It does not install a driver or start automatically.

| Input | What to do |
| --- | --- |
| Tested CF-SV1 Synaptics touchpad | Hold **Alt** and spread or pinch two fingers. Disable **Pinch Zoom** in Synaptics settings if the browser zooms independently. |
| Windows Precision Touchpad | Try **Alt** + two-finger pinch. This is experimental and needs testing on more hardware. |
| Mouse wheel | Hold **Alt** and scroll. Some touchpads also emit compatible scroll events. |
| Keyboard | **Ctrl+Alt+Plus/Minus** magnifies or reduces the desktop in standard mode. **Ctrl+Alt+0** resets to 100% in standard mode. |
| Exit | **Ctrl+Shift+F12** returns the desktop to 100% and closes the app. |

On the supported Synaptics device, the app selects the device-specific input path automatically. Run **Windows-PinchZoom.exe --standard** to test the standard input path on the same device, or use **--wheel** to skip Precision Touchpad input. Exit the current copy before changing modes.

## Compatibility

- Built for Windows 11 x64. The continuous Synaptics pinch has been used on a Panasonic CF-SV1, device ID **SYNA0102**, with the Synaptics RMI HID driver. Other devices with the same ID still need their report format verified.
- The standard mode does not require a particular touchpad. Windows Precision Touchpad support depends on whether the device exposes its contact reports to applications. Keyboard controls and a regular vertical mouse wheel are the fallbacks.
- On a single display, low magnification uses Windows' native magnifier transform. At higher magnification, the app draws an in-memory desktop capture and pixel grid in one click-through window. With multiple displays, standard mode magnifies the display under the pointer using the capture path. Multi-display behavior has not been verified on physical hardware.
- The capture path may show blank areas for protected video. It does not save or transmit captured frames. The zoom overlay is excluded from capture to prevent feedback.
- Quit Windows Magnifier or another desktop magnifier before opening this app.

## Build from source

Run **build.cmd** on Windows x64. It uses the compiler from the installed .NET Framework and writes the portable executable to **dist**. Run **build.cmd console** for a build with console diagnostics.
