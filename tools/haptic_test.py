"""Fire the Magic Trackpad 2 haptic actuator from user mode.

Bluetooth: IOCTL_PTPFILTER_ACTUATE on \\\\.\\AmtPtpHapticDeviceUm (AmtPtpHidFilter).
USB:       command 0x02 on the pipe \\\\.\\pipe\\AmtPtpControlPanelUsbUmInterface (AmtPtpDeviceUsbUm).
Neither needs elevation. Python 3 on Windows, no packages needed.

    python haptic_test.py                      one medium click
    python haptic_test.py firm -n 5 -i 300     five firm clicks, 300 ms apart
    python haptic_test.py 0x08081E             a raw strength (FeedbackClick encoding)
    python haptic_test.py --hold               keep the device open; click on Enter

If the driver requires elevation for user-mode access, CreateFile or DeviceIoControl
fails with ERROR_ACCESS_DENIED; the script then exits with a hint to set
"AllowNonAdminAccess" in the registry.
"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

PRESETS = {"light": 0x040415, "medium": 0x060617, "firm": 0x08081E}
HAPTIC_DEVICE = r"\\.\AmtPtpHapticDeviceUm"
USB_PIPE = r"\\.\pipe\AmtPtpControlPanelUsbUmInterface"
IOCTL_PTPFILTER_ACTUATE = 0x0022A008  # CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_WRITE_ACCESS)
CMD_ACTUATE = 0x02

GENERIC_READ, GENERIC_WRITE = 0x80000000, 0x40000000
OPEN_EXISTING = 3
ERROR_FILE_NOT_FOUND, ERROR_PIPE_BUSY, ERROR_ACCESS_DENIED = 2, 231, 5
INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value

ACCESS_DENIED_MESSAGE = ('Access denied: consider adding "AllowNonAdminAccess" '
                         'in the registry.')


class AccessDeniedError(OSError):
    """Driver rejected user-mode access (ERROR_ACCESS_DENIED)."""


def check_access(error, what):
    if error == ERROR_ACCESS_DENIED:
        raise AccessDeniedError(error, what)


k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CreateFileW.restype = wintypes.HANDLE
k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                            wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
k32.DeviceIoControl.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD,
                                wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
k32.WriteFile.argtypes = [wintypes.HANDLE, wintypes.LPCVOID, wintypes.DWORD,
                          ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
k32.ReadFile.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
                         ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
k32.WaitNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD]
k32.CloseHandle.argtypes = [wintypes.HANDLE]


def open_file(path, access):
    handle = k32.CreateFileW(path, access, 3, None, OPEN_EXISTING, 0, None)
    if handle == INVALID_HANDLE_VALUE:
        return None, ctypes.get_last_error()
    return handle, 0


class Bluetooth:
    name = "Bluetooth, " + HAPTIC_DEVICE

    def __init__(self, handle):
        self.handle = handle

    def fire(self, strength):
        value = wintypes.ULONG(strength)
        returned = wintypes.DWORD()
        if not k32.DeviceIoControl(self.handle, IOCTL_PTPFILTER_ACTUATE, ctypes.byref(value),
                                   ctypes.sizeof(value), None, 0, ctypes.byref(returned), None):
            error = ctypes.get_last_error()
            check_access(error, "DeviceIoControl " + HAPTIC_DEVICE)
            raise OSError(error, "DeviceIoControl")

    def close(self):
        k32.CloseHandle(self.handle)


class Usb:
    name = "USB, " + USB_PIPE

    def fire(self, strength):
        # The driver serves one command per connection.
        for _ in range(20):
            handle, error = open_file(USB_PIPE, GENERIC_READ | GENERIC_WRITE)
            if handle is not None:
                break
            check_access(error, "CreateFile " + USB_PIPE)
            if error != ERROR_PIPE_BUSY:
                raise OSError(error, "CreateFile " + USB_PIPE)
            k32.WaitNamedPipeW(USB_PIPE, 500)
        else:
            raise OSError(ERROR_PIPE_BUSY, "CreateFile " + USB_PIPE)
        try:
            request = bytes([CMD_ACTUATE]) + strength.to_bytes(3, "little")
            done = wintypes.DWORD()
            if not k32.WriteFile(handle, request, len(request), ctypes.byref(done), None):
                error = ctypes.get_last_error()
                check_access(error, "WriteFile")
                raise OSError(error, "WriteFile")
            response = ctypes.create_string_buffer(2)
            if not k32.ReadFile(handle, response, 2, ctypes.byref(done), None) or done.value != 2:
                error = ctypes.get_last_error()
                check_access(error, "ReadFile")
                raise OSError(error, "ReadFile")
            if response.raw[0] != 0:
                raise OSError(0, "driver answered 0x%02X (0xFF: failed, 0xFE: unknown command, "
                                 "i.e. a driver without CMD_ACTUATE)" % response.raw[0])
        finally:
            k32.CloseHandle(handle)

    def close(self):
        pass


def connect():
    handle, error = open_file(HAPTIC_DEVICE, GENERIC_WRITE)
    if handle is not None:
        return Bluetooth(handle)
    check_access(error, "CreateFile " + HAPTIC_DEVICE)
    if error != ERROR_FILE_NOT_FOUND:
        raise OSError(error, "CreateFile " + HAPTIC_DEVICE)

    handle, error = open_file(USB_PIPE, GENERIC_READ | GENERIC_WRITE)
    if handle is not None or error == ERROR_PIPE_BUSY:
        if handle is not None:
            k32.CloseHandle(handle)  # just probing; each click reconnects
        return Usb()
    check_access(error, "CreateFile " + USB_PIPE)
    sys.exit("No Magic Trackpad 2 driver with haptics found: neither %s nor %s (error %d)."
             % (HAPTIC_DEVICE, USB_PIPE, error))


def parse_strength(text):
    if text.lower() in PRESETS:
        return PRESETS[text.lower()]
    value = int(text, 0)
    if not 0 <= value <= 0xFFFFFF:
        raise argparse.ArgumentTypeError("strength is 24 bits")
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("strength", nargs="?", default="medium", type=parse_strength,
                        help="light, medium, firm, or a number like 0x060617 (default: medium)")
    parser.add_argument("-n", "--count", type=int, default=1, help="number of clicks (default: 1)")
    parser.add_argument("-i", "--interval", type=float, default=250, help="ms between clicks (default: 250)")
    parser.add_argument("--hold", action="store_true",
                        help="keep the device open and click on each Enter, e.g. across a reconnect")
    args = parser.parse_args()

    try:
        device = connect()
    except AccessDeniedError:
        sys.exit(ACCESS_DENIED_MESSAGE)
    print("Using", device.name)
    try:
        if args.hold:
            print("Enter: click, q + Enter: quit")
            while input() != "q":
                fire(device, args.strength)
        else:
            for i in range(args.count):
                if i:
                    time.sleep(args.interval / 1000)
                fire(device, args.strength)
    finally:
        device.close()


def fire(device, strength):
    start = time.perf_counter()
    try:
        device.fire(strength)
        print("click 0x%06X: ok, %.0f ms" % (strength, (time.perf_counter() - start) * 1000))
    except AccessDeniedError:
        sys.exit(ACCESS_DENIED_MESSAGE)
    except OSError as e:
        print("click 0x%06X: failed: %s" % (strength, e))


if __name__ == "__main__":
    main()
