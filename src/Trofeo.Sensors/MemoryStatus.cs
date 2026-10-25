using System.Runtime.InteropServices;

namespace Trofeo.Sensors;

/// <summary>Mémoire physique, lue par <c>GlobalMemoryStatusEx</c>.</summary>
internal static partial class MemoryStatus
{
    private const double BytesPerGib = 1024.0 * 1024.0 * 1024.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>Lit la RAM utilisée et la RAM totale, en Go (2^30 octets). Retourne <c>false</c> si l'appel système échoue.</summary>
    public static bool TryRead(out double usedGib, out double totalGib)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            usedGib = totalGib = 0;
            return false;
        }

        totalGib = status.TotalPhys / BytesPerGib;
        usedGib = (status.TotalPhys - status.AvailPhys) / BytesPerGib;
        return true;
    }
}
