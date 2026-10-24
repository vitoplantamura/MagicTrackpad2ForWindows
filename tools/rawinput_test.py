#!/usr/bin/env python3
"""
Magic Trackpad 2 — Raw Input visualizer.

Reads the PTP touchpad stream via Windows Raw Input (Usage Page 0x0D / Usage 0x05),
bypassing the exclusive-open restriction on the HID collection.

The payload is extracted with ctypes.string_at, because bRawData is declared
as c_ubyte * 1 and ctypes array slicing cannot extend past the declared
length — using bRawData[:n] would silently truncate every report to 1 byte.
"""

import ctypes
from ctypes import wintypes as wt
import tkinter as tk

# ---------------------------------------------------------------------------
# Protocol constants
# ---------------------------------------------------------------------------
RID_MULTITOUCH = 0x05
RID_RAW_BT     = 0x31
N_CONTACTS     = 5

X_MIN, X_MAX = -3678, 3934
Y_MIN, Y_MAX = -2479, 2586
X_RANGE = X_MAX - X_MIN
Y_RANGE = Y_MAX - Y_MIN
PRESS_MAX = 300

CANVAS_H = 600
CANVAS_W = int(CANVAS_H * (X_RANGE / Y_RANGE))

COLOR_TOUCH, COLOR_HOVER, COLOR_BTN = "#2563eb", "#93b4f5", "#dc2626"

# ---------------------------------------------------------------------------
# Win32 types
# ---------------------------------------------------------------------------
LRESULT  = ctypes.c_ssize_t
LONG_PTR = ctypes.c_ssize_t
WNDPROC  = ctypes.WINFUNCTYPE(LRESULT, wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM)

GWLP_WNDPROC = -4

RIDEV_INPUTSINK = 0x00000100
RIDEV_DEVNOTIFY = 0x00002000
RID_INPUT       = 0x10000003
WM_INPUT        = 0x00FF

# ---------------------------------------------------------------------------
# Raw Input structures
# ---------------------------------------------------------------------------
class RAWINPUTDEVICE(ctypes.Structure):
    _fields_ = [
        ("usUsagePage", wt.USHORT),
        ("usUsage",     wt.USHORT),
        ("dwFlags",     wt.DWORD),
        ("hwndTarget",  wt.HWND),
    ]

class RAWINPUTHEADER(ctypes.Structure):
    _fields_ = [
        ("dwType",  wt.DWORD),
        ("dwSize",  wt.DWORD),
        ("hDevice", wt.HANDLE),
        ("wParam",  wt.WPARAM),
    ]

class RAWHID(ctypes.Structure):
    _fields_ = [
        ("dwSizeHid", wt.DWORD),
        ("dwCount",   wt.DWORD),
        ("bRawData",  ctypes.c_ubyte * 1),   # variable-length, do NOT slice
    ]

class RAWINPUT(ctypes.Structure):
    _fields_ = [
        ("header", RAWINPUTHEADER),
        ("hid",    RAWHID),
    ]

# ---------------------------------------------------------------------------
# Win32 functions
# ---------------------------------------------------------------------------
user32 = ctypes.WinDLL("user32", use_last_error=True)

user32.RegisterRawInputDevices.argtypes = [
    ctypes.POINTER(RAWINPUTDEVICE), wt.UINT, wt.UINT
]
user32.RegisterRawInputDevices.restype = wt.BOOL

user32.GetRawInputData.argtypes = [
    wt.HANDLE, wt.UINT, ctypes.c_void_p,
    ctypes.POINTER(wt.UINT), wt.UINT
]
user32.GetRawInputData.restype = wt.UINT

user32.GetWindowLongPtrW.argtypes = [wt.HWND, ctypes.c_int]
user32.GetWindowLongPtrW.restype  = LONG_PTR
user32.SetWindowLongPtrW.argtypes = [wt.HWND, ctypes.c_int, LONG_PTR]
user32.SetWindowLongPtrW.restype  = LONG_PTR

# ---------------------------------------------------------------------------
# Parsing
# ---------------------------------------------------------------------------
def u16(b, o): return b[o] | (b[o + 1] << 8)
def u32(b, o): return b[o] | (b[o+1] << 8) | (b[o+2] << 16) | (b[o+3] << 24)

