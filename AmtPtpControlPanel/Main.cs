using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AmtPtpControlPanel
{
    public partial class Main : Form
    {
        public Main()
        {
            InitializeComponent();
        }

        private void ctlTouchpadSettings_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("ms-settings:devices-touchpad");
            }
            catch
            {
            }
        }

        private void ctlApply_Click(object sender, EventArgs e)
        {
            using (new ButtonWait((Button)sender))
                if (SaveSettings())
                {
                    UsbDevice.ReloadSettings();
                    BtDevice.SendIoctl(BtDevice.IOCTL_RELOAD_SETTINGS);
                }
        }

        private void ctlClickOptions_CheckedChanged(object sender, EventArgs e)
        {
            if (!((RadioButton)sender).Checked)
                return;

            RadioButton[] ctls = { ctlMacOSClickOptions, ctlDisableFeedback, ctlMaximumFeedback };

            foreach (var ctl in ctls)
                if (ctl != sender)
                    ctl.Checked = false;

            Control[] macCtls = { ctlSilentClicking, ctlFeedback, ctlLightLabel, ctlMediumLabel, ctlFirmLabel };

            bool macEnabled = ctlMacOSClickOptions.Checked;
            foreach (var ctl in macCtls)
                ctl.Enabled = macEnabled;
        }

        private void ctlStop_CheckedChanged(object sender, EventArgs e)
        {
            if (!((RadioButton)sender).Checked)
                return;

            ctlStopPressureValue.Enabled = sender == ctlStopPressure;
            ctlStopSizeValue.Enabled = sender == ctlStopSize;
        }

        private void ctlStop_Click(object sender, EventArgs e)
        {
            if (sender == ctlStopPressureValue || sender == ctlStopPressureLabel)
                ctlStopPressure.Checked = true;
            else if (sender == ctlStopSizeValue || sender == ctlStopSizeLabel)
                ctlStopSize.Checked = true;
        }

        private void ctlBatteryUpdate_Click(object sender, EventArgs e)
        {
            uint level = 101;

            Action<string> show = (string source) =>
            {
                if (level <= 100)
                {
                    ctlBatteryProgressBar.DisplayType = ProgressBarWithPercentage.TextDisplayType.Percent;
                    ctlBatteryProgressBar.Value = (int)level;
                    ctlBatteryGroupBox.Text = "Battery (" + source + "): --- LAST UPDATED: " + DateTime.Now.ToString();
                }
            };

            if (BtDevice.SendIoctl(BtDevice.IOCTL_GET_BATTERY, out level))
                show("Bluetooth");
            else if (UsbDevice.GetBattery(out level))
                show("USB");
            else
                MessageBox.Show("No Magic Trackpad 2 connected via USB or Bluetooth found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private delegate void delStringRefInt32Void(string _1, ref Int32 _2);

        private void Main_Load(object sender, EventArgs e)
        {
            ctlStopPressureValue.Location = new Point(ctlStopPressure.Right, ctlStopPressureValue.Location.Y);
            ctlStopPressureLabel.Location = new Point(ctlStopPressureValue.Right, ctlStopPressureLabel.Location.Y);

            ctlStopSizeValue.Location = new Point(ctlStopSize.Right, ctlStopSizeValue.Location.Y);
            ctlStopSizeLabel.Location = new Point(ctlStopSizeValue.Right, ctlStopSizeLabel.Location.Y);

            Int32 buttonDisabled = 0;
            Int32 feedbackClick = 0x060617;
            Int32 feedbackRelease = 0x000014;
            Int32 stopPressure = 0;
            Int32 stopSize = -1;
            Int32 ignoreButtonFinger = 1;
            Int32 ignoreNearFingers = 1;
            Int32 palmRejection = 1;

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters"))
                {
                    delStringRefInt32Void get = (string name, ref Int32 output) =>
                    {
                        try
                        {
                            output = (Int32)key.GetValue(name);
                        }
                        catch
                        {
                        }
                    };

                    get("ButtonDisabled", ref buttonDisabled);
                    get("FeedbackClick", ref feedbackClick);
                    get("FeedbackRelease", ref feedbackRelease);
                    get("StopPressure", ref stopPressure);
                    get("StopSize", ref stopSize);
                    get("IgnoreButtonFinger", ref ignoreButtonFinger);
                    get("IgnoreNearFingers", ref ignoreNearFingers);
                    get("PalmRejection", ref palmRejection);
                }
            }
            catch
            {
            }

            if (buttonDisabled != 0)
                ctlDisableFeedback.Checked = true;
            else if (feedbackClick == 0xffffff && feedbackRelease == 0xffffff)
                ctlMaximumFeedback.Checked = true;
            else
            {
                ctlMacOSClickOptions.Checked = true;

                if ((feedbackClick & 0xffff00) == 0 && (feedbackRelease & 0xffff00) == 0)
                    ctlSilentClicking.Checked = true;

                if ((feedbackClick & 0x0000ff) == 0x15 && (feedbackRelease & 0x0000ff) == 0x10)
                    ctlFeedback.Value = 0;
                else if ((feedbackClick & 0x0000ff) == 0x1e && (feedbackRelease & 0x0000ff) == 0x18)
                    ctlFeedback.Value = 2;
            }

            if (stopPressure == -1 && stopSize == -1)
                ctlStopDoNothing.Checked = true;
            else if (stopPressure != -1)
            {
                ctlStopPressure.Checked = true;
                ctlStopPressureValue.Text = stopPressure.ToString();
            }
            else
            {
                ctlStopSize.Checked = true;
                ctlStopSizeValue.Text = stopSize.ToString();
            }

            if (ignoreButtonFinger != 0)
                ctlIgnoreButtonFinger.Checked = true;

            if (ignoreNearFingers != 0)
                ctlIgnoreNearFingers.Checked = true;

            if (palmRejection != 0)
                ctlPalmRejection.Checked = true;
        }

        private bool SaveSettings()
        {
            Int32 buttonDisabled = 0;
            Int32 feedbackClick = 0x060617;
            Int32 feedbackRelease = 0x000014;
            Int32 stopPressure = 0;
            Int32 stopSize = -1;
            Int32 ignoreButtonFinger = 1;
            Int32 ignoreNearFingers = 1;
            Int32 palmRejection = 1;

            if (ctlDisableFeedback.Checked)
            {
                buttonDisabled = 1;
                feedbackClick = 0;
                feedbackRelease = 0;
            }
            else if (ctlMaximumFeedback.Checked)
            {
                buttonDisabled = 0;
                feedbackClick = 0xffffff;
                feedbackRelease = 0xffffff;
            }
            else
            {
                buttonDisabled = 0;
                feedbackClick = ctlFeedback.Value == 0 ? 0x040415 : ctlFeedback.Value == 1 ? 0x060617 : 0x08081e;
                feedbackRelease = ctlFeedback.Value == 0 ? 0x000010 : ctlFeedback.Value == 1 ? 0x000014 : 0x020218;

                if (ctlSilentClicking.Checked)
                {
                    feedbackClick = feedbackClick & 0x0000ff;
                    feedbackRelease = feedbackRelease & 0x0000ff;
                }
            }

            if (ctlStopDoNothing.Checked)
            {
                stopPressure = -1;
                stopSize = -1;
            }
            else if (ctlStopPressure.Checked)
            {
                stopSize = -1;

                if (!Int32.TryParse(ctlStopPressureValue.Text, out stopPressure) || stopPressure < 0)
                {
                    MessageBox.Show("Pressure must be greater than or equal to 0.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            else
            {
                stopPressure = -1;

                if (!Int32.TryParse(ctlStopSizeValue.Text, out stopSize) || stopSize < 0)
                {
                    MessageBox.Show("Size must be greater than or equal to 0.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            ignoreButtonFinger = ctlIgnoreButtonFinger.Checked ? 1 : 0;
            ignoreNearFingers = ctlIgnoreNearFingers.Checked ? 1 : 0;
            palmRejection = ctlPalmRejection.Checked ? 1 : 0;

            try
            {
                Action<string, string> save = (string key, string name) =>
                {
                    using (RegistryKey keyServices = Registry.LocalMachine.OpenSubKey(key, true))
                    using (RegistryKey keyAmtPtpDeviceUsbUm = keyServices.CreateSubKey(name, true))
                    using (RegistryKey keyParameters = keyAmtPtpDeviceUsbUm.CreateSubKey("Parameters", true))
                    {
                        keyParameters.SetValue("ButtonDisabled", buttonDisabled);
                        keyParameters.SetValue("FeedbackClick", feedbackClick);
                        keyParameters.SetValue("FeedbackRelease", feedbackRelease);
                        keyParameters.SetValue("StopPressure", stopPressure);
                        keyParameters.SetValue("StopSize", stopSize);
                        keyParameters.SetValue("IgnoreButtonFinger", ignoreButtonFinger);
                        keyParameters.SetValue("IgnoreNearFingers", ignoreNearFingers);
                        keyParameters.SetValue("PalmRejection", palmRejection);
                    }
                };

                save(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services", "AmtPtpDeviceUsbUm");
                save(@"SYSTEM\CurrentControlSet\Services", "AmtPtpHidFilter");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error writing to the registry: " + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }
    }

    //=================
    // Custom controls
    //=================

    public class ProgressBarWithPercentage : ProgressBar // from: https://cowthulu.com/winforms-progressbar-with-text/
    {
        public const int WM_PAINT = 0xF;
        public const int WS_EX_COMPOSITED = 0x2000_000;

        private TextDisplayType _style = TextDisplayType.Percent;
        private string _manualText = "";

        public ProgressBarWithPercentage()
        {
        }

        [Category("Appearance")]
        [Description("What type of text to display on the progress bar.")]
        public TextDisplayType DisplayType
        {
            get { return _style; }
            set
            {
                _style = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [Description("If DisplayType is Manual, the text to display.")]
        [Browsable(true), EditorBrowsable(EditorBrowsableState.Always)]
        public string ManualText
        {
            get { return _manualText; }
            set
            {
                _style = TextDisplayType.Manual;
                _manualText = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [Description("Color of text on bar.")]
        public Color TextColor { get; set; } = Color.White;

        [Browsable(true), EditorBrowsable(EditorBrowsableState.Always)]
        public override Font Font { get => base.Font; set => base.Font = value; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parms = base.CreateParams;
                parms.ExStyle |= WS_EX_COMPOSITED;
                return parms;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
                AdditionalPaint(m);
        }

        private void AdditionalPaint(Message m)
        {
            if (DisplayType == TextDisplayType.None)
                return;

            string text = GetDisplayText();

            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                Rectangle rect = new Rectangle(0, 0, Width, Height);
                StringFormat format = new StringFormat(StringFormatFlags.NoWrap)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Define offsets for the outline (1 pixel in each diagonal direction gives a nice result)
                Point[] offsets = {
                    new Point(-1, -1), new Point(0, -1), new Point(1, -1),
                    new Point(-1,  0),                   new Point(1,  0),
                    new Point(-1,  1), new Point(0,  1), new Point(1,  1)
                };

                // Draw black outline
                using (Brush outlineBrush = new SolidBrush(Color.Black))
                {
                    foreach (Point offset in offsets)
                    {
                        Rectangle outlineRect = new Rectangle(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
                        g.DrawString(text, Font, outlineBrush, outlineRect, format);
                    }
                }

                // Draw main text on top
                using (Brush textBrush = new SolidBrush(TextColor))
                {
                    g.DrawString(text, Font, textBrush, rect, format);
                }
            }
        }

        private string GetDisplayText()
        {
            string result = "";

            switch (DisplayType)
            {
                case TextDisplayType.Percent:
                    if (Maximum != 0)
                        result = ((int)(((float)Value / (float)Maximum) * 100)).ToString() + " %";
                    break;

                case TextDisplayType.Count:
                    result = Value.ToString() + " / " + Maximum.ToString();
                    break;

                case TextDisplayType.Manual:
                    result = ManualText;
                    break;
            }

            return result;
        }

        public enum TextDisplayType
        {
            None,
            Percent,
            Count,
            Manual
        }
    }

    //=========
    // Helpers
    //=========

    public class ButtonWait : IDisposable
    {
        public ButtonWait(Button btn)
        {
            _btn = btn;
            Cursor.Current = Cursors.WaitCursor;
            Application.UseWaitCursor = true;
            _btn.FindForm().Controls["ctlFocusHack"].Focus();
            _btn.Enabled = false;
            Application.DoEvents();
        }

        public void Dispose()
        {
            Cursor.Current = Cursors.Default;
            Application.UseWaitCursor = false;
            _btn.Enabled = true;
            _btn.Focus();
        }

        private Button _btn;
    }

    //=================
    // Low level stuff
    //=================

    public class UsbDevice
    {
        public static bool ReloadSettings()
        {
            object r = Send(CmdReloadSettings);
            return r == null;
        }

        public static bool GetBattery(out uint result)
        {
            object r = Send(CmdGetBattery);
            if (r is uint)
            {
                result = (uint)r;
                return true;
            }
            else
            {
                result = 0;
                return false;
            }
        }

        // named pipe client

        private const string PipeName = "AmtPtpControlPanelUsbUmInterface";

        private const byte CmdReloadSettings = 0x00;
        private const byte CmdGetBattery = 0x01;

        private const byte StatusOk = 0x00;
        private const byte StatusFail = 0xFF;
        private const byte StatusUnknown = 0xFE;

        private static object Send(byte cmd)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(250);

                    // Send command byte.
                    pipe.WriteByte(cmd);
                    pipe.Flush();

                    // Read 2-byte response [status][payload].
                    var response = new byte[2];
                    int read = 0;
                    while (read < response.Length)
                    {
                        int n = pipe.Read(response, read, response.Length - read);
                        if (n <= 0)
                            throw new IOException("Pipe closed before full response.");
                        read += n;
                    }

                    if (read != 2)
                        throw new IOException("Invalid size of response.");

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

    class BtDevice
    {
        private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;

        private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access)
        {
            return (deviceType << 16) | (access << 14) | (function << 2) | method;
        }

        public static readonly uint IOCTL_RELOAD_SETTINGS = CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS);
        public static readonly uint IOCTL_GET_BATTERY = CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS);

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
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        public static bool SendIoctl(uint code, bool showMessageBox = false)
        {
            return ExecuteIoctl(code, out _, false, showMessageBox);
        }

        public static bool SendIoctl(uint code, out uint result, bool showMessageBox = false)
        {
            return ExecuteIoctl(code, out result, true, showMessageBox);
        }

        private static bool ExecuteIoctl(uint code, out uint result, bool expectData, bool showMessageBox)
        {
            result = 0;
            SafeFileHandle hDevice = null;
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
                    IntPtr.Zero
                );

                if (hDevice.IsInvalid)
                {
                    if (showMessageBox)
                    {
                        MessageBox.Show($"Failed to open device. Error: {Marshal.GetLastWin32Error()}");
                    }
                    return false;
                }

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
                    IntPtr.Zero
                );

                if (success && expectData)
                {
                    result = (uint)Marshal.ReadInt32(pOutBuffer);
                }

                if (!success && showMessageBox)
                {
                    MessageBox.Show($"DeviceIoControl failed. Error: {Marshal.GetLastWin32Error()}");
                }

                return success;
            }
            finally
            {
                if (pOutBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pOutBuffer);
                }
                hDevice?.Dispose();
            }
        }
    }
}
