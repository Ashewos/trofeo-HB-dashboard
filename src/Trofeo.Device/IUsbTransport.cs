namespace Trofeo.Device;

/// <summary>Un transfert USB a dépassé son délai.</summary>
public sealed class UsbTimeoutException : Exception
{
    public UsbTimeoutException() : base("Délai USB dépassé.") { }
    public UsbTimeoutException(string message) : base(message) { }
}

/// <summary>L'écran est présent mais ouvert par un autre programme (par exemple TRCC).</summary>
public sealed class UsbBusyException : Exception
{
    public UsbBusyException() : base("Périphérique USB utilisé par un autre programme.") { }
    public UsbBusyException(string message) : base(message) { }
}

/// <summary>Accès bulk à l'écran : OUT 0x09, IN 0x81. Les autres échecs de transfert lèvent <see cref="IOException"/>.</summary>
public interface IUsbTransport : IDisposable
{
    /// <exception cref="UsbTimeoutException">Le délai est dépassé.</exception>
    void Write(ReadOnlySpan<byte> data, int timeoutMs);

    /// <returns>Le nombre d'octets lus.</returns>
    /// <exception cref="UsbTimeoutException">Le délai est dépassé.</exception>
    int Read(Span<byte> buffer, int timeoutMs);

    /// <summary>Abandonne les transferts en cours et lève l'arrêt (clear halt) des deux pipes.</summary>
    void ResetPipes();
}

/// <summary>Ouvre l'écran. Renvoie <c>null</c> s'il est absent ; lève <see cref="UsbBusyException"/> s'il est occupé.</summary>
public interface IUsbTransportFactory
{
    IUsbTransport? TryOpen();
}
