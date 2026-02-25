# Magic Trackpad 2 Precision Touchpad driver for Windows 11, signed by Microsoft, no more hacks required 🎉

This is a fork of the excellent [imbusho](https://github.com/imbushuo/mac-precision-touchpad) driver for the Magic Trackpad 2. **It supports Bluetooth**. Currently, it only works on Windows 11: **Windows 10 is not supported**. Compared to imbusho or to the official 2021 Apple driver, this project adds:

- support for USB-C Magic Trackpad 2
- battery level reading
- haptic feedback control
- various options for controlling pointer precision
- a control panel:

![Control Panel](https://raw.githubusercontent.com/vitoplantamura/MagicTrackpad2ForWindows/master/assets/ControlPanel.png)

The previous version of this project used a hack to install itself in the DriverStore and couldn't support Bluetooth. At the beginning of this year, I decided to purchase an EV certificate to properly sign the driver: I paid 485 euros for it, including taxes that I have no way of recovering as an individual. I was tired of seeing people resorting to the wildest hacks to get the MT2 to work via Bluetooth 😀 (you can get a glimpse of this in the issues of this repo). **Windows drivers signing requirements and costs are unfair to open-source developers**.

## Installation

0) Remove any previous versions of this driver, imbushuo, `Trackpad++`, `official 2021 Apple driver` or `Magic Utilities`. Personally I use [DriverStore Explorer](https://github.com/lostindark/DriverStoreExplorer) for that, alternatively you can use Windows Device Manager.

1) Download the zip file of this project from the [Releases](https://github.com/vitoplantamura/MagicTrackpad2ForWindows/releases) of this repo and unzip it.

2) Select your architecture: Windows 11 AMD64 or ARM64. Right-click on the INF file and click "Install".

## Credits

- [This excellent PR](https://github.com/imbushuo/mac-precision-touchpad/pull/533) of [1Revenger1](https://github.com/1Revenger1) to the imbushuo repo, which fixes the "near field fingers" problem, cleans up the code, and removes the QueryPerformanceCounter call in the interrupt function.

- The haptic feedback control messages sent by the driver to the MT2 in this project are based on the excellent reverse engineering work of [dos1](https://github.com/dos1) ([here](https://github.com/mwyborski/Linux-Magic-Trackpad-2-Driver/issues/28#issuecomment-451625504)).

- My long-time friends at [Landlogic IT](https://landlogic.it/), who took care of the grueling process of gaining access to Microsoft's Hardware Dashboard and who take care of signing the driver packages for me.
