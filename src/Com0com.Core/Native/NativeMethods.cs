using System.Runtime.InteropServices;
using System.Text;

namespace Com0com.Core.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVINFO_DATA
{
    public uint cbSize;
    public Guid ClassGuid;
    public uint DevInst;
    public IntPtr Reserved;

    public static SP_DEVINFO_DATA Create() => new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_CLASSINSTALL_HEADER
{
    public uint cbSize;
    public uint InstallFunction;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_PROPCHANGE_PARAMS
{
    public SP_CLASSINSTALL_HEADER ClassInstallHeader;
    public uint StateChange;
    public uint Scope;
    public uint HwProfile;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SP_DEVINSTALL_PARAMS
{
    public uint cbSize;
    public uint Flags;
    public uint FlagsEx;
    public IntPtr hwndParent;
    public IntPtr InstallMsgHandler;
    public IntPtr InstallMsgHandlerContext;
    public IntPtr FileQueue;
    public UIntPtr ClassInstallReserved;
    public uint Reserved;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string DriverPath;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INFCONTEXT
{
    public IntPtr Inf;
    public IntPtr CurrentInf;
    public uint Section;
    public uint Line;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_STATUS
{
    public uint dwServiceType;
    public uint dwCurrentState;
    public uint dwControlsAccepted;
    public uint dwWin32ExitCode;
    public uint dwServiceSpecificExitCode;
    public uint dwCheckPoint;
    public uint dwWaitHint;
}

internal static class NativeMethods
{
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    // DIGCF
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_ALLCLASSES = 0x04;

    // SPDRP
    public const uint SPDRP_DEVICEDESC = 0x00;
    public const uint SPDRP_HARDWAREID = 0x01;
    public const uint SPDRP_FRIENDLYNAME = 0x0C;
    public const uint SPDRP_LOCATION_INFORMATION = 0x0D;
    public const uint SPDRP_PHYSICAL_DEVICE_OBJECT_NAME = 0x0E;

    // DIF
    public const uint DIF_REMOVE = 0x05;
    public const uint DIF_PROPERTYCHANGE = 0x12;
    public const uint DIF_REGISTERDEVICE = 0x19;

    // DICS
    public const uint DICS_ENABLE = 1;
    public const uint DICS_DISABLE = 2;
    public const uint DICS_PROPCHANGE = 3;
    public const uint DICS_FLAG_GLOBAL = 1;
    public const uint DICS_FLAG_CONFIGSPECIFIC = 2;

    public const uint DIREG_DEV = 1;
    public const uint DICD_GENERATE_ID = 1;

    public const uint DI_NEEDRESTART = 0x00000080;
    public const uint DI_NEEDREBOOT = 0x00000100;
    public const uint DI_QUIETINSTALL = 0x00800000;

    public const uint INSTALLFLAG_FORCE = 0x00000001;

    public const uint SPOST_NONE = 0;
    public const uint SPOST_PATH = 1;
    public const uint SP_COPY_REPLACEONLY = 0x0000002;
    public const uint SP_COPY_NOOVERWRITE = 0x0000008;
    public const uint INF_STYLE_WIN4 = 0x00000002;

    // CfgMgr
    public const uint CR_SUCCESS = 0;
    public const uint DN_NEED_RESTART = 0x00000100;
    public const uint DN_HAS_PROBLEM = 0x00000400;
    public const uint CM_PROB_DISABLED = 0x00000016;

    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;

    // Registry access
    public const uint KEY_READ = 0x20019;
    public const uint KEY_WRITE = 0x20006;

    // Win32 errors
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_SHARING_VIOLATION = 32;
    public const int ERROR_FILE_EXISTS = 80;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    public const int ERROR_IN_WOW64 = unchecked((int)0xE0000235);
    public const int ERROR_NO_SUCH_DEVINST = unchecked((int)0xE000020B);
    public const int ERROR_DEVINST_ALREADY_EXISTS = unchecked((int)0xE0000207);

    // Services
    public const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    public const uint DELETE = 0x00010000;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_STOPPED = 1;

    public const uint CDB_REPORT_BITS = 0;
    public const int COMDB_MAX_PORTS_ARBITRATED = 4096;

    // ---------------------------------------------------------------- setupapi
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, byte[] propertyBuffer, uint propertyBufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint scope, uint hwProfile, uint keyType, uint samDesired);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiCreateDevRegKeyW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint scope, uint hwProfile, uint keyType, IntPtr infHandle, string? infSectionName);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiSetClassInstallParamsW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, uint classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstallParamsW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_DEVINSTALL_PARAMS deviceInstallParams);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetINFClassW(string infName, out Guid classGuid, StringBuilder className,
        uint classNameSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiCreateDeviceInfoW(IntPtr deviceInfoSet, string deviceName, ref Guid classGuid,
        string? deviceDescription, IntPtr hwndParent, uint creationFlags, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiInstallClassW(IntPtr hwndParent, string infFileName, uint flags, IntPtr fileQueue);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupCopyOEMInfW(string sourceInfFileName, string? oemSourceMediaLocation,
        uint oemSourceMediaType, uint copyStyle, StringBuilder? destinationInfFileName, uint destinationInfFileNameSize,
        IntPtr requiredSize, IntPtr destinationInfFileNameComponent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupUninstallOEMInfW(string infFileName, uint flags, IntPtr reserved);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupOpenInfFileW(string fileName, string? infClass, uint infStyle, out uint errorLine);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern void SetupCloseInfFile(IntPtr infHandle);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupFindFirstLineW(IntPtr infHandle, string section, string? key, out INFCONTEXT context);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupFindNextMatchLineW(ref INFCONTEXT contextIn, string? key, out INFCONTEXT contextOut);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupGetStringFieldW(ref INFCONTEXT context, uint fieldIndex, StringBuilder? returnBuffer,
        uint returnBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupGetInfFileListW(string? directoryPath, uint infStyle, char[]? returnBuffer,
        uint returnBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern uint CMP_WaitNoPendingInstallEvents(uint timeOut);

    // ---------------------------------------------------------------- newdev
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwndParent, string hardwareId,
        string fullInfPath, uint installFlags, [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);

    // ---------------------------------------------------------------- cfgmgr32
    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Reenumerate_DevNode(uint devInst, uint flags);

    // ---------------------------------------------------------------- msports
    [DllImport("msports.dll", SetLastError = true)]
    public static extern int ComDBOpen(out IntPtr hComDB);

    [DllImport("msports.dll", SetLastError = true)]
    public static extern int ComDBClose(IntPtr hComDB);

    [DllImport("msports.dll", SetLastError = true)]
    public static extern int ComDBGetCurrentPortUsage(IntPtr hComDB, byte[]? buffer, uint bufferSize,
        uint reportType, out uint maxPortsReported);

    [DllImport("msports.dll", SetLastError = true)]
    public static extern int ComDBClaimPort(IntPtr hComDB, uint comNumber, [MarshalAs(UnmanagedType.Bool)] bool forceClaim,
        IntPtr forced);

    [DllImport("msports.dll", SetLastError = true)]
    public static extern int ComDBReleasePort(IntPtr hComDB, uint comNumber);

    // ---------------------------------------------------------------- kernel32
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint QueryDosDeviceW(string? deviceName, char[] targetPath, uint max);

    // ---------------------------------------------------------------- advapi32
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenServiceW(IntPtr hSCManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);
}
