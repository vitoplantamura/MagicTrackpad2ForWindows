using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Threading;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Reflection;
using Microsoft.Win32;

namespace AmtPtpControlPanel
{
    public partial class Main : Form
    {
        public Main()
        {
            InitializeComponent();
        }

        private void ctlInstallDriver_Click(object sender, EventArgs e)
        {
            string fn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\DriverStore\FileRepository\amtptpdevice.inf_amd64_5de6239780ba286e\AmtPtpDeviceUsbUm.dll");
            string src = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                @"AmtPtpDeviceUsbUm.dll");

            Action<string> showErr = (string str) =>
            {
                MessageBox.Show(str, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            if (!File.Exists(src))
            {
                showErr("\"" + src + "\" doesn't exist.");
                return;
            }

            if (!File.Exists(fn))
            {
                showErr("\"" + fn + "\" doesn't exist.");
                return;
            }

            List<string> errs = new List<string>();
            Action<string, Exception> err = (string str, Exception ex) =>
            {
                errs.Add(str + (ex == null ? "" : ": " + ex.ToString()));
            };

            Action replaceDriver = () =>
            {
                try
                {
                    if (!TokenManipulator.AddPrivilege("SeRestorePrivilege"))
                        throw new Exception("TokenManipulator.AddPrivilege of SeRestorePrivilege returned FALSE.");
                    if (!TokenManipulator.AddPrivilege("SeTakeOwnershipPrivilege"))
                        throw new Exception("TokenManipulator.AddPrivilege of SeTakeOwnershipPrivilege returned FALSE.");
                }
                catch (Exception ex)
                {
                    try
                    {
                        TokenManipulator.RemovePrivilege("SeRestorePrivilege");
                    }
                    catch
                    {
                    }

                    try
                    {
                        TokenManipulator.RemovePrivilege("SeTakeOwnershipPrivilege");
                    }
                    catch
                    {
                    }

                    err("AddPrivilege failed", ex);
                    return;
                }

                NTAccount prevOwner = null;
                try
                {
                    var fs = File.GetAccessControl(fn);
                    prevOwner = (NTAccount)fs.GetOwner(typeof(NTAccount));
                }
                catch (Exception ex)
                {
                    err("GetOwner failed", ex);
                }

                bool ownerSet = false;
                if (prevOwner != null)
                    try
                    {
                        var fs = File.GetAccessControl(fn);
                        fs.SetOwner(WindowsIdentity.GetCurrent().User);
                        File.SetAccessControl(fn, fs);
                        ownerSet = true;
                    }
                    catch (Exception ex)
                    {
                        err("SetOwner #1 failed", ex);
                    }

                FileSystemAccessRule accessRule = null;
                if (ownerSet)
                    try
                    {
                        accessRule = new FileSystemAccessRule(
                            WindowsIdentity.GetCurrent().User,
                            FileSystemRights.FullControl,
                            InheritanceFlags.None,
                            PropagationFlags.NoPropagateInherit,
                            AccessControlType.Allow);
                    }
                    catch (Exception ex)
                    {
                        err("FileSystemAccessRule creation failed", ex);
                    }

                bool accessRuleAdded = false;
                if (accessRule != null)
                    try
                    {
                        var fs = File.GetAccessControl(fn);
                        fs.AddAccessRule(accessRule);
                        File.SetAccessControl(fn, fs);
                        accessRuleAdded = true;
                    }
                    catch (Exception ex)
                    {
                        err("AddAccessRule failed", ex);
                    }

                if (accessRuleAdded)
                    try
                    {
                        File.Copy(src, fn, true);
                    }
                    catch (Exception ex)
                    {
                        err("Copy failed", ex);
                    }

                if (accessRuleAdded)
                    try
                    {
                        var fs = File.GetAccessControl(fn);
                        fs.RemoveAccessRule(accessRule);
                        File.SetAccessControl(fn, fs);
                    }
                    catch (Exception ex)
                    {
                        err("RemoveAccessRule failed", ex);
                    }

                if (prevOwner != null)
                    try
                    {
                        var fs = File.GetAccessControl(fn);
                        fs.SetOwner(prevOwner);
                        File.SetAccessControl(fn, fs);
                    }
                    catch (Exception ex)
                    {
                        err("SetOwner #2 failed", ex);
                    }

                try
                {
                    if (!TokenManipulator.RemovePrivilege("SeRestorePrivilege"))
                        throw new Exception("TokenManipulator.RemovePrivilege of SeRestorePrivilege returned FALSE.");
                }
                catch (Exception ex)
                {
                    err("RemovePrivilege failed", ex);
                }

                try
                {
                    if (!TokenManipulator.RemovePrivilege("SeTakeOwnershipPrivilege"))
                        throw new Exception("TokenManipulator.RemovePrivilege of SeTakeOwnershipPrivilege returned FALSE.");
                }
                catch (Exception ex)
                {
                    err("RemovePrivilege failed", ex);
                }
            };

            using (new ButtonWait((Button)sender))
                Device.RestartDevices(replaceDriver);

            if (errs.Count == 0)
                MessageBox.Show("Operation succeeded!");
            else
                foreach (string errStr in errs)
                    showErr(errStr);
        }

        private void ctlApply_Click(object sender, EventArgs e)
        {
            using (new ButtonWait((Button)sender))
                if (SaveSettings())
                    Device.RestartDevices();
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

        private delegate void delStringRefInt32Void(string _1, ref Int32 _2);

        private void Main_Load(object sender, EventArgs e)
        {
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
                using (RegistryKey keyServices = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services", true))
                using (RegistryKey keyAmtPtpDeviceUsbUm = keyServices.CreateSubKey("AmtPtpDeviceUsbUm", true))
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
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error writing to the registry: " + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
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

    public class TokenManipulator // https://stackoverflow.com/questions/17031552/how-do-you-take-file-ownership-with-powershell/17047190#17047190
    {
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TokPriv1Luid
        {
            public int Count;
            public long Luid;
            public int Attr;
        }

        internal const int SE_PRIVILEGE_DISABLED = 0x00000000;
        internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const int TOKEN_QUERY = 0x00000008;
        internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;

        public const string SE_ASSIGNPRIMARYTOKEN_NAME = "SeAssignPrimaryTokenPrivilege";
        public const string SE_AUDIT_NAME = "SeAuditPrivilege";
        public const string SE_BACKUP_NAME = "SeBackupPrivilege";
        public const string SE_CHANGE_NOTIFY_NAME = "SeChangeNotifyPrivilege";
        public const string SE_CREATE_GLOBAL_NAME = "SeCreateGlobalPrivilege";
        public const string SE_CREATE_PAGEFILE_NAME = "SeCreatePagefilePrivilege";
        public const string SE_CREATE_PERMANENT_NAME = "SeCreatePermanentPrivilege";
        public const string SE_CREATE_SYMBOLIC_LINK_NAME = "SeCreateSymbolicLinkPrivilege";
        public const string SE_CREATE_TOKEN_NAME = "SeCreateTokenPrivilege";
        public const string SE_DEBUG_NAME = "SeDebugPrivilege";
        public const string SE_ENABLE_DELEGATION_NAME = "SeEnableDelegationPrivilege";
        public const string SE_IMPERSONATE_NAME = "SeImpersonatePrivilege";
        public const string SE_INC_BASE_PRIORITY_NAME = "SeIncreaseBasePriorityPrivilege";
        public const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
        public const string SE_INC_WORKING_SET_NAME = "SeIncreaseWorkingSetPrivilege";
        public const string SE_LOAD_DRIVER_NAME = "SeLoadDriverPrivilege";
        public const string SE_LOCK_MEMORY_NAME = "SeLockMemoryPrivilege";
        public const string SE_MACHINE_ACCOUNT_NAME = "SeMachineAccountPrivilege";
        public const string SE_MANAGE_VOLUME_NAME = "SeManageVolumePrivilege";
        public const string SE_PROF_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
        public const string SE_RELABEL_NAME = "SeRelabelPrivilege";
        public const string SE_REMOTE_SHUTDOWN_NAME = "SeRemoteShutdownPrivilege";
        public const string SE_RESTORE_NAME = "SeRestorePrivilege";
        public const string SE_SECURITY_NAME = "SeSecurityPrivilege";
        public const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
        public const string SE_SYNC_AGENT_NAME = "SeSyncAgentPrivilege";
        public const string SE_SYSTEM_ENVIRONMENT_NAME = "SeSystemEnvironmentPrivilege";
        public const string SE_SYSTEM_PROFILE_NAME = "SeSystemProfilePrivilege";
        public const string SE_SYSTEMTIME_NAME = "SeSystemtimePrivilege";
        public const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";
        public const string SE_TCB_NAME = "SeTcbPrivilege";
        public const string SE_TIME_ZONE_NAME = "SeTimeZonePrivilege";
        public const string SE_TRUSTED_CREDMAN_ACCESS_NAME = "SeTrustedCredManAccessPrivilege";
        public const string SE_UNDOCK_NAME = "SeUndockPrivilege";
        public const string SE_UNSOLICITED_INPUT_NAME = "SeUnsolicitedInputPrivilege";

        public static bool AddPrivilege(string privilege)
        {
            try
            {
                bool retVal;
                TokPriv1Luid tp;
                IntPtr hproc = GetCurrentProcess();
                IntPtr htok = IntPtr.Zero;
                retVal = OpenProcessToken(hproc, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref htok);
                tp.Count = 1;
                tp.Luid = 0;
                tp.Attr = SE_PRIVILEGE_ENABLED;
                retVal = LookupPrivilegeValue(null, privilege, ref tp.Luid);
                retVal = AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                return retVal;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static bool RemovePrivilege(string privilege)
        {
            try
            {
                bool retVal;
                TokPriv1Luid tp;
                IntPtr hproc = GetCurrentProcess();
                IntPtr htok = IntPtr.Zero;
                retVal = OpenProcessToken(hproc, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref htok);
                tp.Count = 1;
                tp.Luid = 0;
                tp.Attr = SE_PRIVILEGE_DISABLED;
                retVal = LookupPrivilegeValue(null, privilege, ref tp.Luid);
                retVal = AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                return retVal;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }

    public class Device
    {
        public static bool RestartDevices(Action action = null)
        {
            Guid guid = new Guid("4a5064e5-7d39-41d1-a0e4-81097edce967"); // <-- driver device interface

            bool success = false;
            IntPtr deviceInfoSet = EnableDevices(false, guid, INVALID_HANDLE_VALUE, ref success);

            if (action != null)
                action();

            if (success)
                EnableDevices(true, guid, deviceInfoSet, ref success);

            if (deviceInfoSet != INVALID_HANDLE_VALUE)
                SetupDiDestroyDeviceInfoList(deviceInfoSet);

            return success;
        }

        public static IntPtr EnableDevices(bool enable, Guid guid, IntPtr deviceInfoSetOverride, ref bool success)
        {
            IntPtr deviceInfoSet = deviceInfoSetOverride != INVALID_HANDLE_VALUE ? deviceInfoSetOverride :
                SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceInfoSet == INVALID_HANDLE_VALUE)
                return INVALID_HANDLE_VALUE;

            uint index = 0;

            while (true)
            {
                SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = (UInt32)Marshal.SizeOf(devInfo);
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfo))
                    break;
                else
                    index++;

                SP_PROPCHANGE_PARAMS propChange = new SP_PROPCHANGE_PARAMS();
                propChange.ClassInstallHeader = new SP_CLASSINSTALL_HEADER();
                propChange.ClassInstallHeader.cbSize = (UInt32)Marshal.SizeOf(propChange.ClassInstallHeader);
                propChange.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
                propChange.Scope = DICS_FLAG_GLOBAL;
                propChange.StateChange = enable ? DICS_ENABLE : DICS_DISABLE;

                if (SetupDiSetClassInstallParams(deviceInfoSet, ref devInfo, ref propChange, Marshal.SizeOf(propChange)))
                {
                    if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, ref devInfo))
                    {
                        success = true;
                    }
                }
            }

            return deviceInfoSet;
        }

        // P/Invoke:

        static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        const int DIGCF_DEFAULT = 0x1;
        const int DIGCF_PRESENT = 0x2;
        const int DIGCF_ALLCLASSES = 0x4;
        const int DIGCF_PROFILE = 0x8;
        const int DIGCF_DEVICEINTERFACE = 0x10;

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SetupDiGetClassDevs(
           ref Guid ClassGuid,
           IntPtr Enumerator,
           IntPtr hwndParent,
           int Flags
        );

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList
        (
                IntPtr DeviceInfoSet
        );

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public UInt32 cbSize;
            public Guid ClassGuid;
            public UInt32 DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool SetupDiSetClassInstallParams(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref SP_PROPCHANGE_PARAMS ClassInstallParams, int ClassInstallParamsSize);

        [StructLayout(LayoutKind.Sequential)]
        struct SP_CLASSINSTALL_HEADER
        {
            public UInt32 cbSize;
            public UInt32 InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public UInt32 StateChange;
            public UInt32 Scope;
            public UInt32 HwProfile;
        }

        const uint DIF_PROPERTYCHANGE = 0x12;
        const uint DICS_ENABLE = 1;
        const uint DICS_DISABLE = 2;
        const uint DICS_FLAG_GLOBAL = 1;

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiCallClassInstaller(
             UInt32 InstallFunction,
             IntPtr DeviceInfoSet,
             ref SP_DEVINFO_DATA DeviceInfoData
        );
    }
}
