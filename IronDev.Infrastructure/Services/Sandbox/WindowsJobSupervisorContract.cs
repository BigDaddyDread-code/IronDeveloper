using System.Text;
using System.Security.Cryptography;
using IronDev.Core.Sandbox;

namespace IronDev.Infrastructure.Services.Sandbox;

/// <summary>
/// Exact source artifact compiled inside the isolated container. Its SHA-256 is part of
/// the canonical sandbox policy, rechecked before Add-Type, and returned in runtime
/// evidence. The trusted PowerShell loader and this supervisor are outside the nested
/// workload job; the project-controlled root and all of its descendants are inside it.
/// </summary>
public static class WindowsJobSupervisorContract
{
    public const string Version = "irondev-windows-job-supervisor-v1";
    public const string WorkloadProcessScope = "UntrustedWorkloadTreeIncludingRoot";
    public const string ContainerAdministratorSid = "S-1-5-93-2-1";
    public const string ProofMarker = "IRONDEV_SUPERVISOR_PROOF:";
    public const string ContainerDirectory = @"C:\IronDev\Supervisor";
    public const string LoaderContainerPath = @"C:\IronDev\Supervisor\loader.ps1";
    public const string SourceContainerPath = @"C:\IronDev\Supervisor\supervisor.cs";
    public const string LoaderFileName = "loader.ps1";
    public const string SourceFileName = "supervisor.cs";
    public const string HostStagingOwnerSuffix = ".owner.json";

    public static string SourceSha256 { get; } = SandboxCanonicalJson.Sha256(SupervisorSource);

    public static byte[] GetLoaderBytes() => new UTF8Encoding(false).GetBytes(LoaderScript);

    public static byte[] GetSourceBytes() => new UTF8Encoding(false).GetBytes(SupervisorSource);

    public static byte[] GetBootstrapBytes() => Encoding.Unicode.GetBytes(BootstrapScript);

    public static string LoaderSha256 => HexSha256(GetLoaderBytes());

    public static string BootstrapSha256 => HexSha256(GetBootstrapBytes());

