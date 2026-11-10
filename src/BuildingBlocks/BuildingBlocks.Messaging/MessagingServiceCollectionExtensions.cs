using BuildingBlocks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public sealed class MessagingBuilder(IServiceCollection services, MessageHandlerRegistry registry)
{
    public MessagingBuilder AddHandler<TMessage, THandler>()
        where TMessage : IIntegrationMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<IMessageHandler<TMessage>, THandler>();
        registry.Register<TMessage>();
        return this;
    }
}

public static class MessagingServiceCollectionExtensions
{
    public static MessagingBuilder AddRabbitMqMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string queueName)
        where TDbContext : DbContext, IMessagingDbContext
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.PostConfigure<RabbitMqOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.QueueName))
                o.QueueName = queueName;
        });

        var registry = new MessageHandlerRegistry();
        services.AddSingleton(registry);
        services.AddSingleton<RabbitMqConnection>();
        services.AddHostedService<OutboxPublisher<TDbContext>>();
        services.AddHostedService<RabbitMqConsumerWorker<TDbContext>>();

        return new MessagingBuilder(services, registry);
    }
}

