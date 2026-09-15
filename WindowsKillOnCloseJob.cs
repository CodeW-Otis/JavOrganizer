using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Binds a child process to a Win32 Job Object configured with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, so the operating system
/// terminates the child — and every process it spawned — the instant this
/// process (the Jellyfin server) dies, no matter how abruptly: graceful
/// shutdown, crash, stack overflow, <c>taskkill /F</c>, End Task or a
/// closed console window. Windows closes the job handle when the owning
/// process is terminated, and the kernel then kills everything that is
/// still inside the job.
/// </summary>
/// <remarks>
/// This closes the gap that <see cref="IHostedService.StopAsync"/> alone
/// cannot: hosted services only run during a *graceful* shutdown. When the
/// server is force-killed the plugin gets no callback at all, and without
/// the job object the child would be orphaned and keep running.
/// </remarks>
internal sealed class WindowsKillOnCloseJob : IDisposable
{
    private IntPtr _handle;

    private WindowsKillOnCloseJob(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Creates a kill-on-close job object and puts the just-started
    /// process into it. Returns <c>null</c> on non-Windows platforms,
    /// where job objects do not exist and the caller falls back to
    /// managing the child purely in software.
    /// </summary>
    /// <param name="process">The process to bind, straight from <see cref="Process.Start(ProcessStartInfo)"/>.</param>
    /// <returns>The bound job, or <c>null</c> when not on Windows.</returns>
    /// <exception cref="Win32Exception">The job could not be created or the process could not be assigned to it.</exception>
    public static WindowsKillOnCloseJob? TryCreate(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception();
        }

        var info = default(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            var error = Marshal.GetLastWin32Error();
            CloseHandle(handle);
            throw new Win32Exception(error);
        }

        if (!AssignProcessToJobObject(handle, process.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            CloseHandle(handle);
            throw new Win32Exception(error);
        }

        return new WindowsKillOnCloseJob(handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            // Merely closing the last handle kills every process still in
            // the job (that is the whole point of the job object).
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, IntPtr lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

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
}
