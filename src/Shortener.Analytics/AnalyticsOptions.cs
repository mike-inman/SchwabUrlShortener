using Confluent.Kafka;

namespace Shortener.Analytics;

public sealed record AnalyticsOptions(string BootstrapServers, string Topic, string ConsumerGroup)
{
    public SecurityProtocol SecurityProtocol { get; init; } = SecurityProtocol.Plaintext;
    public SaslMechanism? SaslMechanism { get; init; }
    public string? SaslUsername { get; init; }
    public string? SaslPassword { get; init; }
    public bool EnableIdempotence { get; init; } = true;

    public static AnalyticsOptions FromConfiguration(IConfiguration configuration)
    {
        var bootstrap = configuration["Kafka:BootstrapServers"];
        var topic = configuration["Kafka:Topic"];
        var group = configuration["Kafka:ConsumerGroup"];
        if (string.IsNullOrWhiteSpace(bootstrap) || string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(group))
            throw new InvalidOperationException("Kafka:BootstrapServers, Kafka:Topic, and Kafka:ConsumerGroup must be configured.");
        var protocolText = configuration["Kafka:SecurityProtocol"] ?? "Plaintext";
        if (!Enum.TryParse<SecurityProtocol>(protocolText, true, out var protocol) ||
            protocol is not (SecurityProtocol.Plaintext or SecurityProtocol.SaslSsl))
            throw new InvalidOperationException("Kafka:SecurityProtocol must be Plaintext or SaslSsl.");
        var mechanismText = configuration["Kafka:SaslMechanism"];
        SaslMechanism? mechanism = null;
        if (!string.IsNullOrWhiteSpace(mechanismText))
        {
            if (!Enum.TryParse<SaslMechanism>(mechanismText, true, out var parsed))
                throw new InvalidOperationException("Kafka:SaslMechanism is invalid.");
            mechanism = parsed;
        }
        var username = configuration["Kafka:SaslUsername"];
        var password = configuration["Kafka:SaslPassword"];
        if (protocol == SecurityProtocol.SaslSsl && mechanism is null)
            throw new InvalidOperationException("Kafka:SaslMechanism must be configured for SaslSsl.");
        if (protocol == SecurityProtocol.SaslSsl && string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Kafka:SaslUsername must be configured for SaslSsl.");
        if (protocol == SecurityProtocol.SaslSsl && string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Kafka:SaslPassword must be configured for SaslSsl.");
        var idempotenceText = configuration["Kafka:EnableIdempotence"] ?? "true";
        if (!bool.TryParse(idempotenceText, out var idempotence))
            throw new InvalidOperationException("Kafka:EnableIdempotence must be true or false.");
        return new(bootstrap, topic, group)
        {
            SecurityProtocol = protocol, SaslMechanism = mechanism, SaslUsername = username,
            SaslPassword = password, EnableIdempotence = idempotence
        };
    }

    public ProducerConfig ProducerConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = EnableIdempotence,
        MessageSendMaxRetries = SecurityProtocol == SecurityProtocol.SaslSsl ? int.MaxValue : 3,
        RetryBackoffMs = 250,
        MessageTimeoutMs = SecurityProtocol == SecurityProtocol.SaslSsl ? 120000 : 15000,
        RequestTimeoutMs = SecurityProtocol == SecurityProtocol.SaslSsl ? 60000 : 10000,
        SocketTimeoutMs = SecurityProtocol == SecurityProtocol.SaslSsl ? 60000 : 10000,
        SecurityProtocol = SecurityProtocol,
        SaslMechanism = SaslMechanism,
        SaslUsername = SaslUsername,
        SaslPassword = SaslPassword
    };

    public ConsumerConfig ConsumerConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        GroupId = ConsumerGroup,
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        SessionTimeoutMs = 10000,
        SecurityProtocol = SecurityProtocol,
        SaslMechanism = SaslMechanism,
        SaslUsername = SaslUsername,
        SaslPassword = SaslPassword
    };

    public AdminClientConfig AdminConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        SecurityProtocol = SecurityProtocol,
        SaslMechanism = SaslMechanism,
        SaslUsername = SaslUsername,
        SaslPassword = SaslPassword
    };
}
