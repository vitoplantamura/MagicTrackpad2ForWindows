This is a fork of [vitoplantamura's MagicTrackpad2ForWindows](https://github.com/vitoplantamura/MagicTrackpad2ForWindows) with additional support for the new USB C Magic Trackpad 2.
Installation of this fork differs slightly from the original project.

# Windows Precision Touchpad Implementation for Apple MacBook family/Magic Trackpad 2

This is the Precision Touchpad driver for the Magic Trackpad 2 that I personally use on my PCs. It is based entirely on the excellent [imbushuo driver](https://github.com/imbushuo/mac-precision-touchpad) and solves a number of problems explained in the "Context" section below. This is an image of the Control Panel: (each option is an additional feature built on top of the "vanilla" imbushuo driver)

![Control Panel](https://raw.githubusercontent.com/vitoplantamura/MagicTrackpad2ForWindows/master/assets/ControlPanel.png)

## Context

In terms of hardware, the Magic Trackpad 2 is the best external touchpad you can buy (not just for macOS), by far. In terms of software, specifically for Windows, AFAIK there are 4 options to use it: Trackpad++, Magic Utilities, the excellent [open source driver by imbushuo](https://github.com/imbushuo/mac-precision-touchpad) and the [official 2021 Apple driver](https://github.com/lc700x/MagicTrackPad2_Windows_Precision_Drivers). In my opinion the two options that offer the best feeling, experience and stability (using the MT2 via USB at least) are the last two (imbushuo and Apple drivers) which coincidentally are extremely similar according to my tests (in terms of "feeling", and they are the only 2 that present the MT2 as a Precision Touchpad to Windows). Unfortunately these two solutions present different pointer precision problems. One problem is that of "near field fingers", i.e. the trackpad registers movements and clicks even without physically touching the trackpad surface, at a distance of even one millimeter from the surface. Another issue (determined by the first) is the accuracy of the pointer when you lift your finger from the trackpad. Furthermore, AFAIK, with both the imbushuo driver and the Apple one, it is not possible to adjust the behavior of the haptic feedback.

A few months ago, I discovered [this excellent PR](https://github.com/imbushuo/mac-precision-touchpad/pull/533) of [1Revenger1](https://github.com/1Revenger1) to the imbushuo repo (which unfortunately hasn't been updated for 3 years). This PR solves the "near field fingers" problem. It also removes the QueryPerformanceCounter call in the interrupt function, instead setting the timestamp of the reports to the value returned by the Magic Trackpad itself (this may seem secondary but it is important, since using the MT2 in conditions of heavy PC load can determine returning inaccurate timestamps due to delayed thread scheduling). This PR convinced me that it might be worth investing some time in trying to solve all the other problems that made using MT2 more uncomfortable on PC than on macOS: I added the Control Panel, the ability to control the MT2's haptic feedback and other pointer precision options which I personally found useful.

I'm really happy with the result: the feeling of the MT2 is identical to that of the touchpad of my laptop and very similar to that of the MT2 when used in macOS (pointer acceleration is different, but this is not determined by the driver).

**Additional Credits**: The haptic feedback control messages sent by the driver to the MT2 in this project are based on the excellent reverse engineering work of [dos1](https://github.com/dos1) ([here](https://github.com/mwyborski/Linux-Magic-Trackpad-2-Driver/issues/28#issuecomment-451625504)).

**License**: This project has the same license as the imbushuo project, on which it is entirely based.

## Installation

**NOTE**: Only for the MT2 when connected via USB, ie bluetooth not supported.

1) Connect the MT2 to the PC via USB and first install the imbushuo driver: download [this file (for x86)](https://github.com/imbushuo/mac-precision-touchpad/releases/download/2105-3979/Drivers-amd64-ReleaseMSSigned.zip) or [this file (for ARM)](https://github.com/imbushuo/mac-precision-touchpad/releases/download/2105-3979/Drivers-arm64-ReleaseMSSigned.zip), unzip it, right-click on the INF file and click "Install".

2) Download the zip file of this project from the [Releases](https://github.com/vitoplantamura/MagicTrackpad2ForWindows/releases) of this repo, unzip it, start the Control Panel and click on "Install Driver".

3) Manually choose the drivers in Device Manager for the Human Interface Devices with the Instance Path that matches this: USB\VID_05AC&PID_0324&MI_00\*** & USB\VID_05AC&PID_0324&MI_01\*** to be the Apple USB Precision Touchpad Device (User-mode) by Bingxing Wang (imbushuo). This can be done by double-clicking the device item in Device Manager -> (Details Tab) Update Driver -> Browse my computer for drivers -> Pick from list of drivers -> Uncheck Show compatible hardware.

4) Use [AmtPtpControlPanel.exe] to fine tune your experience after confirming that the driver has loaded successfully.

## How the Installation Works

The imbushuo MT2 USB driver is a UMDF driver. Windows Driver Signature Enforcement does not block the loading of self-signed UMDF drivers. This unfortunately does not apply to KMDF drivers such as the imbushuo bluetooth driver for the MT2 and this is the reason why this project only supports the USB version of the driver. On a personal note I prefer to use the MT2 via USB: the MT2 can be switched between different computers without problems, no worries about the battery, the driver cannot bluescreen the PC and the USB version of the imbushuo driver in particular has proven to be very stable over the years.

When you click on "Install Driver" in the Control Panel, all MT2s connected to the system are disabled and then the AmtPtpDeviceUsbUm.dll file is updated directly in the Windows Driver Store. The owner and ACL of the file are modified to allow copying, and are restored to their original state at the end of the procedure. At the end of the copy, the MT2 is re-enabled. This procedure is not supported by Microsoft, but is commonly used during driver development and does not put system security at risk (as long as the file you are copying is trusted).
