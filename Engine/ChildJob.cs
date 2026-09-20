using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SocLucia.Engine;

/// <summary>
/// Un «job» de Windows con KILL_ON_JOB_CLOSE: los procesos que se le asignan mueren cuando muere
/// esta aplicacion, aunque sea a lo bruto (Administrador de tareas, cuelgue). Asi no queda ningun
/// llama-server huerfano con gigas de memoria ocupados.
/// </summary>
public static class ChildJob
{
    private static readonly Lazy<IntPtr> Job = new(Create);

    public static void Add(Process process)
    {
        try
        {
            if (Job.Value != IntPtr.Zero)
                AssignProcessToJobObject(Job.Value, process.Handle);
        }
        catch (Exception) { }
    }

    private static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
            return IntPtr.Zero;
        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = 0x2000 /* JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE */ },
        };
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    /// <summary>Al arrancar: si quedo algun llama-server nuestro de una sesion anterior que murio mal, fuera.</summary>
    public static void KillStale(string engineFolder)
    {
        foreach (var process in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                var path = process.MainModule?.FileName ?? string.Empty;
                if (path.StartsWith(engineFolder, StringComparison.OrdinalIgnoreCase))
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception) { }
            finally { process.Dispose(); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
