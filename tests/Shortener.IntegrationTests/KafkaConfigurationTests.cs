using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Shortener.Analytics;

namespace Shortener.IntegrationTests;

public class KafkaConfigurationTests
{
    [Fact]
    public void LocalKafkaDefaultsStayPlaintextAndIdempotent()
    {
        var options = AnalyticsOptions.FromConfiguration(Config());
        Assert.Equal(SecurityProtocol.Plaintext, options.ProducerConfig().SecurityProtocol);
        Assert.Equal(SecurityProtocol.Plaintext, options.ConsumerConfig().SecurityProtocol);
        Assert.Equal(SecurityProtocol.Plaintext, options.AdminConfig().SecurityProtocol);
        Assert.True(options.ProducerConfig().EnableIdempotence);
    }

    [Fact]
    public void EventHubsSecurityIsAppliedToEveryClient()
    {
        var options = AnalyticsOptions.FromConfiguration(Config(new()
        {
            ["Kafka:SecurityProtocol"] = "SaslSsl", ["Kafka:SaslMechanism"] = "Plain",
            ["Kafka:SaslUsername"] = "$ConnectionString", ["Kafka:SaslPassword"] = "private-value",
            ["Kafka:EnableIdempotence"] = "true"
        }));
        var producer = options.ProducerConfig();
        var consumer = options.ConsumerConfig();
        var admin = options.AdminConfig();
        Assert.Equal(SecurityProtocol.SaslSsl, producer.SecurityProtocol);
        Assert.Equal(SecurityProtocol.SaslSsl, consumer.SecurityProtocol);
        Assert.Equal(SecurityProtocol.SaslSsl, admin.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, producer.SaslMechanism);
        Assert.Equal(SaslMechanism.Plain, consumer.SaslMechanism);
        Assert.Equal(SaslMechanism.Plain, admin.SaslMechanism);
        Assert.Equal("$ConnectionString", producer.SaslUsername);
        Assert.Equal("$ConnectionString", consumer.SaslUsername);
        Assert.Equal("$ConnectionString", admin.SaslUsername);
        Assert.Equal("private-value", producer.SaslPassword);
        Assert.Equal("private-value", consumer.SaslPassword);
        Assert.Equal("private-value", admin.SaslPassword);
        Assert.True(producer.EnableIdempotence);
        Assert.Equal(60000, producer.RequestTimeoutMs);
    }

    [Fact]
    public void IdempotenceCanBeDisabledExplicitly()
    {
        var options = AnalyticsOptions.FromConfiguration(Config(new() { ["Kafka:EnableIdempotence"] = "false" }));
        Assert.False(options.ProducerConfig().EnableIdempotence);
    }

    [Theory]
    [InlineData("Kafka:SaslUsername")]
    [InlineData("Kafka:SaslPassword")]
    [InlineData("Kafka:SaslMechanism")]
    public void MissingSaslSettingFailsWithoutDisclosingPassword(string missing)
    {
        var values = new Dictionary<string, string?>
        {
            ["Kafka:SecurityProtocol"] = "SaslSsl", ["Kafka:SaslMechanism"] = "Plain",
            ["Kafka:SaslUsername"] = "$ConnectionString", ["Kafka:SaslPassword"] = "private-value"
        };
        values.Remove(missing);
        var ex = Assert.Throws<InvalidOperationException>(() => AnalyticsOptions.FromConfiguration(Config(values)));
        Assert.Contains(missing, ex.Message);
        Assert.DoesNotContain("private-value", ex.Message);
    }

    private static IConfiguration Config(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092", ["Kafka:Topic"] = "managed-clicks-v2",
            ["Kafka:ConsumerGroup"] = "shortener-test"
        };
        if (overrides is not null) foreach (var pair in overrides) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