def parse_ptp(buf):
    if not buf or buf[0] != RID_MULTITOUCH:
        return None
    if len(buf) < 1 + N_CONTACTS * 21 + 6:
        return None
    contacts, off = [], 1
    for _ in range(N_CONTACTS):
        b = buf[off]
        contacts.append(dict(
            confidence = b & 0x01,
            tipswitch  = (b >> 1) & 0x01,
            id         = u32(buf, off + 1),
            x          = u16(buf, off + 5),
            y          = u16(buf, off + 7),
            width      = u16(buf, off + 9),
            height     = u16(buf, off + 11),
            pressure   = u16(buf, off + 13),
            size       = buf[off + 15],
            touch_maj  = buf[off + 16],
            touch_min  = buf[off + 17],
            orient     = buf[off + 18],
            finger     = buf[off + 19],
            state      = buf[off + 20],
        ))
        off += 21
    return dict(
        contacts   = contacts,
        scan_time  = u16(buf, off),
        count      = buf[off + 2],
        mech_force = u16(buf, off + 3),
        button     = buf[off + 5],
        source     = "0x05",
    )

def parse_raw_bt(buf):
    if not buf or buf[0] != RID_RAW_BT or len(buf) < 4:
        return None
    contacts, off = [], 4
    while off + 9 <= len(buf):
        v = buf[off] | (buf[off+1] << 8) | (buf[off+2] << 16) | (buf[off+3] << 24)
        x_raw  = v & 0x1FFF
        y_raw  = (v >> 13) & 0x1FFF
        finger = (v >> 26) & 0x07
        state  = (v >> 29) & 0x07
        x = (x_raw << 19) >> 19
        y = (y_raw << 19) >> 19
        x -= X_MIN
        y = -y - Y_MIN
        contacts.append(dict(
            confidence = 1,
            tipswitch  = 1 if 3 <= state <= 5 else 0,
            id         = len(contacts),
            x          = max(0, x),
            y          = max(0, y),
            width      = 0, height = 0,
            pressure   = buf[off + 7],
            size       = buf[off + 6],
            touch_maj  = buf[off + 4],
            touch_min  = buf[off + 5],
            orient     = buf[off + 8] & 0x07,
            finger     = finger,
            state      = state,
        ))
        off += 9
    if not contacts:
        return None
    return dict(
        contacts   = contacts,
        scan_time  = 0,
        count      = len(contacts),
        mech_force = 0,
        button     = buf[1] & 0x01,
        source     = "0x31",
    )