    public static int BrokerProbeEncodedCommandLength =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(BrokerProbeScript)).Length;

    // The policy SHA commits to the exact bytes of all three fixed artifacts through
    // their component hashes. Loader/source are staged as exact files; bootstrap is the
    // exact UTF-16LE byte sequence supplied to PowerShell -EncodedCommand.
    public static string Sha256 => SandboxCanonicalJson.Sha256(
        $"{BootstrapSha256}:{LoaderSha256}:{SourceSha256}");

    private static string HexSha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public const string SupervisorSource = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.IO;
        using System.Runtime.InteropServices;
        using System.Text;
        using System.Threading;

        namespace IronDevSandbox
        {
            public sealed class ProcessLimitProof
            {
                public int ActiveProcessLimit { get; set; }
                public uint LimitFlags { get; set; }
                public bool FirstSixtyFourAssignmentsSucceeded { get; set; }
                public bool SixtyFifthAssignmentRejected { get; set; }
                public bool SuspendedAssignmentBeforeResume { get; set; }
                public bool AllProbeProcessesTerminated { get; set; }
                public bool RestrictedLowIntegrityToken { get; set; }
                public bool SupervisorHandleIsolation { get; set; }
            }

            public sealed class WorkloadRunResult
            {
                public int ExitCode { get; set; }
                public bool TimedOut { get; set; }
            }

            public static class WorkloadSupervisor
            {
                public const string ContractVersion = "irondev-windows-job-supervisor-v1";
                private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
                private const uint JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x00000800;
                private const uint JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x00001000;
                private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
                private const uint RequiredLimitFlags =
                    JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                private const uint CREATE_SUSPENDED = 0x00000004;
                private const uint CREATE_NO_WINDOW = 0x08000000;
                private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
                private const uint STARTF_USESTDHANDLES = 0x00000100;
                private const uint GENERIC_READ = 0x80000000;
                private const uint GENERIC_WRITE = 0x40000000;
                private const uint FILE_SHARE_READ = 0x00000001;
                private const uint FILE_SHARE_WRITE = 0x00000002;
                private const uint CREATE_ALWAYS = 2;
                private const uint OPEN_EXISTING = 3;
                private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
                private const uint WAIT_OBJECT_0 = 0x00000000;
                private const uint WAIT_TIMEOUT = 0x00000102;
                private const uint WAIT_FAILED = 0xffffffff;
                private const uint PROCESS_TERMINATE = 0x00000001;
                private const uint PROCESS_DUP_HANDLE = 0x00000040;
                private const uint DACL_SECURITY_INFORMATION = 0x00000004;
                private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
                private const uint TOKEN_DUPLICATE = 0x0002;
                private const uint TOKEN_QUERY = 0x0008;
                private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
                private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
                private const uint DISABLE_MAX_PRIVILEGE = 0x00000001;
                private const uint LUA_TOKEN = 0x00000004;
                private const uint WRITE_RESTRICTED = 0x00000008;
                private const uint SE_GROUP_INTEGRITY = 0x00000020;
                private const int TokenIntegrityLevel = 25;
                private static readonly IntPtr PROC_THREAD_ATTRIBUTE_HANDLE_LIST = new IntPtr(0x00020002);
                private const int JobObjectBasicAccountingInformation = 1;
                private const int JobObjectExtendedLimitInformation = 9;

                [StructLayout(LayoutKind.Sequential)]
                private struct SECURITY_ATTRIBUTES
                {
                    public int nLength;
                    public IntPtr lpSecurityDescriptor;
                    [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
                }

                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                private struct STARTUPINFO
                {
                    public int cb;
                    public string lpReserved;
                    public string lpDesktop;
                    public string lpTitle;
                    public uint dwX;
                    public uint dwY;
                    public uint dwXSize;
                    public uint dwYSize;
                    public uint dwXCountChars;
                    public uint dwYCountChars;
                    public uint dwFillAttribute;
                    public uint dwFlags;
                    public short wShowWindow;
                    public short cbReserved2;
                    public IntPtr lpReserved2;
                    public IntPtr hStdInput;
                    public IntPtr hStdOutput;
                    public IntPtr hStdError;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct PROCESS_INFORMATION
                {
                    public IntPtr hProcess;
                    public IntPtr hThread;
                    public uint dwProcessId;
                    public uint dwThreadId;
                }

                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                private struct STARTUPINFOEX
                {
                    public STARTUPINFO StartupInfo;
                    public IntPtr lpAttributeList;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    public long PerProcessUserTimeLimit;
                    public long PerJobUserTimeLimit;
                    public uint LimitFlags;
                    public UIntPtr MinimumWorkingSetSize;
                    public UIntPtr MaximumWorkingSetSize;
                    public uint ActiveProcessLimit;
                    public UIntPtr Affinity;
                    public uint PriorityClass;
                    public uint SchedulingClass;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct IO_COUNTERS
                {
                    public ulong ReadOperationCount;
                    public ulong WriteOperationCount;
                    public ulong OtherOperationCount;
                    public ulong ReadTransferCount;
                    public ulong WriteTransferCount;
                    public ulong OtherTransferCount;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                    public IO_COUNTERS IoInfo;
                    public UIntPtr ProcessMemoryLimit;
                    public UIntPtr JobMemoryLimit;
                    public UIntPtr PeakProcessMemoryUsed;
                    public UIntPtr PeakJobMemoryUsed;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
                {
                    public long TotalUserTime;
                    public long TotalKernelTime;
                    public long ThisPeriodTotalUserTime;
                    public long ThisPeriodTotalKernelTime;
                    public uint TotalPageFaultCount;
                    public uint TotalProcesses;
                    public uint ActiveProcesses;
                    public uint TotalTerminatedProcesses;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct SID_AND_ATTRIBUTES
                {
                    public IntPtr Sid;
                    public uint Attributes;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct TOKEN_MANDATORY_LABEL
                {
                    public SID_AND_ATTRIBUTES Label;
                }

                [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);

                [DllImport("kernel32.dll")]
                private static extern IntPtr GetCurrentProcess();

                [DllImport("advapi32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool OpenProcessToken(
                    IntPtr process,
                    uint desiredAccess,
                    out IntPtr token);

                [DllImport("advapi32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool CreateRestrictedToken(
                    IntPtr existingToken,
                    uint flags,
                    uint disableSidCount,
                    IntPtr sidsToDisable,
                    uint deletePrivilegeCount,
                    IntPtr privilegesToDelete,
                    uint restrictedSidCount,
                    IntPtr sidsToRestrict,
                    out IntPtr newToken);

                [DllImport("advapi32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool SetTokenInformation(
                    IntPtr token,
                    int informationClass,
                    ref TOKEN_MANDATORY_LABEL information,
                    uint informationLength);

                [DllImport("advapi32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool IsTokenRestricted(IntPtr token);

                [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    string stringSecurityDescriptor,
                    uint stringSDRevision,
                    out IntPtr securityDescriptor,
                    out uint securityDescriptorSize);

                [DllImport("advapi32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool SetKernelObjectSecurity(
                    IntPtr handle,
                    uint securityInformation,
                    IntPtr securityDescriptor);

                [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool ConvertStringSidToSidW(string stringSid, out IntPtr sid);

                [DllImport("advapi32.dll")]
                private static extern uint GetLengthSid(IntPtr sid);

                [DllImport("kernel32.dll")]
                private static extern IntPtr LocalFree(IntPtr memory);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool SetInformationJobObject(
                    IntPtr job,
                    int informationClass,
                    ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information,
                    uint informationLength);

                [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool QueryExtendedLimitInformation(
                    IntPtr job,
                    int informationClass,
                    out JOBOBJECT_EXTENDED_LIMIT_INFORMATION information,
                    uint informationLength,
                    IntPtr returnLength);

                [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool QueryBasicAccountingInformation(
                    IntPtr job,
                    int informationClass,
                    out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information,
                    uint informationLength,
                    IntPtr returnLength);

                [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool CreateProcessAsUserW(
                    IntPtr token,
                    string applicationName,
                    StringBuilder commandLine,
                    IntPtr processAttributes,
                    IntPtr threadAttributes,
                    [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
                    uint creationFlags,
                    IntPtr environment,
                    string currentDirectory,
                    ref STARTUPINFO startupInfo,
                    out PROCESS_INFORMATION processInformation);

                [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, CharSet = CharSet.Unicode)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool CreateProcessAsUserWithAttributes(
                    IntPtr token,
                    string applicationName,
                    StringBuilder commandLine,
                    IntPtr processAttributes,
                    IntPtr threadAttributes,
                    [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
                    uint creationFlags,
                    IntPtr environment,
                    string currentDirectory,
                    ref STARTUPINFOEX startupInfo,
                    out PROCESS_INFORMATION processInformation);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool InitializeProcThreadAttributeList(
                    IntPtr attributeList,
                    int attributeCount,
                    int flags,
                    ref IntPtr size);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool UpdateProcThreadAttribute(
                    IntPtr attributeList,
                    uint flags,
                    IntPtr attribute,
                    IntPtr value,
                    IntPtr size,
                    IntPtr previousValue,
                    IntPtr returnSize);

                [DllImport("kernel32.dll")]
                private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

                [DllImport("kernel32.dll", SetLastError = true)]
                private static extern uint ResumeThread(IntPtr thread);

                [DllImport("kernel32.dll", SetLastError = true)]
                private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool TerminateProcess(IntPtr process, uint exitCode);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

                [DllImport("kernel32.dll", SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                private static extern bool CloseHandle(IntPtr handle);

                [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
                private static extern IntPtr CreateFileW(
                    string fileName,
                    uint desiredAccess,
                    uint shareMode,
                    ref SECURITY_ATTRIBUTES securityAttributes,
                    uint creationDisposition,
                    uint flagsAndAttributes,
                    IntPtr templateFile);

                public static ProcessLimitProof ProbeProcessLimit(int expectedLimit)
                {
                    if (expectedLimit != 64)
                        throw new InvalidOperationException("The v0.1 workload limit must be exactly 64.");

                    IntPtr job = IntPtr.Zero;
                    IntPtr restrictedToken = IntPtr.Zero;
                    var children = new List<PROCESS_INFORMATION>();
                    bool sixtyFifthRejected = false;
                    bool orderingProven = false;
                    try
                    {
                        bool handleIsolation = HardenSupervisorProcess();
                        restrictedToken = CreateLowIntegrityRestrictedToken();
                        job = CreateVerifiedJob(expectedLimit);
                        string command = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.System),
                            "cmd.exe");
                        for (int index = 0; index <= expectedLimit; index++)
                        {
                            PROCESS_INFORMATION child = CreateSuspended(
                                restrictedToken,
                                command,
                                new[] { "/d", "/c", "exit", "0" },
                                Environment.GetFolderPath(Environment.SpecialFolder.System),
                                IntPtr.Zero,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                false);
                            children.Add(child);
                            bool assigned = AssignProcessToJobObject(job, child.hProcess);
                            int assignmentError = assigned ? 0 : Marshal.GetLastWin32Error();
                            if (index < expectedLimit && !assigned)
                                throw new Win32Exception(
                                    assignmentError,
                                    "A suspended probe process could not be assigned.");
                            if (index == expectedLimit)
                            {
                                if (assigned)
                                    throw new InvalidOperationException("The 65th workload process was accepted.");
                                uint rejectionWait = WaitForSingleObject(child.hProcess, 1000);
                                sixtyFifthRejected =
                                    rejectionWait == WAIT_OBJECT_0 &&
                                    QueryAccounting(job).ActiveProcesses == expectedLimit;
                                if (!sixtyFifthRejected)
                                    throw new InvalidOperationException(
                                        "The 65th workload process was not terminated by the active-process limit.");
                            }
                        }

                        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = QueryLimits(job);
                        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting = QueryAccounting(job);
                        if (accounting.ActiveProcesses != expectedLimit)
                            throw new InvalidOperationException("The workload job did not report exactly 64 active probe processes.");

                        if (ResumeThread(children[0].hThread) == 0xffffffff)
                        {
                            int resumeError = Marshal.GetLastWin32Error();
                            throw new Win32Exception(
                                resumeError,
                                "An assigned suspended probe process could not be resumed.");
                        }
                        uint wait = WaitForSingleObject(children[0].hProcess, 10000);
                        if (wait != WAIT_OBJECT_0)
                            throw new InvalidOperationException("The assigned probe process did not run only after resume.");
                        orderingProven = true;

                        if (!TerminateJobObject(job, 0x49524456))
                            ThrowLastError("The workload probe job could not be terminated.");
                        WaitForNoActiveProcesses(job);

                        return new ProcessLimitProof
                        {
                            ActiveProcessLimit = (int)limits.BasicLimitInformation.ActiveProcessLimit,
                            LimitFlags = limits.BasicLimitInformation.LimitFlags,
                            FirstSixtyFourAssignmentsSucceeded = true,
                            SixtyFifthAssignmentRejected = sixtyFifthRejected,
                            SuspendedAssignmentBeforeResume = orderingProven,
                            AllProbeProcessesTerminated = QueryAccounting(job).ActiveProcesses == 0,
                            RestrictedLowIntegrityToken = IsTokenRestricted(restrictedToken),
                            SupervisorHandleIsolation = handleIsolation
                        };
                    }
                    finally
                    {
                        if (job != IntPtr.Zero)
                        {
                            TerminateJobObject(job, 0x49524456);
                            foreach (PROCESS_INFORMATION child in children)
                            {
                                if (child.hProcess != IntPtr.Zero)
                                    TerminateProcess(child.hProcess, 0x49524456);
                            }
                        }
                        foreach (PROCESS_INFORMATION child in children)
                        {
                            if (child.hThread != IntPtr.Zero) CloseHandle(child.hThread);
                            if (child.hProcess != IntPtr.Zero) CloseHandle(child.hProcess);
                        }
                        if (job != IntPtr.Zero) CloseHandle(job);
                        if (restrictedToken != IntPtr.Zero) CloseHandle(restrictedToken);
                    }
                }

                public static WorkloadRunResult Run(
                    string applicationPath,
                    string[] arguments,
                    string workingDirectory,
                    string standardOutputPath,
                    string standardErrorPath,
                    int expectedLimit,
                    int timeoutMilliseconds)
                {
                    if (expectedLimit != 64)
                        throw new InvalidOperationException("The v0.1 workload limit must be exactly 64.");
                    if (timeoutMilliseconds <= 0)
                        throw new InvalidOperationException("A positive fixed workload timeout is required.");
                    string normalizedWork = Path.GetFullPath(workingDirectory).TrimEnd('\\');
                    if (!String.Equals(normalizedWork, @"C:\IronDev\Scratch", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(normalizedWork, @"C:\IronDev\Scratch\Source", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The workload current directory is outside fixed scratch.");
                    IntPtr job = IntPtr.Zero;
                    IntPtr restrictedToken = IntPtr.Zero;
                    IntPtr input = IntPtr.Zero;
                    IntPtr output = IntPtr.Zero;
                    IntPtr error = IntPtr.Zero;
                    PROCESS_INFORMATION child = new PROCESS_INFORMATION();
                    try
                    {
                        HardenSupervisorProcess();
                        restrictedToken = CreateLowIntegrityRestrictedToken();
                        job = CreateVerifiedJob(expectedLimit);
                        SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES();
                        attributes.nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES));
                        attributes.bInheritHandle = true;
                        input = CreateFileW("NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                            ref attributes, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                        output = CreateFileW(standardOutputPath, GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE, ref attributes, CREATE_ALWAYS,
                            FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                        error = CreateFileW(standardErrorPath, GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE, ref attributes, CREATE_ALWAYS,
                            FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                        RequireHandle(input, "standard input");
                        RequireHandle(output, "standard output");
                        RequireHandle(error, "standard error");

                        child = CreateSuspended(
                            restrictedToken,
                            applicationPath,
                            arguments,
                            normalizedWork,
                            input,
                            output,
                            error,
                            true);
                        if (!AssignProcessToJobObject(job, child.hProcess))
                        {
                            int assignmentError = Marshal.GetLastWin32Error();
                            TerminateProcess(child.hProcess, 0x49524456);
                            throw new Win32Exception(
                                assignmentError,
                                "The suspended workload root could not be assigned to its job.");
                        }
                        if (QueryAccounting(job).ActiveProcesses != 1)
                            throw new InvalidOperationException("The suspended workload root assignment was not observable.");
                        if (ResumeThread(child.hThread) == 0xffffffff)
                        {
                            int resumeError = Marshal.GetLastWin32Error();
                            TerminateJobObject(job, 0x49524456);
                            throw new Win32Exception(
                                resumeError,
                                "The assigned workload root could not be resumed.");
                        }

                        uint wait = WaitForSingleObject(child.hProcess, (uint)timeoutMilliseconds);
                        if (wait == WAIT_TIMEOUT)
                        {
                            if (!TerminateJobObject(job, 0x49524456))
                                ThrowLastError("The timed-out workload job could not be terminated.");
                            WaitForNoActiveProcesses(job);
                            return new WorkloadRunResult { ExitCode = 124, TimedOut = true };
                        }
                        if (wait == WAIT_FAILED)
                            ThrowLastError("Waiting for the workload root failed.");
                        if (wait != WAIT_OBJECT_0)
                            throw new InvalidOperationException("The workload root returned an unexpected wait state.");
                        uint exitCode;
                        if (!GetExitCodeProcess(child.hProcess, out exitCode))
                            ThrowLastError("The workload root exit code could not be read.");

                        if (QueryAccounting(job).ActiveProcesses > 0)
                        {
                            if (!TerminateJobObject(job, 0x49524456))
                                ThrowLastError("Remaining workload descendants could not be terminated.");
                            WaitForNoActiveProcesses(job);
                        }
                        return new WorkloadRunResult
                        {
                            ExitCode = unchecked((int)exitCode),
                            TimedOut = false
                        };
                    }
                    finally
                    {
                        if (job != IntPtr.Zero) TerminateJobObject(job, 0x49524456);
                        if (child.hThread != IntPtr.Zero) CloseHandle(child.hThread);
                        if (child.hProcess != IntPtr.Zero) CloseHandle(child.hProcess);
                        if (input != IntPtr.Zero && input != new IntPtr(-1)) CloseHandle(input);
                        if (output != IntPtr.Zero && output != new IntPtr(-1)) CloseHandle(output);
                        if (error != IntPtr.Zero && error != new IntPtr(-1)) CloseHandle(error);
                        if (job != IntPtr.Zero) CloseHandle(job);
                        if (restrictedToken != IntPtr.Zero) CloseHandle(restrictedToken);
                    }
                }

                private static IntPtr CreateVerifiedJob(int expectedLimit)
                {
                    IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
                    RequireHandle(job, "unnamed workload job");
                    var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    limits.BasicLimitInformation.LimitFlags = RequiredLimitFlags;
                    limits.BasicLimitInformation.ActiveProcessLimit = (uint)expectedLimit;
                    uint length = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, length))
                    {
                        int error = Marshal.GetLastWin32Error();
                        CloseHandle(job);
                        throw new Win32Exception(error, "The fixed workload job limits could not be applied.");
                    }
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION observed = QueryLimits(job);
                    if (observed.BasicLimitInformation.ActiveProcessLimit != expectedLimit ||
                        observed.BasicLimitInformation.LimitFlags != RequiredLimitFlags ||
                        (observed.BasicLimitInformation.LimitFlags &
                         (JOB_OBJECT_LIMIT_BREAKAWAY_OK | JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK)) != 0)
                    {
                        CloseHandle(job);
                        throw new InvalidOperationException("The workload job limits did not survive exact inspection.");
                    }
                    return job;
                }

                private static bool HardenSupervisorProcess()
                {
                    IntPtr descriptor = IntPtr.Zero;
                    try
                    {
                        // Deny termination, injection, suspension, handle duplication,
                        // quota and process-information mutation to every future opener.
                        // The supervisor already owns every handle it needs.
                        const string sddl =
                            "D:P(D;;0x00000b6b;;;WD)(A;;0x001ff494;;;OW)(A;;0x001ff494;;;SY)";
                        uint length;
                        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                                sddl, 1, out descriptor, out length))
                            ThrowLastError("The supervisor process security descriptor could not be created.");
                        if (!SetKernelObjectSecurity(
                                GetCurrentProcess(),
                                DACL_SECURITY_INFORMATION,
                                descriptor))
                            ThrowLastError("The supervisor process handle boundary could not be applied.");
                        return true;
                    }
                    finally
                    {
                        if (descriptor != IntPtr.Zero) LocalFree(descriptor);
                    }
                }

                private static IntPtr CreateLowIntegrityRestrictedToken()
                {
                    IntPtr currentToken = IntPtr.Zero;
                    IntPtr restrictedToken = IntPtr.Zero;
                    IntPtr lowIntegritySid = IntPtr.Zero;
                    IntPtr worldSid = IntPtr.Zero;
                    IntPtr restrictedSids = IntPtr.Zero;
                    try
                    {
                        uint access = TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY |
                            TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID;
                        if (!OpenProcessToken(GetCurrentProcess(), access, out currentToken))
                            ThrowLastError("The trusted supervisor token could not be opened.");
                        if (!ConvertStringSidToSidW("S-1-1-0", out worldSid))
                            ThrowLastError("The workload restricting SID could not be created.");
                        var worldRestriction = new SID_AND_ATTRIBUTES();
                        worldRestriction.Sid = worldSid;
                        worldRestriction.Attributes = 0;
                        restrictedSids = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SID_AND_ATTRIBUTES)));
                        Marshal.StructureToPtr(worldRestriction, restrictedSids, false);
                        uint flags = DISABLE_MAX_PRIVILEGE | LUA_TOKEN | WRITE_RESTRICTED;
                        if (!CreateRestrictedToken(
                                currentToken,
                                flags,
                                0,
                                IntPtr.Zero,
                                0,
                                IntPtr.Zero,
                                1,
                                restrictedSids,
                                out restrictedToken))
                            ThrowLastError("A restricted workload token could not be created.");
                        if (!ConvertStringSidToSidW("S-1-16-4096", out lowIntegritySid))
                            ThrowLastError("The low-integrity workload SID could not be created.");
                        var label = new TOKEN_MANDATORY_LABEL();
                        label.Label.Sid = lowIntegritySid;
                        label.Label.Attributes = SE_GROUP_INTEGRITY;
                        uint length = (uint)Marshal.SizeOf(typeof(TOKEN_MANDATORY_LABEL)) +
                            GetLengthSid(lowIntegritySid);
                        if (!SetTokenInformation(
                                restrictedToken,
                                TokenIntegrityLevel,
                                ref label,
                                length))
                            ThrowLastError("The restricted workload token could not be set to low integrity.");
                        if (!IsTokenRestricted(restrictedToken))
                            throw new InvalidOperationException("The workload token is not restricted.");
                        IntPtr result = restrictedToken;
                        restrictedToken = IntPtr.Zero;
                        return result;
                    }
                    finally
                    {
                        if (lowIntegritySid != IntPtr.Zero) LocalFree(lowIntegritySid);
                        if (restrictedSids != IntPtr.Zero) Marshal.FreeHGlobal(restrictedSids);
                        if (worldSid != IntPtr.Zero) LocalFree(worldSid);
                        if (restrictedToken != IntPtr.Zero) CloseHandle(restrictedToken);
                        if (currentToken != IntPtr.Zero) CloseHandle(currentToken);
                    }
                }

                private static JOBOBJECT_EXTENDED_LIMIT_INFORMATION QueryLimits(IntPtr job)
                {
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION information;
                    uint length = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    if (!QueryExtendedLimitInformation(job, JobObjectExtendedLimitInformation,
                            out information, length, IntPtr.Zero))
                        ThrowLastError("The workload job limits could not be inspected.");
                    return information;
                }

                private static JOBOBJECT_BASIC_ACCOUNTING_INFORMATION QueryAccounting(IntPtr job)
                {
                    JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information;
                    uint length = (uint)Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION));
                    if (!QueryBasicAccountingInformation(job, JobObjectBasicAccountingInformation,
                            out information, length, IntPtr.Zero))
                        ThrowLastError("The workload job accounting could not be inspected.");
                    return information;
                }

                private static void WaitForNoActiveProcesses(IntPtr job)
                {
                    for (int attempt = 0; attempt < 200; attempt++)
                    {
                        if (QueryAccounting(job).ActiveProcesses == 0)
                            return;
                        Thread.Sleep(10);
                    }
                    throw new InvalidOperationException("The workload job retained a process after termination.");
                }

                private static PROCESS_INFORMATION CreateSuspended(
                    IntPtr restrictedToken,
                    string applicationPath,
                    string[] arguments,
                    string workingDirectory,
                    IntPtr standardInput,
                    IntPtr standardOutput,
                    IntPtr standardError,
                    bool inheritHandles)
                {
                    if (String.IsNullOrWhiteSpace(applicationPath) || !Path.IsPathRooted(applicationPath))
                        throw new InvalidOperationException("The workload application path must be absolute.");
                    if (String.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathRooted(workingDirectory))
                        throw new InvalidOperationException("The workload current directory must be absolute.");
                    var commandLine = new StringBuilder();
                    commandLine.Append(QuoteArgument(applicationPath));
                    foreach (string argument in arguments ?? new string[0])
                    {
                        commandLine.Append(' ');
                        commandLine.Append(QuoteArgument(argument ?? String.Empty));
                    }
                    if (commandLine.Length > 24000)
                        throw new InvalidOperationException("The restricted workload command line exceeds its fixed Windows bound.");
                    var startup = new STARTUPINFO();
                    startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                    if (inheritHandles)
                    {
                        startup.dwFlags = STARTF_USESTDHANDLES;
                        startup.hStdInput = standardInput;
                        startup.hStdOutput = standardOutput;
                        startup.hStdError = standardError;
                    }
                    PROCESS_INFORMATION process;
                    if (!inheritHandles)
                    {
                        if (!CreateProcessAsUserW(
                                restrictedToken,
                                applicationPath,
                                commandLine,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                false,
                                CREATE_SUSPENDED | CREATE_NO_WINDOW,
                                IntPtr.Zero,
                                workingDirectory,
                                ref startup,
                                out process))
                            ThrowLastError("The workload process could not be created suspended.");
                        return process;
                    }

                    IntPtr attributeList = IntPtr.Zero;
                    IntPtr handleList = IntPtr.Zero;
                    bool attributeListInitialized = false;
                    try
                    {
                        IntPtr attributeBytes = IntPtr.Zero;
                        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
                        if (attributeBytes == IntPtr.Zero)
                            ThrowLastError("The inherited-handle allow-list size could not be resolved.");
                        attributeList = Marshal.AllocHGlobal(attributeBytes);
                        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeBytes))
                            ThrowLastError("The inherited-handle allow-list could not be initialized.");
                        attributeListInitialized = true;
                        handleList = Marshal.AllocHGlobal(IntPtr.Size * 3);
                        Marshal.WriteIntPtr(handleList, 0, standardInput);
                        Marshal.WriteIntPtr(handleList, IntPtr.Size, standardOutput);
                        Marshal.WriteIntPtr(handleList, IntPtr.Size * 2, standardError);
                        if (!UpdateProcThreadAttribute(
                                attributeList,
                                0,
                                PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                                handleList,
                                new IntPtr(IntPtr.Size * 3),
                                IntPtr.Zero,
                                IntPtr.Zero))
                            ThrowLastError("The inherited-handle allow-list could not be applied.");
                        var extended = new STARTUPINFOEX();
                        extended.StartupInfo = startup;
                        extended.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
                        extended.lpAttributeList = attributeList;
                        if (!CreateProcessAsUserWithAttributes(
                                restrictedToken,
                                applicationPath,
                                commandLine,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                true,
                                CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
                                IntPtr.Zero,
                                workingDirectory,
                                ref extended,
                                out process))
                            ThrowLastError("The workload process could not be created suspended with exact handles.");
                        return process;
                    }
                    finally
                    {
                        if (attributeList != IntPtr.Zero)
                        {
                            if (attributeListInitialized)
                                DeleteProcThreadAttributeList(attributeList);
                            Marshal.FreeHGlobal(attributeList);
                        }
                        if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
                    }
                }

                private static string QuoteArgument(string value)
                {
                    if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                        return value;
                    var result = new StringBuilder();
                    result.Append('"');
                    int slashes = 0;
                    foreach (char character in value)
                    {
                        if (character == '\\')
                        {
                            slashes++;
                            continue;
                        }
                        if (character == '"')
                        {
                            result.Append('\\', slashes * 2 + 1);
                            result.Append('"');
                            slashes = 0;
                            continue;
                        }
                        result.Append('\\', slashes);
                        slashes = 0;
                        result.Append(character);
                    }
                    result.Append('\\', slashes * 2);
                    result.Append('"');
                    return result.ToString();
                }

                private static void RequireHandle(IntPtr handle, string label)
                {
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                        ThrowLastError("The " + label + " handle could not be created.");
                }

                private static void ThrowLastError(string message)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), message);
                }
            }
        }
        """;

    private const string BrokerProbeScript = """
        $ErrorActionPreference = 'Stop'
        $nativeProbe = @'
        using System;
        using System.Runtime.InteropServices;
        using System.Text;

        public static class IronDevBoundaryProbe
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct STARTUPINFO
            {
                public int cb;
                public string reserved;
                public string desktop;
                public string title;
                public uint x;
                public uint y;
                public uint xSize;
                public uint ySize;
                public uint xChars;
                public uint yChars;
                public uint fill;
                public uint flags;
                public short show;
                public short reservedBytes;
                public IntPtr reservedPointer;
                public IntPtr input;
                public IntPtr output;
                public IntPtr error;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct PROCESS_INFORMATION
            {
                public IntPtr process;
                public IntPtr thread;
                public uint processId;
                public uint threadId;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool TerminateProcess(IntPtr process, uint exitCode);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CreateProcessW(
                string application,
                StringBuilder commandLine,
                IntPtr processAttributes,
                IntPtr threadAttributes,
                bool inheritHandles,
                uint creationFlags,
                IntPtr environment,
                string currentDirectory,
                ref STARTUPINFO startup,
                out PROCESS_INFORMATION process);

            public static bool CanOpenSupervisor(uint processId)
            {
                IntPtr handle = OpenProcess(0x00000b6b, false, processId);
                if (handle == IntPtr.Zero) return false;
                CloseHandle(handle);
                return true;
            }

            public static bool CanCreateBreakaway()
            {
                string command = @"C:\Windows\System32\cmd.exe";
                var startup = new STARTUPINFO();
                startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                PROCESS_INFORMATION process;
                bool created = CreateProcessW(
                    command,
                    new StringBuilder("cmd.exe /d /c exit 0"),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    0x09000004,
                    IntPtr.Zero,
                    @"C:\Windows\System32",
                    ref startup,
                    out process);
                if (!created) return false;
                TerminateProcess(process.process, 0x49524456);
                CloseHandle(process.thread);
                CloseHandle(process.process);
                return true;
            }
        }
        '@
        Add-Type -TypeDefinition $nativeProbe -Language CSharp
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $administrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $scratchProbe = 'C:\IronDev\Scratch\restricted-write.probe'
        $evidenceProbe = 'C:\IronDev\Evidence\restricted-direct-write.probe'
        $supervisorProbe = 'C:\IronDev\Supervisor\restricted-direct-write.probe'
        $scratchWriteSucceeded = $false
        $evidenceWriteSucceeded = $false
        $supervisorWriteSucceeded = $false
        $loaderWriteOpenSucceeded = $false
        $sourceWriteOpenSucceeded = $false
        try {
            [IO.File]::WriteAllText($scratchProbe, 'probe', [Text.UTF8Encoding]::new($false))
            $scratchWriteSucceeded = $true
        } catch { $scratchWriteSucceeded = $false }
        try {
            [IO.File]::WriteAllText($evidenceProbe, 'probe', [Text.UTF8Encoding]::new($false))
            $evidenceWriteSucceeded = $true
        } catch { $evidenceWriteSucceeded = $false }
        try {
            [IO.File]::WriteAllText($supervisorProbe, 'probe', [Text.UTF8Encoding]::new($false))
            $supervisorWriteSucceeded = $true
        } catch { $supervisorWriteSucceeded = $false }
        try {
            $stream = [IO.File]::Open(
                'C:\IronDev\Supervisor\loader.ps1',
                [IO.FileMode]::Open,
                [IO.FileAccess]::Write,
                [IO.FileShare]::ReadWrite)
            $stream.Dispose()
            $loaderWriteOpenSucceeded = $true
        } catch { $loaderWriteOpenSucceeded = $false }
        try {
            $stream = [IO.File]::Open(
                'C:\IronDev\Supervisor\supervisor.cs',
                [IO.FileMode]::Open,
                [IO.FileAccess]::Write,
                [IO.FileShare]::ReadWrite)
            $stream.Dispose()
            $sourceWriteOpenSucceeded = $true
        } catch { $sourceWriteOpenSucceeded = $false }
        Remove-Item -LiteralPath $scratchProbe -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $evidenceProbe -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $supervisorProbe -Force -ErrorAction SilentlyContinue
        $supervisorHandleSucceeded = [IronDevBoundaryProbe]::CanOpenSupervisor(
            [uint32]$env:IRONDEV_SUPERVISOR_PID)
        $breakawaySucceeded = [IronDevBoundaryProbe]::CanCreateBreakaway()

        $command = 'C:\Windows\System32\cmd.exe'
        $escapedCommand = '"' + $command + '" /d /c exit 0'
        $wmiSucceeded = $false
        try {
            $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $escapedCommand }
            $wmiSucceeded = ([int]$result.ReturnValue -eq 0)
        } catch { $wmiSucceeded = $false }

        $serviceName = 'IronDevBrokerProbe'
        $scmSucceeded = $false
        try {
            & 'C:\Windows\System32\sc.exe' create $serviceName binPath= $escapedCommand start= demand 2>&1 | Out-Null
            $scmSucceeded = ($LASTEXITCODE -eq 0)
        } catch { $scmSucceeded = $false }
        if ($scmSucceeded) {
            & 'C:\Windows\System32\sc.exe' delete $serviceName 2>&1 | Out-Null
        }

        $taskName = '\IronDevBrokerProbe'
        $taskSucceeded = $false
        try {
            & 'C:\Windows\System32\schtasks.exe' /Create /F /SC ONCE /ST 23:59 /TN $taskName /TR $escapedCommand 2>&1 | Out-Null
            $taskSucceeded = ($LASTEXITCODE -eq 0)
        } catch { $taskSucceeded = $false }
        if ($taskSucceeded) {
            & 'C:\Windows\System32\schtasks.exe' /Delete /F /TN $taskName 2>&1 | Out-Null
        }

        $comSucceeded = $false
        try {
            $shell = New-Object -ComObject 'Shell.Application'
            $shell.ShellExecute($command, '/d /c exit 0', '', 'open', 0)
            $comSucceeded = $true
        } catch { $comSucceeded = $false }

        if ($administrator -or -not $scratchWriteSucceeded -or $evidenceWriteSucceeded -or
            $supervisorWriteSucceeded -or $loaderWriteOpenSucceeded -or $sourceWriteOpenSucceeded -or
            $supervisorHandleSucceeded -or $breakawaySucceeded -or
            $wmiSucceeded -or $scmSucceeded -or $taskSucceeded -or $comSucceeded) {
            [Console]::Error.WriteLine('The restricted low-integrity workload boundary proof failed.')
            exit 41
        }
        [Console]::Out.WriteLine('IRONDEV_BROKER_DENIAL_PROVEN')
        exit 0
        """;

    public const string BootstrapScript = """
        $ErrorActionPreference = 'Stop'
        function Get-IronDevSha256([byte[]]$bytes) {
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = $hasher.ComputeHash($bytes) }
            finally { $hasher.Dispose() }
            return (-join @($hash | ForEach-Object { $_.ToString('x2') }))
        }
        $loaderPath = 'C:\IronDev\Supervisor\loader.ps1'
        $sourcePath = 'C:\IronDev\Supervisor\supervisor.cs'
        $directory = Get-Item -LiteralPath 'C:\IronDev\Supervisor' -Force
        $loader = Get-Item -LiteralPath $loaderPath -Force
        $source = Get-Item -LiteralPath $sourcePath -Force
        if (-not $directory.PSIsContainer -or
            (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or
            [IO.Path]::GetFullPath($directory.FullName) -cne 'C:\IronDev\Supervisor' -or
            $loader.Directory.FullName -cne $directory.FullName -or
            $source.Directory.FullName -cne $directory.FullName -or
            $loader.PSIsContainer -or $source.PSIsContainer -or
            (($loader.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or
            (($source.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or
            $loader.Length -lt 1 -or $loader.Length -gt 65536 -or
            $source.Length -lt 1 -or $source.Length -gt 131072) {
            throw 'The staged supervisor artifact paths are unsafe.'
        }
        $loaderHash = Get-IronDevSha256 ([IO.File]::ReadAllBytes($loaderPath))
        $sourceHash = Get-IronDevSha256 ([IO.File]::ReadAllBytes($sourcePath))
        if ($loaderHash -cne $env:IRONDEV_SUPERVISOR_LOADER_SHA256 -or
            $sourceHash -cne $env:IRONDEV_SUPERVISOR_SOURCE_SHA256 -or
            $env:IRONDEV_BOOTSTRAP_SHA256 -notmatch '^[a-f0-9]{64}$') {
            throw 'A staged supervisor component hash does not match policy.'
        }
        $material = $env:IRONDEV_BOOTSTRAP_SHA256 + ':' + $loaderHash + ':' + $sourceHash
        $contractHash = Get-IronDevSha256 ([Text.Encoding]::UTF8.GetBytes($material))
        if ($contractHash -cne $env:IRONDEV_SUPERVISOR_SHA256) {
            throw 'The staged supervisor composite hash does not match policy.'
        }
        & $loaderPath
        """;

    public static string LoaderScript { get; } = BuildLoaderScript();

    private static string BuildLoaderScript()
    {
        var brokerProbeBase64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(BrokerProbeScript));
        return $$"""
            $ErrorActionPreference = 'Stop'
            $loaderPath = '{{LoaderContainerPath}}'
            $sourcePath = '{{SourceContainerPath}}'
            if ([IO.Path]::GetFullPath($PSCommandPath) -cne $loaderPath) {
                throw 'The supervisor loader was not invoked from its fixed protected path.'
            }
            $loaderBytes = [IO.File]::ReadAllBytes($loaderPath)
            $sourceBytes = [IO.File]::ReadAllBytes($sourcePath)
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $loaderHashBytes = $hasher.ComputeHash($loaderBytes) }
            finally { $hasher.Dispose() }
            $loaderHash = -join @($loaderHashBytes | ForEach-Object { $_.ToString('x2') })
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $sourceHashBytes = $hasher.ComputeHash($sourceBytes) }
            finally { $hasher.Dispose() }
            $sourceHash = -join @($sourceHashBytes | ForEach-Object { $_.ToString('x2') })
            if ($loaderHash -cne $env:IRONDEV_SUPERVISOR_LOADER_SHA256 -or
                $sourceHash -cne $env:IRONDEV_SUPERVISOR_SOURCE_SHA256) {
                throw 'A workload supervisor file hash does not match its trusted contract.'
            }
            $material = $env:IRONDEV_BOOTSTRAP_SHA256 + ':' + $loaderHash + ':' + $sourceHash
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $compositeBytes = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($material)) }
            finally { $hasher.Dispose() }
            $compositeHash = -join @($compositeBytes | ForEach-Object { $_.ToString('x2') })
            if ($compositeHash -cne $env:IRONDEV_SUPERVISOR_SHA256) {
                throw 'The trusted workload supervisor composite hash does not match policy.'
            }
            $source = [Text.Encoding]::UTF8.GetString($sourceBytes)
            Add-Type -TypeDefinition $source -Language CSharp
            if ([IronDevSandbox.WorkloadSupervisor]::ContractVersion -cne $env:IRONDEV_SUPERVISOR_VERSION) {
                throw 'The loaded workload supervisor version does not match its trusted contract.'
            }

            $mode = [string]$env:IRONDEV_SUPERVISOR_MODE
            $limit = [int]$env:IRONDEV_WORKLOAD_PROCESS_LIMIT
            if ($limit -ne 64) { throw 'The v0.1 workload process maximum must be exactly 64.' }

            if ($mode -ceq 'preflight') {
                $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
                $principal = [Security.Principal.WindowsPrincipal]::new($identity)
                if ($identity.User.Value -cne '{{ContainerAdministratorSid}}' -or
                    -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
                    throw 'The trusted supervisor must run as the fixed container administrator identity.'
                }

                New-Item -ItemType Directory -Force -Path 'C:\IronDev\Scratch\Source' | Out-Null
                New-Item -ItemType Directory -Force -Path 'C:\IronDev\Evidence' | Out-Null
                & 'C:\Windows\System32\icacls.exe' '{{ContainerDirectory}}' /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-93-2-1:(OI)(CI)RX' /T /C /Q | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'The protected supervisor artifact ACL could not be applied.' }
                & 'C:\Windows\System32\icacls.exe' '{{ContainerDirectory}}' /setintegritylevel '(OI)(CI)M' /T /C /Q | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'The protected supervisor artifact integrity label could not be applied.' }
                & 'C:\Windows\System32\icacls.exe' 'C:\IronDev\Scratch' /grant '*S-1-1-0:(OI)(CI)M' /T /C /Q | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'The restricted workload scratch ACL could not be applied.' }
                & 'C:\Windows\System32\icacls.exe' 'C:\IronDev\Scratch' /setintegritylevel '(OI)(CI)L' /T /C /Q | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'The restricted workload scratch integrity label could not be applied.' }

                $probe = [IronDevSandbox.WorkloadSupervisor]::ProbeProcessLimit($limit)
                if ($probe.ActiveProcessLimit -ne 64 -or $probe.LimitFlags -ne 8200 -or
                    -not $probe.FirstSixtyFourAssignmentsSucceeded -or
                    -not $probe.SixtyFifthAssignmentRejected -or
                    -not $probe.SuspendedAssignmentBeforeResume -or
                    -not $probe.AllProbeProcessesTerminated -or
                    -not $probe.RestrictedLowIntegrityToken -or
                    -not $probe.SupervisorHandleIsolation) {
                    throw 'The restricted workload Job Object proof failed.'
                }

                $brokerOut = 'C:\IronDev\Evidence\broker-probe.stdout.log'
                $brokerErr = 'C:\IronDev\Evidence\broker-probe.stderr.log'
                $env:TEMP = 'C:\IronDev\Scratch'
                $env:TMP = 'C:\IronDev\Scratch'
                $env:IRONDEV_SUPERVISOR_PID = [string]$PID
                $brokerArgs = @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', '{{brokerProbeBase64}}')
                $broker = [IronDevSandbox.WorkloadSupervisor]::Run(
                    'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe',
                    $brokerArgs,
                    'C:\IronDev\Scratch',
                    $brokerOut,
                    $brokerErr,
                    $limit,
                    60000)
                $brokerText = if (Test-Path -LiteralPath $brokerOut) { [IO.File]::ReadAllText($brokerOut) } else { '' }
                if ($broker.TimedOut -or $broker.ExitCode -ne 0 -or
                    -not $brokerText.Contains('IRONDEV_BROKER_DENIAL_PROVEN')) {
                    throw 'The restricted workload process-launch broker denial proof failed.'
                }

                Get-ChildItem -LiteralPath 'C:\IronDev\Input\Source' -Force | ForEach-Object {
                    Copy-Item -LiteralPath $_.FullName -Destination 'C:\IronDev\Scratch\Source' -Recurse -Force
                }
                & 'C:\Windows\System32\icacls.exe' 'C:\IronDev\Scratch\Source' /setintegritylevel '(OI)(CI)L' /T /C /Q | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'The copied workload source integrity label could not be applied.' }

                $proof = [ordered]@{
                    trustedSupervisorVersion = [string]$env:IRONDEV_SUPERVISOR_VERSION
                    trustedSupervisorSha256 = [string]$env:IRONDEV_SUPERVISOR_SHA256
                    maximumUntrustedWorkloadProcessCount = $limit
                    untrustedWorkloadProcessScope = '{{WorkloadProcessScope}}'
                    suspendedAssignmentBeforeResumeProven = $true
                    untrustedWorkloadProcessLimitProven = $true
                    restrictedLowIntegrityWorkloadIdentityProven = $true
                    supervisorHandleIsolationProven = $true
                    workloadScratchAndEvidenceBoundaryProven = $true
                    brokerLaunchDenialProven = $true
                    projectBytesCopiedAfterPreflightProven = $true
                }
                $json = $proof | ConvertTo-Json -Compress
                [Console]::Out.WriteLine('{{ProofMarker}}' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))
                exit 0
            }

            if ($mode -ceq 'stage') {
                $payloadBase64 = [string]$env:IRONDEV_STAGE_PAYLOAD
                $payloadJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payloadBase64))
                $payload = $payloadJson | ConvertFrom-Json
                $stdoutPath = Join-Path 'C:\IronDev\Evidence' ($payload.stage + '.stdout.log')
                $stderrPath = Join-Path 'C:\IronDev\Evidence' ($payload.stage + '.stderr.log')
                Get-ChildItem Env: | Remove-Item -ErrorAction SilentlyContinue
                foreach ($property in $payload.environment.PSObject.Properties) {
                    Set-Item -LiteralPath ('Env:' + $property.Name) -Value ([string]$property.Value)
                }
                $expectedNames = @($payload.environment.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
                $actualNames = @(Get-ChildItem Env: | ForEach-Object { $_.Name } | Sort-Object)
                if (@(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames).Count -ne 0) {
                    throw 'The command environment could not be reduced to the explicit allow-list.'
                }
                $arguments = @($payload.arguments | ForEach-Object { [string]$_ })
                $result = [IronDevSandbox.WorkloadSupervisor]::Run(
                    [string]$payload.commandPath,
                    $arguments,
                    'C:\IronDev\Scratch\Source',
                    $stdoutPath,
                    $stderrPath,
                    $limit,
                    [int]$payload.timeoutMilliseconds)
                if (Test-Path -LiteralPath $stdoutPath) { [Console]::Out.Write([IO.File]::ReadAllText($stdoutPath)) }
                if (Test-Path -LiteralPath $stderrPath) { [Console]::Error.Write([IO.File]::ReadAllText($stderrPath)) }
                if ($result.TimedOut) { [Console]::Error.WriteLine('IRONDEV_WORKLOAD_TIMEOUT') }
                exit ([int]$result.ExitCode)
            }

            throw 'The trusted workload supervisor mode is invalid.'
            """;
    }
}
