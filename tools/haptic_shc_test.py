"""Exercise the Windows haptic touchpad interface of the Magic Trackpad 2 drivers.

The drivers expose a Simple Haptic Controller in the touch pad collection (see
common/MagicTrackpad2Haptics.h). This script talks to it the way Windows does, over USB or
Bluetooth, without elevation (the collection is opened with no access rights, which is
enough for feature and output reports):

    python haptic_shc_test.py                    show the haptic collections and current values
    python haptic_shc_test.py fire step          fire one waveform at full intensity
    python haptic_shc_test.py fire all -i 50     fire every waveform at intensity 50
    python haptic_shc_test.py set 3 75           click sensitivity 3 (Heavy), haptic click 75
    python haptic_shc_test.py play               interactive: type a waveform and press Enter to fire it

Waveforms: press, release, hover, collide, align, step, grow. Windows uses collide for
window snapping and step for the Settings slider preview.

"set" does what Windows Settings does; Windows sends its own values again when you change
them in Settings or reconnect the trackpad.
Python 3 on Windows, no packages needed.
"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

HID_GUID = "{4d1e55b2-f16f-11cf-88cb-001111000030}"
DEVICE_VID = 0x8910          # synthetic VID both drivers report
REPORTID_THRESHOLD, REPORTID_INTENSITY, REPORTID_WAVEFORMS, REPORTID_TRIGGER = 0x0A, 0x0B, 0x0C, 0x0D
WAVEFORMS = ["press", "release", "hover", "collide", "align", "step", "grow"]  # ordinals 3..9
WAVEFORM_IDS = {0x1006: "press", 0x1007: "release", 0x1008: "hover", 0x1012: "collide",
                0x1013: "align", 0x1014: "step", 0x1015: "grow"}

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
hid = ctypes.WinDLL("hid", use_last_error=True)
cfg = ctypes.WinDLL("cfgmgr32")
k32.CreateFileW.restype = wintypes.HANDLE
k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                            wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
k32.CloseHandle.argtypes = [wintypes.HANDLE]
for name in ("HidD_GetFeature", "HidD_SetFeature", "HidD_SetOutputReport"):
    getattr(hid, name).argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.ULONG]
hid.HidD_GetPreparsedData.argtypes = [wintypes.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidD_GetAttributes.argtypes = [wintypes.HANDLE, ctypes.c_void_p]


class GUID(ctypes.Structure):
    _fields_ = [("d1", ctypes.c_ulong), ("d2", ctypes.c_ushort), ("d3", ctypes.c_ushort), ("d4", ctypes.c_ubyte * 8)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", ctypes.c_ulong), ("VendorID", ctypes.c_ushort), ("ProductID", ctypes.c_ushort),
                ("VersionNumber", ctypes.c_ushort)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", ctypes.c_ushort), ("UsagePage", ctypes.c_ushort),
                ("InputReportByteLength", ctypes.c_ushort), ("OutputReportByteLength", ctypes.c_ushort),
                ("FeatureReportByteLength", ctypes.c_ushort), ("Reserved", ctypes.c_ushort * 17),
                ("NumberLinkCollectionNodes", ctypes.c_ushort)] + \
               [(n, ctypes.c_ushort) for n in ("a", "b", "c", "d", "e", "f", "g", "h", "i")]


class HIDP_LINK_COLLECTION_NODE(ctypes.Structure):
    _fields_ = [("LinkUsage", ctypes.c_ushort), ("LinkUsagePage", ctypes.c_ushort), ("Parent", ctypes.c_ushort),
                ("NumberOfChildren", ctypes.c_ushort), ("NextSibling", ctypes.c_ushort),
                ("FirstChild", ctypes.c_ushort), ("Bits", ctypes.c_ulong), ("UserContext", ctypes.c_void_p)]


def hid_interfaces():
    guid = GUID()
    ctypes.oledll.ole32.CLSIDFromString(HID_GUID, ctypes.byref(guid))
    size = ctypes.c_ulong()
    cfg.CM_Get_Device_Interface_List_SizeW(ctypes.byref(size), ctypes.byref(guid), None, 0)
    buf = ctypes.create_unicode_buffer(size.value)
    if cfg.CM_Get_Device_Interface_ListW(ctypes.byref(guid), None, buf, size.value, 0) != 0:
        return []
    return [p for p in buf[:size.value].split("\0") if p]


def open_touchpad():
    """Returns (handle, caps) for the first PTP collection of a Magic Trackpad 2 driver."""
    for path in hid_interfaces():
        h = k32.CreateFileW(path, 0, 3, None, 3, 0, None)
        if h == wintypes.HANDLE(-1).value:
            continue
        attrs = HIDD_ATTRIBUTES(Size=ctypes.sizeof(HIDD_ATTRIBUTES))
        pp = ctypes.c_void_p()
        caps = HIDP_CAPS()
        if hid.HidD_GetAttributes(h, ctypes.byref(attrs)) and attrs.VendorID == DEVICE_VID \
                and hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
            hid.HidP_GetCaps(pp, ctypes.byref(caps))
            if (caps.UsagePage, caps.Usage) == (0x0D, 0x05):
                return h, caps, pp, path
            hid.HidD_FreePreparsedData(pp)
        k32.CloseHandle(h)
    sys.exit("No Magic Trackpad 2 touch pad collection found (driver VID 0x%04X)." % DEVICE_VID)


def get_feature(h, caps, report_id):
    buf = (ctypes.c_ubyte * caps.FeatureReportByteLength)()
    buf[0] = report_id
    if not hid.HidD_GetFeature(h, buf, len(buf)):
        raise OSError(ctypes.get_last_error(), "HidD_GetFeature 0x%02X" % report_id)
    return bytes(buf)


def set_feature(h, caps, report_id, value):
    buf = (ctypes.c_ubyte * caps.FeatureReportByteLength)()
    buf[0], buf[1] = report_id, value
    if not hid.HidD_SetFeature(h, buf, len(buf)):
        raise OSError(ctypes.get_last_error(), "HidD_SetFeature 0x%02X" % report_id)


def fire(h, caps, ordinal, intensity):
    buf = (ctypes.c_ubyte * caps.OutputReportByteLength)()
    buf[0], buf[1], buf[2] = REPORTID_TRIGGER, ordinal, intensity
    start = time.perf_counter()
    if not hid.HidD_SetOutputReport(h, buf, len(buf)):
        raise OSError(ctypes.get_last_error(), "HidD_SetOutputReport")
    return (time.perf_counter() - start) * 1000


def show(h, caps, pp, path):
    print("Touch pad collection:", path)
    n = wintypes.ULONG(caps.NumberLinkCollectionNodes)
    nodes = (HIDP_LINK_COLLECTION_NODE * n.value)()
    hid.HidP_GetLinkCollectionNodes(nodes, ctypes.byref(n), pp)
    shc = [i for i, node in enumerate(nodes) if node.LinkUsagePage == 0x0E and node.LinkUsage == 0x01]
    if not shc or caps.OutputReportByteLength == 0:
        sys.exit("This driver has no haptic controller (Simple Haptic Controller collections: %d)." % len(shc))
    print("Simple Haptic Controller collections: %d, output report: %d bytes" % (len(shc), caps.OutputReportByteLength))
    print("Click sensitivity (button press threshold): %d (1 Light .. 3 Heavy)" % get_feature(h, caps, REPORTID_THRESHOLD)[1])
    print("Haptic click intensity: %d" % get_feature(h, caps, REPORTID_INTENSITY)[1])
    r = get_feature(h, caps, REPORTID_WAVEFORMS)
    count = len(WAVEFORMS)
    ids = [int.from_bytes(r[1 + 2 * i:3 + 2 * i], "little") for i in range(count)]
    durations = r[1 + 2 * count:1 + 3 * count]
    for i, (wid, ms) in enumerate(zip(ids, durations)):
        print("  ordinal %d: 0x%04X %-8s %d ms" % (i + 3, wid, WAVEFORM_IDS.get(wid, "?"), ms))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd")
    p_fire = sub.add_parser("fire", help="fire a waveform (or 'all')")
    p_fire.add_argument("waveform", choices=WAVEFORMS + ["all"])
    p_fire.add_argument("-i", "--intensity", type=int, default=100, help="0..100 (default 100)")
    p_fire.add_argument("-n", "--count", type=int, default=1)
    p_fire.add_argument("--interval", type=float, default=600, help="ms between pulses (default 600)")
    p_play = sub.add_parser("play", help="interactive: fire waveforms on Enter")
    p_play.add_argument("-i", "--intensity", type=int, default=100, help="0..100 (default 100)")
    p_set = sub.add_parser("set", help="set click sensitivity (1..3) and haptic click intensity (0..100)")
    p_set.add_argument("threshold", type=int, choices=[1, 2, 3])
    p_set.add_argument("intensity", type=int)
    args = parser.parse_args()

    h, caps, pp, path = open_touchpad()
    try:
        if args.cmd is None:
            show(h, caps, pp, path)
        elif args.cmd == "play":
            intensity = max(0, min(100, args.intensity))
            menu = "  ".join("%d %s" % (i + 1, n) for i, n in enumerate(WAVEFORMS))
            print("Rest a finger on the trackpad. Type a waveform (name or number) and press Enter;")
            print("Enter alone repeats the last one, i<0..100> sets the intensity, q quits.")
            print(menu)
            last = None
            while True:
                try:
                    choice = input("[intensity %d] > " % intensity).strip().lower()
                except EOFError:
                    break
                if choice == "q":
                    break
                if choice.startswith("i") and choice[1:].isdigit():
                    intensity = max(0, min(100, int(choice[1:])))
                    continue
                if choice == "" and last:
                    choice = last
                if choice.isdigit() and 1 <= int(choice) <= len(WAVEFORMS):
                    choice = WAVEFORMS[int(choice) - 1]
                if choice not in WAVEFORMS:
                    print(menu)
                    continue
                last = choice
                ms = fire(h, caps, WAVEFORMS.index(choice) + 3, intensity)
                print("%s at %d: ok, %.0f ms" % (choice, intensity, ms))
        elif args.cmd == "set":
            set_feature(h, caps, REPORTID_THRESHOLD, args.threshold)
            set_feature(h, caps, REPORTID_INTENSITY, max(0, min(100, args.intensity)))
            print("Set click sensitivity %d, haptic click intensity %d" % (args.threshold, args.intensity))
        else:
            names = WAVEFORMS if args.waveform == "all" else [args.waveform] * args.count
            for k, name in enumerate(names):
                if k:
                    time.sleep(args.interval / 1000)
                ms = fire(h, caps, WAVEFORMS.index(name) + 3, max(0, min(100, args.intensity)))
                print("%-8s intensity %3d: ok, %.0f ms" % (name, args.intensity, ms))
    except OSError as e:
        sys.exit("Failed: %s" % e)
    finally:
        hid.HidD_FreePreparsedData(pp)
        k32.CloseHandle(h)


if __name__ == "__main__":
    main()
