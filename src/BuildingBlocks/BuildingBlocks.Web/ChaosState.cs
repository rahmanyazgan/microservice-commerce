namespace BuildingBlocks.Web;

public sealed record ChaosSettings(int DelayMs, int FailurePercent);

/// <summary>
/// Canlı ortamda Polly (retry / circuit breaker / timeout) ve Saga telafi (compensation) davranışlarını
/// arayüzden tek tıkla test edebilmek için bellekte tutulan hata enjeksiyonu durumu.
/// </summary>
public sealed class ChaosState
{
    private volatile int _delayMs;
    private volatile int _failurePercent;

    public ChaosSettings Get() => new(_delayMs, _failurePercent);

    public ChaosSettings Update(ChaosSettings settings)
    {
        _delayMs = Math.Clamp(settings.DelayMs, 0, 15_000);
        _failurePercent = Math.Clamp(settings.FailurePercent, 0, 100);
        return Get();
    }

    public async Task ApplyAsync(string targetName, CancellationToken ct = default)
    {
        var delay = _delayMs;
        var fail = _failurePercent;

        if (delay > 0)
            await Task.Delay(delay, ct);

        if (fail > 0 && Random.Shared.Next(100) < fail)
            throw new SimulatedFaultException($"{targetName} hata simülasyonu aktif (%{fail} hata oranı).");
    }
}

