using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Trofeo.Device;

/// <summary>Trouve l'écran par son interface WinUSB et ouvre un <see cref="WinUsbTransport"/>.</summary>
public sealed class WinUsbTransportFactory : IUsbTransportFactory
{
    private static readonly Guid InterfaceGuid = new("3876E417-E089-4BCC-BBF8-47ABA55E46FB");

    /// <returns><c>null</c> si l'écran est absent ou inutilisable.</returns>
    /// <exception cref="UsbBusyException">Un autre programme a déjà ouvert l'écran.</exception>
    public IUsbTransport? TryOpen()
    {
        var path = FindDevicePath();
        if (path is null)
            return null;

        var file = WinUsbNative.CreateFile(
            path,
            WinUsbNative.GenericRead | WinUsbNative.GenericWrite,
            WinUsbNative.FileShareRead | WinUsbNative.FileShareWrite,
            0,
            WinUsbNative.OpenExisting,
            WinUsbNative.FileFlagOverlapped,
            0);
        if (file.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            return IsBusy(error) ? throw new UsbBusyException() : null;
        }

        if (!WinUsbNative.WinUsb_Initialize(file, out var usb))
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            return IsBusy(error) ? throw new UsbBusyException() : null;
        }

        return new WinUsbTransport(file, usb);
    }

    private static bool IsBusy(int error) =>
        error is WinUsbNative.ErrorAccessDenied or WinUsbNative.ErrorSharingViolation;

    private static string? FindDevicePath()
    {
        var guid = InterfaceGuid;
        using var set = WinUsbNative.SetupDiGetClassDevs(
            ref guid, 0, 0, WinUsbNative.DigcfPresent | WinUsbNative.DigcfDeviceInterface);
        if (set.IsInvalid)
            return null;

        var data = new WinUsbNative.SpDeviceInterfaceData { CbSize = (uint)Marshal.SizeOf<WinUsbNative.SpDeviceInterfaceData>() };
        if (!WinUsbNative.SetupDiEnumDeviceInterfaces(set, 0, ref guid, 0, ref data))
            return null;

        // Premier appel pour connaître la taille, second pour lire le chemin.
        WinUsbNative.SetupDiGetDeviceInterfaceDetail(set, ref data, ref MemoryMarshal.GetReference(Span<byte>.Empty), 0, out var size, 0);
        if (size == 0 || Marshal.GetLastWin32Error() != WinUsbNative.ErrorInsufficientBuffer)
            return null;

        // SP_DEVICE_INTERFACE_DETAIL_DATA_W : DWORD cbSize (8 en 64 bits), puis le chemin en UTF-16 à l'offset 4.
        var detail = new byte[size];
        BitConverter.TryWriteBytes(detail, IntPtr.Size == 8 ? 8u : 6u);
        if (!WinUsbNative.SetupDiGetDeviceInterfaceDetail(set, ref data, ref detail[0], size, out _, 0))
            return null;

        return Encoding.Unicode.GetString(detail, 4, detail.Length - 4).TrimEnd('\0');
    }
}
