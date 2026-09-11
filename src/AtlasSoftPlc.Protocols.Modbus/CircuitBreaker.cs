namespace AtlasSoftPlc.Protocols.Modbus;

/// <summary>
/// Mini circuit-breaker (sección 52): ante fallos sucesivos entra en estado de espera con
/// backoff exponencial, evitando reintentos inmediatos y degradación por bloqueo.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly object _gate = new();
    private int _failureCount;
    private DateTimeOffset _openUntilUtc = DateTimeOffset.MinValue;
    private bool _wasTripped;

    /// <summary>Umbral de fallos consecutivos que dispara el circuito.</summary>
    public int FailureThreshold { get; }

    /// <summary>Retardo base (ms) que crece exponencialmente con cada fallo.</summary>
    public int BaseBackoffMs { get; }

    /// <summary>Retardo máximo permitido (ms).</summary>
    public int MaxBackoffMs { get; }

    public CircuitBreaker(int failureThreshold = 3, int baseBackoffMs = 500, int maxBackoffMs = 15000)
    {
        FailureThreshold = Math.Max(1, failureThreshold);
        BaseBackoffMs = Math.Max(1, baseBackoffMs);
        MaxBackoffMs = Math.Max(BaseBackoffMs, maxBackoffMs);
    }

    /// <summary>Registra que el circuito estaba inactivo al iniciar una operación.</summary>
    public void OnAttempt()
    {
        lock (_gate)
        {
            _wasTripped = _failureCount > 0;
        }
    }

    /// <summary>Registra un éxito y cierra el circuito.</summary>
    public void OnSuccess()
    {
        lock (_gate)
        {
            _failureCount = 0;
            _openUntilUtc = DateTimeOffset.MinValue;
        }
    }

    /// <summary>Registra un fallo y, si se supera el umbral, abre el circuito.</summary>
    public void OnFailure()
    {
        lock (_gate)
        {
            _failureCount++;
            if (_failureCount >= FailureThreshold)
            {
                var backoff = BaseBackoffMs * (1 << Math.Min(_failureCount, 8));
                _openUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(Math.Min(backoff, MaxBackoffMs));
            }
        }
    }

    /// <summary>
    /// Indica si el circuito está abierto (debe esperar antes de reintentar).
    /// </summary>
    public bool IsOpen()
    {
        lock (_gate)
        {
            if (_openUntilUtc == DateTimeOffset.MinValue)
                return false;

            if (DateTimeOffset.UtcNow >= _openUntilUtc)
            {
                // Permite un único intento de sondeo antes de reabrir la ventana.
                _openUntilUtc = DateTimeOffset.MinValue;
                return false;
            }
            return true;
        }
    }

    /// <summary>Devuelve el tiempo que resta hasta poder volver a intentar.</summary>
    public TimeSpan RetryAfter()
    {
        lock (_gate)
        {
            if (_openUntilUtc == DateTimeOffset.MinValue)
                return TimeSpan.Zero;

            var remaining = _openUntilUtc - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Número de fallos consecutivos actuales.</summary>
    public int CurrentFailures
    {
        get { lock (_gate) return _failureCount; }
    }

    /// <summary>Indica si el último intento se produjo con el circuito previamente disparado.</summary>
    public bool WasTrippedBeforeAttempt => _wasTripped;
}