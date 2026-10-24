using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Trofeo.Device;

/// <summary>Transport bulk synchrone sur WinUSB : OUT 0x09, IN 0x81, délai réglé par pipe avant chaque appel.</summary>
internal sealed class WinUsbTransport : IUsbTransport
{
    private const byte PipeOut = 0x09;
    private const byte PipeIn = 0x81;

    private readonly SafeFileHandle _file;
    private readonly SafeWinUsbHandle _usb;

    public WinUsbTransport(SafeFileHandle file, SafeWinUsbHandle usb)
    {
        _file = file;
        _usb = usb;
    }

    public void Write(ReadOnlySpan<byte> data, int timeoutMs)
    {
        SetTimeout(PipeOut, timeoutMs);
        var ok = WinUsbNative.WinUsb_WritePipe(
            _usb, PipeOut, ref MemoryMarshal.GetReference(data), (uint)data.Length, out var written, 0);
        ThrowOnFailure(ok);
        if (written != data.Length)
            throw new IOException($"Écriture USB incomplète : {written} octets sur {data.Length}.");
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        SetTimeout(PipeIn, timeoutMs);
        var ok = WinUsbNative.WinUsb_ReadPipe(
            _usb, PipeIn, ref MemoryMarshal.GetReference(buffer), (uint)buffer.Length, out var read, 0);
        ThrowOnFailure(ok);
        return (int)read;
    }

    /// <summary>Au mieux : un abandon sans transfert en cours peut échouer sans conséquence, la poignée de main qui suit le confirmera.</summary>
    public void ResetPipes()
    {
        foreach (var pipe in new[] { PipeOut, PipeIn })
        {
            WinUsbNative.WinUsb_AbortPipe(_usb, pipe);
            WinUsbNative.WinUsb_ResetPipe(_usb, pipe);
        }
    }

    public void Dispose()
    {
        _usb.Dispose(); // WinUsb_Free d'abord, puis fermeture du fichier
        _file.Dispose();
    }

    private void SetTimeout(byte pipe, int timeoutMs)
    {
        var value = (uint)timeoutMs;
        ThrowOnFailure(WinUsbNative.WinUsb_SetPipePolicy(_usb, pipe, WinUsbNative.PipeTransferTimeout, sizeof(uint), ref value));
    }

    private static void ThrowOnFailure(bool ok)
    {
        if (ok)
            return;

        var error = Marshal.GetLastWin32Error();
        if (error == WinUsbNative.ErrorSemTimeout)
            throw new UsbTimeoutException();
        throw new IOException(new Win32Exception(error).Message, new Win32Exception(error));
    }
}