# ---------------------------------------------------------------------------
# App
# ---------------------------------------------------------------------------
class App:
    def __init__(self, root):
        self.root = root
        root.title("Magic Trackpad 2 — Raw Input")
        root.configure(bg="#0b0b0b")
        root.resizable(False, False)

        frame = tk.Frame(root, bg="#0b0b0b")
        frame.pack(padx=8, pady=8)

        self.canvas = tk.Canvas(
            frame, width=CANVAS_W, height=CANVAS_H,
            bg="white", highlightthickness=1,
            highlightbackground="#333",
        )
        self.canvas.pack(side=tk.LEFT)

        self.info = tk.Text(
            frame, width=60, height=CANVAS_H // 15,
            bg="#0b0b0b", fg="#d8d8d8",
            font=("Consolas", 10),
            borderwidth=0, highlightthickness=0,
            padx=10, pady=6,
        )
        self.info.pack(side=tk.RIGHT, fill=tk.Y, padx=(10, 0))
        self.info.configure(state=tk.DISABLED)

        self._orig_wndproc   = None
        self._new_wndproc    = None
        self._wm_input_count = 0

        root.bind("<Map>", self._on_map)

    def _on_map(self, event):
        if event.widget is not self.root:
            return
        self.root.unbind("<Map>")
        self._install_raw_input()

    def _install_raw_input(self):
        hwnd = self.root.winfo_id()

        rid = RAWINPUTDEVICE()
        rid.usUsagePage = 0x0D
        rid.usUsage     = 0x05
        rid.dwFlags     = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY
        rid.hwndTarget  = hwnd
        if not user32.RegisterRawInputDevices(
                ctypes.byref(rid), 1, ctypes.sizeof(rid)):
            print(f"RegisterRawInputDevices FAILED: {ctypes.get_last_error()}")
            return
        print("Raw Input registered (0x0D / 0x05)")

        old = user32.GetWindowLongPtrW(hwnd, GWLP_WNDPROC)
        self._orig_wndproc = WNDPROC(old)
        self._new_wndproc  = WNDPROC(self._wndproc)
        addr = ctypes.cast(self._new_wndproc, ctypes.c_void_p).value
        user32.SetWindowLongPtrW(hwnd, GWLP_WNDPROC, addr)
        print("Window procedure subclassed")

    def _wndproc(self, hwnd, msg, wparam, lparam):
        if msg == WM_INPUT:
            self._wm_input_count += 1
            if self._wm_input_count <= 5:
                print(f"WM_INPUT #{self._wm_input_count}")
            try:
                self._handle_wm_input(lparam)
            except Exception as e:
                print(f"WM_INPUT handler error: {e}")
        return self._orig_wndproc(hwnd, msg, wparam, lparam)

    def _handle_wm_input(self, lparam):
        size = wt.UINT(0)
        user32.GetRawInputData(
            lparam, RID_INPUT, None,
            ctypes.byref(size), ctypes.sizeof(RAWINPUTHEADER),
        )
        if size.value == 0:
            return

        buf = ctypes.create_string_buffer(size.value)
        written = user32.GetRawInputData(
            lparam, RID_INPUT, buf,
            ctypes.byref(size), ctypes.sizeof(RAWINPUTHEADER),
        )
        if written == 0 or written == 0xFFFFFFFF:
            return

        raw = ctypes.cast(buf, ctypes.POINTER(RAWINPUT)).contents
        if raw.header.dwType != 2:              # RIM_TYPEHID
            return

        n = raw.hid.dwSizeHid * raw.hid.dwCount
        if n == 0:
            return

        # CRITICAL: bRawData is c_ubyte * 1. Slicing it cannot extend past
        # one element, so bRawData[:n] would silently return 1 byte. Read the
        # full payload directly from the buffer's memory instead.
        data = ctypes.string_at(ctypes.addressof(raw.hid.bRawData), n)

        rep = parse_ptp(data) or parse_raw_bt(data)
        if rep is None:
            print(f"unparsed: dwSizeHid={raw.hid.dwSizeHid} "
                  f"dwCount={raw.hid.dwCount} n={n} hex={data.hex()}")
            return

        self._draw(rep)

    def _draw(self, rep):
        c = self.canvas
        c.delete("all")

        for i in range(1, 4):
            c.create_line(CANVAS_W * i / 4, 0, CANVAS_W * i / 4, CANVAS_H, fill="#f0f0f0")
            c.create_line(0, CANVAS_H * i / 4, CANVAS_W, CANVAS_H * i / 4, fill="#f0f0f0")

        if rep["button"]:
            c.create_rectangle(2, 2, CANVAS_W - 2, CANVAS_H - 2,
                               outline=COLOR_BTN, width=4)

        n = min(rep["count"], N_CONTACTS)
        for i in range(n):
            ct = rep["contacts"][i]
            cx = max(0.0, min(float(CANVAS_W), ct["x"] / X_RANGE * CANVAS_W))
            cy = max(0.0, min(float(CANVAS_H), ct["y"] / Y_RANGE * CANVAS_H))

            p    = min(ct["pressure"], PRESS_MAX)
            frac = p / PRESS_MAX if PRESS_MAX else 0.0
            radius = 8 + frac * 34

            if ct["tipswitch"]:
                fill, outline = COLOR_TOUCH, "#1e3a8a"
            else:
                fill, outline = COLOR_HOVER, "#4a5f8a"

            ew = ct["width"]  / X_RANGE * CANVAS_W
            eh = ct["height"] / Y_RANGE * CANVAS_H
            if max(ew, eh) > 2 * radius:
                c.create_oval(cx - ew/2, cy - eh/2, cx + ew/2, cy + eh/2,
                              outline="#999", width=1, dash=(2, 2))

            c.create_oval(cx - radius, cy - radius,
                          cx + radius, cy + radius,
                          fill=fill, outline=outline, width=2)
            c.create_text(cx, cy, text=str(ct["id"]), fill="white",
                          font=("Consolas", max(8, int(radius * 0.8)), "bold"))

        self._update_info(rep)

    def _update_info(self, rep):
        lines = [
            f"source     : {rep['source']}",
            f"contacts   : {rep['count']}",
            f"button     : {'PRESSED' if rep['button'] else 'up'}",
            f"MechForce  : {rep['mech_force']}",
            f"ScanTime   : {rep['scan_time']}",
            "",
        ]
        n = min(rep["count"], N_CONTACTS)
        for i in range(n):
            ct = rep["contacts"][i]
            lines.append(
                f"[{i}] id={ct['id']}  x={ct['x']}  y={ct['y']}  "
                f"p={ct['pressure']}  tip={ct['tipswitch']}"
            )
            lines.append(
                f"     w={ct['width']}  h={ct['height']}  "
                f"sz={ct['size']}  tmaj={ct['touch_maj']}  tmin={ct['touch_min']}  "
                f"o={ct['orient']}  f={ct['finger']}  s=0x{ct['state']:02x}  "
                f"c={ct['confidence']}"
            )
            lines.append("")
        self.info.configure(state=tk.NORMAL)
        self.info.delete("1.0", tk.END)
        self.info.insert(tk.END, "\n".join(lines))
        self.info.configure(state=tk.DISABLED)

# ---------------------------------------------------------------------------
if __name__ == "__main__":
    root = tk.Tk()
    App(root)
    root.mainloop()
