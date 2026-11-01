using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AmtPtpControlPanel.WinUI3
{
    /// <summary>
    /// Talks to the USB UMDF driver through a named pipe.
    /// </summary>
    public static class UsbDevice
    {
        private const string PipeName = "AmtPtpControlPanelUsbUmInterface";

        private const byte CmdReloadSettings = 0x00;
        private const byte CmdGetBattery = 0x01;

        private const byte StatusOk = 0x00;
        private const byte StatusFail = 0xFF;
        private const byte StatusUnknown = 0xFE;

        public static bool ReloadSettings()
        {
            return Send(CmdReloadSettings) == null;
        }

        public static bool GetBattery(out uint result)
        {
            object? r = Send(CmdGetBattery);
            if (r is uint value)
            {
                result = value;
                return true;
            }

            result = 0;
            return false;
        }

        private static object? Send(byte cmd)
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(250);

                    // Send command byte.
                    pipe.WriteByte(cmd);
                    pipe.Flush();

                    // Read 2-byte response [status][payload].
                    byte[] response = new byte[2];
                    int read = 0;
                    while (read < response.Length)
                    {
                        int n = pipe.Read(response, read, response.Length - read);
                        if (n <= 0)
                            throw new IOException("Pipe closed before full response.");
                        read += n;
                    }

                    byte status = response[0];
                    byte payload = response[1];

                    switch (status)
                    {
                        case StatusOk:
                            return cmd == CmdGetBattery ? (object)(uint)payload : null;
                        case StatusFail:
                            return "Driver reported failure.";
                        case StatusUnknown:
                            return "Driver reported unknown command.";
                        default:
                            return $"Unexpected status 0x{status:X2}.";
                    }
                }
            }
            catch (TimeoutException)
            {
                return "Timed out waiting for driver pipe.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Talks to the Bluetooth driver control device through DeviceIoControl.
    /// </summary>
    public static class BtDevice
    {
        private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;

        private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access)
        {
            return (deviceType << 16) | (access << 14) | (function << 2) | method;
        }

        public static readonly uint IOCTL_RELOAD_SETTINGS =
            CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS);

        public static readonly uint IOCTL_GET_BATTERY =
            CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        public static bool SendIoctl(uint code)
        {
            return ExecuteIoctl(code, out _, false);
        }

        public static bool SendIoctl(uint code, out uint result)
        {
            return ExecuteIoctl(code, out result, true);
        }

        private static bool ExecuteIoctl(uint code, out uint result, bool expectData)
        {
            result = 0;
            SafeFileHandle? hDevice = null;
            IntPtr pOutBuffer = IntPtr.Zero;

            try
            {
                hDevice = CreateFile(
                    @"\\.\AmtPtpControlDeviceUm",
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (hDevice.IsInvalid)
                    return false;

                uint outBufferSize = 0;
                if (expectData)
                {
                    outBufferSize = sizeof(uint);
                    pOutBuffer = Marshal.AllocHGlobal((int)outBufferSize);
                }

                bool success = DeviceIoControl(
                    hDevice,
                    code,
                    IntPtr.Zero,
                    0,
                    pOutBuffer,
                    outBufferSize,
                    out _,
                    IntPtr.Zero);

                if (success && expectData)
                    result = (uint)Marshal.ReadInt32(pOutBuffer);

                return success;
            }
            finally
            {
                if (pOutBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(pOutBuffer);

                hDevice?.Dispose();
            }
        }
    }
}
