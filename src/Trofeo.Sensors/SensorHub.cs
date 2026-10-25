using System.Diagnostics;
using Trofeo.Core.Rendering;

namespace Trofeo.Sensors;

/// <summary>Source partagée de relevés pour les modules : le dernier relevé et un événement à chaque nouveau relevé.</summary>
public interface ISensorFeed
{
    /// <summary>Dernier relevé publié (immuable), ou <c>null</c> avant le premier ou quand plus personne n'utilise la source.</summary>
    SensorSnapshot? Latest { get; }

    /// <summary>Levé après chaque relevé publié, sur le thread qui l'a produit (minuterie ou lecture du tube). Jamais sous un verrou.</summary>
    event EventHandler? Updated;

    /// <summary>Déclare un utilisateur ; la source ne tourne que tant qu'il en reste. Libérer deux fois le même ticket est sans effet.</summary>
    IDisposable Acquire();
}

/// <summary>
/// Choisit la source des relevés : ceux de l'assistant élevé tant qu'il parle (moins de 3 s de silence), sinon
/// une lecture locale par seconde. Le hub ne fonctionne qu'entre le premier <see cref="Acquire"/> et la libération du
/// dernier ticket ; à ce moment la minuterie s'arrête et le lecteur local est détruit (il détient des pilotes et du
/// matériel ouvert, inutiles tant que personne n'affiche de capteurs). Il ne possède pas le flux de l'assistant.
/// </summary>
public sealed class SensorHub(Func<ISensorReader> localFactory, ISnapshotStream helper, TimeProvider time) : ISensorFeed, IDisposable
{
    private static readonly TimeSpan LocalPeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();     // compteur, abonnement, minuterie, dernière réception de l'assistant
    private readonly object _readGate = new(); // une seule lecture locale à la fois, et le lecteur n'est détruit qu'entre deux lectures
    private readonly RateLimitedErrorLog _errors = new(time, m => Trace.TraceWarning(m));
    private int _users;
    private bool _disposed;
    private ITimer? _timer;
    private long? _helperAt;
    private long _startedAt;
    private ISensorReader? _reader; // protégé par _readGate
    private SensorSnapshot? _latest;

    public SensorSnapshot? Latest => Volatile.Read(ref _latest);

    public event EventHandler? Updated;

    public IDisposable Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_users++ == 0) Start();
        }
        return new Lease(this);
    }

    /// <summary>Arrête tout, même si des tickets sont encore tenus ; ils deviennent sans effet.</summary>
    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _users = 0;
            timer = Detach();
        }
        Stop(timer);
    }

    private void Start()
    {
        _helperAt = null; // un ancien silence ou un ancien relevé ne doit pas peser sur la nouvelle session
        _startedAt = time.GetTimestamp();
        helper.Received += OnHelperSnapshot;
        _timer = time.CreateTimer(_ => ReadLocal(), null, LocalPeriod, LocalPeriod);
    }

    private void Release()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_users == 0 || --_users > 0) return;
            timer = Detach();
        }
        Stop(timer);
    }

    // Sous _gate, pour qu'un Acquire concurrent ne voie jamais un hub à moitié arrêté.
    private ITimer? Detach()
    {
        var timer = _timer;
        _timer = null;
        helper.Received -= OnHelperSnapshot;
        Volatile.Write(ref _latest, null);
        return timer;
    }

    private void Stop(ITimer? timer)
    {
        timer?.Dispose();

        // Attend la fin d'une lecture en cours (la première, qui ouvre les pilotes, peut durer quelques secondes).
        lock (_readGate)
        {
            _reader?.Dispose();
            _reader = null;
        }
    }

    private void OnHelperSnapshot(object? sender, SensorSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_users == 0) return;
            _helperAt = time.GetTimestamp();
        }
        Publish(snapshot);
    }

    /// <summary>
    /// L'assistant a parlé il y a moins de 3 s, ou la session a moins de 3 s : au démarrage, on lui laisse le temps
    /// de se connecter avant d'ouvrir LibreHardwareMonitor dans le service.
    /// </summary>
    private bool HelperIsActive()
    {
        lock (_gate)
            return _helperAt is { } at
                ? time.GetElapsedTime(at) < HelperTimeout
                : time.GetElapsedTime(_startedAt) < HelperTimeout;
    }

    private void ReadLocal()
    {
        // Une lecture lente ne doit pas empiler les rappels de la minuterie.
        if (!Monitor.TryEnter(_readGate)) return;
        SensorSnapshot? snapshot = null;
        try
        {
            if (HelperIsActive())
            {
                // L'assistant a pris le relais : le lecteur local rend ses pilotes et sa mémoire.
                _reader?.Dispose();
                _reader = null;
                return;
            }

            lock (_gate)
                if (_users == 0) return;

            _reader ??= localFactory();
            snapshot = _reader.Read();
        }
        catch (Exception ex)
        {
            // Une fois par minute au plus : la panne se répète à chaque seconde.
            _errors.Report($"Lecture locale des capteurs impossible : {ex}");
        }
        finally
        {
            Monitor.Exit(_readGate);
        }

        if (snapshot is not null) Publish(snapshot);
    }

    private void Publish(SensorSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_users == 0) return;
            Volatile.Write(ref _latest, snapshot);
        }

        try
        {
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Un abonné défaillant ne doit ni couper le flux ni faire tomber le processus (rappel de minuterie).
            _errors.Report($"Abonné de la source de capteurs défaillant : {ex}");
        }
    }

    private sealed class Lease(SensorHub hub) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) hub.Release();
        }
    }
}
