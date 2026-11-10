using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Uygulama ömrü boyunca tek bir TCP bağlantısı (IConnection) tutar; kanallar (IChannel) bundan açılır.
/// RabbitMQ henüz ayakta değilse bağlantıyı artan beklemeyle tekrar dener.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsConnected => _connection is { IsOpen: true };

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (IsConnected) return _connection!;

        await _lock.WaitAsync(ct);
        try
        {
            if (IsConnected) return _connection!;

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = o.QueueName
            };

            var attempt = 0;
            while (true)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync(ct);
                    logger.LogInformation("RabbitMQ bağlantısı kuruldu ({Host}:{Port})", o.HostName, o.Port);
                    return _connection;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    attempt++;
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                    logger.LogWarning("RabbitMQ'ya bağlanılamadı (deneme {Attempt}): {Message}. {Delay} sn sonra tekrar denenecek.",
                        attempt, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
    }
}

