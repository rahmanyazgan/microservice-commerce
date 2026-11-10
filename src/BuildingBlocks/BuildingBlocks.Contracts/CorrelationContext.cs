namespace BuildingBlocks.Contracts;

/// <summary>
/// İstek boyunca (HTTP → RabbitMQ → HTTP) taşınan korelasyon kimliği.
/// AsyncLocal sayesinde aynı async akış içinde her yerden erişilebilir.
/// </summary>
public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    private static readonly AsyncLocal<string?> Current = new();

    public static string? CorrelationId
    {
        get => Current.Value;
        set => Current.Value = value;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..16];
}

