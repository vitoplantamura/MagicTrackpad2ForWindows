using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AmtPtpControlPanel.WinUI3
{
    /// <summary>
    /// One contact of a Precision Touchpad input report.
    /// </summary>
    public struct TouchContact
    {
        public int Id;

        /// <summary>True while the finger touches the surface, false while it hovers or lifts.</summary>
        public bool Tip;

        /// <summary>False when the driver rejects the contact (palm rejection).</summary>
        public bool Confidence;

        /// <summary>Position on the surface, 0..1 from the left / top edge.</summary>
        public double X;
        public double Y;

        /// <summary>Axes of the contact ellipse in mm, 0 when the device does not report them.</summary>
        public double MajorMm;
        public double MinorMm;

        /// <summary>Clockwise rotation of the major axis from vertical, in degrees.</summary>
        public double AngleDeg;

        /// <summary>Pressure (0..255 on the Magic Trackpad 2), -1 when not reported.</summary>
        public int Pressure;

        /// <summary>The trackpad's own guess of the finger type (6 = palm), -1 when not reported.</summary>
        public int FingerType;

        public bool IsPalm => !Confidence || FingerType == 6;
    }

    /// <summary>
    /// All the contacts of one touchpad frame.
    /// </summary>
    public sealed class TouchFrame
    {
        public double SurfaceWidthMm;
        public double SurfaceHeightMm;
        public bool ButtonDown;
        public List<TouchContact> Contacts = new List<TouchContact>();
    }

    /// <summary>
    /// Reads Precision Touchpad reports through Raw Input and decodes them with the HID
    /// parser, so it follows the report descriptor and needs no access to the drivers.
    /// Raw Input is delivered to the given window, which is subclassed while reading.
    /// </summary>
    public sealed class TouchpadReader : IDisposable
    {
        public event Action<TouchFrame>? FrameReceived;

        private const uint WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEHID = 2;
        private const uint RIDI_PREPARSEDDATA = 0x20000005;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const int HIDP_STATUS_SUCCESS = 0x00110000;
        private const int HidP_Input = 0;

        private const ushort PageGenericDesktop = 0x01;
        private const ushort PageDigitizer = 0x0D;
        private const ushort PageButton = 0x09;
        private const ushort PageVendor = 0xFF00;

        private const ushort UsageX = 0x30;
        private const ushort UsageY = 0x31;
        private const ushort UsageTouchPad = 0x05;
        private const ushort UsagePressure = 0x30;
        private const ushort UsageTipSwitch = 0x42;
        private const ushort UsageConfidence = 0x47;
        private const ushort UsageWidth = 0x48;
        private const ushort UsageContactId = 0x51;
        private const ushort UsageContactCount = 0x54;

        // Vendor usages added to each finger collection by the Magic Trackpad 2 drivers.
        private const ushort VendorTouchMajor = 0x03;
        private const ushort VendorTouchMinor = 0x04;
        private const ushort VendorOrientation = 0x05;
        private const ushort VendorFingerType = 0x06;

        private static readonly UIntPtr SubclassId = (UIntPtr)0x4D543250; // "MT2P"

        private readonly Dictionary<IntPtr, DeviceInfo?> _devices = new Dictionary<IntPtr, DeviceInfo?>();
        private readonly ushort[] _usages = new ushort[64];
        private SubclassProc? _subclassProc;
        private IntPtr _hwnd;
        private IntPtr _buffer;
        private int _bufferSize;
        private TouchFrame? _pendingFrame;
        private int _pendingContacts;

        public bool IsRunning => _hwnd != IntPtr.Zero;

        public bool Start(IntPtr hwnd)
        {
            if (IsRunning)
                return true;

            _subclassProc = WndProc;
            if (!SetWindowSubclass(hwnd, _subclassProc, SubclassId, UIntPtr.Zero))
            {
                _subclassProc = null;
                return false;
            }

            RAWINPUTDEVICE[] rid =
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = PageDigitizer,
                    usUsage = UsageTouchPad,
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = hwnd,
                },
            };

            if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                RemoveWindowSubclass(hwnd, _subclassProc, SubclassId);
                _subclassProc = null;
                return false;
            }

            _hwnd = hwnd;
            return true;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            RAWINPUTDEVICE[] rid =
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = PageDigitizer,
                    usUsage = UsageTouchPad,
                    dwFlags = RIDEV_REMOVE,
                    hwndTarget = IntPtr.Zero,
                },
            };
            RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            if (_subclassProc != null)
                RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);

            _hwnd = IntPtr.Zero;
            _pendingFrame = null;
            _pendingContacts = 0;
        }

        public void Dispose()
        {
            Stop();

            foreach (DeviceInfo? device in _devices.Values)
            {
                if (device != null)
                    Marshal.FreeHGlobal(device.PreparsedData);
            }
            _devices.Clear();

            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
                _bufferSize = 0;
            }

            _subclassProc = null;
        }

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
        {
            if (uMsg == WM_INPUT)
            {
                try
                {
                    HandleRawInput(lParam);
                }
                catch
                {
                    // Never let a malformed report take down the window procedure.
                }
            }

            // WM_INPUT must still reach DefWindowProc so the system can clean up.
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void HandleRawInput(IntPtr hRawInput)
        {
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            uint size = 0;
            if (GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
                return;

            if (size > _bufferSize)
            {
                if (_buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_buffer);
                _bufferSize = (int)Math.Max(size, 1024);
                _buffer = Marshal.AllocHGlobal(_bufferSize);
            }

            if (GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, headerSize) != size)
                return;

            RAWINPUTHEADER header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
            if (header.dwType != RIM_TYPEHID)
                return;

            DeviceInfo? device = GetDevice(header.hDevice);
            if (device == null)
                return;

            // RAWHID follows the header: dwSizeHid, dwCount, then dwCount reports.
            int sizeHid = Marshal.ReadInt32(_buffer, (int)headerSize);
            int count = Marshal.ReadInt32(_buffer, (int)headerSize + 4);
            IntPtr reports = _buffer + (int)headerSize + 8;

            for (int i = 0; i < count; i++)
                ParseReport(device, reports + i * sizeHid, (uint)sizeHid);
        }

        private void ParseReport(DeviceInfo device, IntPtr report, uint length)
        {
            // In hybrid reporting the first report of a frame carries the total contact
            // count and the following ones carry 0, with the rest of the contacts.
            uint contactCount = GetValue(device, PageDigitizer, 0, UsageContactCount, report, length) ?? 0;
            if (contactCount > 0 || _pendingFrame == null)
            {
                _pendingFrame = new TouchFrame
                {
                    SurfaceWidthMm = device.SurfaceWidthMm,
                    SurfaceHeightMm = device.SurfaceHeightMm,
                    ButtonDown = HasUsage(device, PageButton, 0, 1, report, length),
                };
                _pendingContacts = (int)contactCount;
            }

            TouchFrame frame = _pendingFrame;

            foreach (ushort link in device.FingerCollections)
            {
                if (_pendingContacts <= 0)
                    break;
                _pendingContacts--;

                uint x = GetValue(device, PageGenericDesktop, link, UsageX, report, length) ?? 0;
                uint y = GetValue(device, PageGenericDesktop, link, UsageY, report, length) ?? 0;

                TouchContact c = new TouchContact
                {
                    Id = (int)(GetValue(device, PageDigitizer, link, UsageContactId, report, length) ?? 0),
                    X = device.X.Normalize(x),
                    Y = device.Y.Normalize(y),
                    Pressure = -1,
                    FingerType = -1,
                };

                GetUsages(device, PageDigitizer, link, report, length, out c.Tip, out c.Confidence);

                if (device.HasPressure)
                    c.Pressure = (int)(GetValue(device, PageDigitizer, link, UsagePressure, report, length) ?? 0);

                if (device.HasVendorExtras)
                {
                    // Raw TouchMajor/TouchMinor, scaled like the Width/Height usages (<< 2).
                    uint major = GetValue(device, PageVendor, link, VendorTouchMajor, report, length) ?? 0;
                    uint minor = GetValue(device, PageVendor, link, VendorTouchMinor, report, length) ?? 0;
                    uint orientation = GetValue(device, PageVendor, link, VendorOrientation, report, length) ?? 4;

                    c.MajorMm = (major << 2) * device.ContactMmPerUnit;
                    c.MinorMm = (minor << 2) * device.ContactMmPerUnit;
                    c.FingerType = (int)(GetValue(device, PageVendor, link, VendorFingerType, report, length) ?? 0);

                    // 4 = major axis along Y, 22.5 degrees per step, lower = turned clockwise.
                    c.AngleDeg = (4 - (int)(orientation & 7)) * 22.5;
                }

                frame.Contacts.Add(c);
            }

            if (_pendingContacts <= 0)
            {
                _pendingFrame = null;
                FrameReceived?.Invoke(frame);
            }
        }

        private uint? GetValue(DeviceInfo device, ushort page, ushort link, ushort usage, IntPtr report, uint length)
        {
            return HidP_GetUsageValue(HidP_Input, page, link, usage, out uint value, device.PreparsedData, report, length) == HIDP_STATUS_SUCCESS
                ? value
                : null;
        }

        private bool HasUsage(DeviceInfo device, ushort page, ushort link, ushort usage, IntPtr report, uint length)
        {
            uint n = (uint)_usages.Length;
            if (HidP_GetUsages(HidP_Input, page, link, _usages, ref n, device.PreparsedData, report, length) != HIDP_STATUS_SUCCESS)
                return false;

            for (int i = 0; i < n; i++)
            {
                if (_usages[i] == usage)
                    return true;
            }

            return false;
        }

        private void GetUsages(DeviceInfo device, ushort page, ushort link, IntPtr report, uint length, out bool tip, out bool confidence)
        {
            tip = false;
            confidence = false;

            uint n = (uint)_usages.Length;
            if (HidP_GetUsages(HidP_Input, page, link, _usages, ref n, device.PreparsedData, report, length) != HIDP_STATUS_SUCCESS)
                return;

            for (int i = 0; i < n; i++)
            {
                if (_usages[i] == UsageTipSwitch)
                    tip = true;
                else if (_usages[i] == UsageConfidence)
                    confidence = true;
            }
        }

        private DeviceInfo? GetDevice(IntPtr hDevice)
        {
            if (_devices.TryGetValue(hDevice, out DeviceInfo? cached))
                return cached;

            DeviceInfo? device = DescribeDevice(hDevice);
            _devices[hDevice] = device;
            return device;
        }

        private static DeviceInfo? DescribeDevice(IntPtr hDevice)
        {
            uint size = 0;
            GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            if (size == 0)
                return null;

            IntPtr preparsed = Marshal.AllocHGlobal((int)size);
            bool keep = false;

            try
            {
                if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, preparsed, ref size) == uint.MaxValue)
                    return null;

                if (HidP_GetCaps(preparsed, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                    return null;

                if (caps.UsagePage != PageDigitizer || caps.Usage != UsageTouchPad)
                    return null;

                ushort capCount = caps.NumberInputValueCaps;
                HIDP_VALUE_CAPS[] valueCaps = new HIDP_VALUE_CAPS[capCount];
                if (HidP_GetValueCaps(HidP_Input, valueCaps, ref capCount, preparsed) != HIDP_STATUS_SUCCESS)
                    return null;

                DeviceInfo device = new DeviceInfo { PreparsedData = preparsed };
                HIDP_VALUE_CAPS? widthCap = null;

                for (int i = 0; i < capCount; i++)
                {
                    HIDP_VALUE_CAPS cap = valueCaps[i];

                    if (cap.UsagePage == PageGenericDesktop && cap.Covers(UsageX))
                    {
                        if (!device.FingerCollections.Contains(cap.LinkCollection))
                            device.FingerCollections.Add(cap.LinkCollection);
                        device.X = new Axis(cap);
                    }
                    else if (cap.UsagePage == PageGenericDesktop && cap.Covers(UsageY))
                    {
                        device.Y = new Axis(cap);
                    }
                    else if (cap.UsagePage == PageDigitizer && cap.Covers(UsagePressure) && cap.LinkCollection != 0)
                    {
                        device.HasPressure = true;
                    }
                    else if (cap.UsagePage == PageDigitizer && cap.Covers(UsageWidth))
                    {
                        widthCap = cap;
                    }
                    else if (cap.UsagePage == PageVendor && cap.Covers(VendorFingerType))
                    {
                        device.HasVendorExtras = true;
                    }
                }

                if (device.FingerCollections.Count == 0)
                    return null;

                device.FingerCollections.Sort();

                // Size of the surface; when the descriptor has no physical size, keep
                // the aspect ratio of the logical ranges.
                device.SurfaceWidthMm = device.X.SizeMm ?? 100.0;
                device.SurfaceHeightMm = device.Y.SizeMm ?? device.SurfaceWidthMm * device.Y.LogicalRange / Math.Max(1, device.X.LogicalRange);

                // The contact axes use the scale of the Width usage, or the X axis.
                Axis contactAxis = widthCap != null ? new Axis(widthCap.Value) : device.X;
                device.ContactMmPerUnit = contactAxis.MmPerUnit ?? device.SurfaceWidthMm / Math.Max(1, device.X.LogicalRange);

                keep = true;
                return device;
            }
            finally
            {
                if (!keep)
                    Marshal.FreeHGlobal(preparsed);
            }
        }

        private sealed class DeviceInfo
        {
            public IntPtr PreparsedData;
            public List<ushort> FingerCollections = new List<ushort>();
            public Axis X;
            public Axis Y;
            public bool HasPressure;
            public bool HasVendorExtras;
            public double SurfaceWidthMm;
            public double SurfaceHeightMm;
            public double ContactMmPerUnit;
        }

        private struct Axis
        {
            public int LogicalMin;
            public int LogicalRange;
            public double? MmPerUnit;

            public Axis(HIDP_VALUE_CAPS cap)
            {
                LogicalMin = cap.LogicalMin;
                LogicalRange = Math.Max(1, cap.LogicalMax - cap.LogicalMin);
                MmPerUnit = null;

                // Only SI or English linear lengths (cm or inch) are understood.
                double? unitMm = (cap.Units & 0xFF) switch
                {
                    0x11 => 10.0,
                    0x13 => 25.4,
                    _ => null,
                };

                int physicalRange = cap.PhysicalMax - cap.PhysicalMin;
                if (unitMm != null && physicalRange > 0)
                {
                    int exponent = (int)(cap.UnitsExp & 0xF);
                    if (exponent > 7)
                        exponent -= 16;

                    MmPerUnit = physicalRange * Math.Pow(10, exponent) * unitMm.Value / LogicalRange;
                }
            }

            public double? SizeMm => MmPerUnit * LogicalRange;

            public double Normalize(uint value) => Math.Clamp(((int)value - LogicalMin) / (double)LogicalRange, 0.0, 1.0);
        }

        // ---- Win32 ----

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public byte HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;
            public ushort Reserved2a;
            public ushort Reserved2b;
            public ushort Reserved2c;
            public ushort Reserved2d;
            public ushort Reserved2e;
            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;
            public ushort UsageMin; // NotRange.Usage when IsRange == 0
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;

            public bool Covers(ushort usage) =>
                IsRange != 0 ? usage >= UsageMin && usage <= UsageMax : usage == UsageMin;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll")]
        private static extern int HidP_GetValueCaps(int ReportType, [Out] HIDP_VALUE_CAPS[] ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsageValue(int ReportType, ushort UsagePage, ushort LinkCollection, ushort Usage, out uint UsageValue, IntPtr PreparsedData, IntPtr Report, uint ReportLength);

        [DllImport("hid.dll")]
        private static extern int HidP_GetUsages(int ReportType, ushort UsagePage, ushort LinkCollection, [Out] ushort[] UsageList, ref uint UsageLength, IntPtr PreparsedData, IntPtr Report, uint ReportLength);
    }
}
