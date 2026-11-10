namespace BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>Tüm olayların yayınlandığı topic exchange.</summary>
    public string Exchange { get; set; } = "eticaret.events";

    /// <summary>Servisin kuyruk adı (ör. "order"). Retry/DLQ kuyrukları bundan türetilir.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>Bir mesajın DLQ'ya düşmeden önce kaç kez yeniden deneneceği.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Retry kuyruğunda bekleme süresi (TTL).</summary>
    public int RetryDelayMs { get; set; } = 3000;

    public ushort PrefetchCount { get; set; } = 10;

    public int OutboxPollingIntervalMs { get; set; } = 500;
}

