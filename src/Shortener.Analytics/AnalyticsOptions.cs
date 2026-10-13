using Confluent.Kafka;

namespace Shortener.Analytics;

public sealed record AnalyticsOptions(string BootstrapServers, string Topic, string ConsumerGroup)
{
    public static AnalyticsOptions FromConfiguration(IConfiguration configuration)
    {
        var bootstrap = configuration["Kafka:BootstrapServers"];
        var topic = configuration["Kafka:Topic"];
        var group = configuration["Kafka:ConsumerGroup"];
        if (string.IsNullOrWhiteSpace(bootstrap) || string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(group))
            throw new InvalidOperationException("Kafka:BootstrapServers, Kafka:Topic, and Kafka:ConsumerGroup must be configured.");
        return new(bootstrap, topic, group);
    }

    public ProducerConfig ProducerConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
        MessageSendMaxRetries = 3,
        RetryBackoffMs = 250,
        MessageTimeoutMs = 15000,
        RequestTimeoutMs = 10000,
        SocketTimeoutMs = 10000
    };

    public ConsumerConfig ConsumerConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        GroupId = ConsumerGroup,
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        SessionTimeoutMs = 10000
    };
}
