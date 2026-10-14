using Shortener.Application;

namespace Shortener.UnitTests;

public sealed class LinkChangeTests
{
    [Theory]
    [InlineData(LinkChangeScope.Tenant, "tenant")]
    [InlineData(LinkChangeScope.Workspace, "workspace")]
    public void Payload_matches_live_update_contract(LinkChangeScope scope, string name)
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var change = new LinkChange(scope, id, "code1");
        Assert.Equal($"{{\"v\":1,\"scope\":\"{name}\",\"scopeId\":\"{id}\",\"shortCode\":\"code1\"}}",
            change.Serialize());
        Assert.True(LinkChange.TryDeserialize(change.Serialize(), out var parsed));
        Assert.Equal(change, parsed);
    }

    [Theory]
    [InlineData("{\"v\":2,\"scope\":\"tenant\",\"scopeId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"shortCode\":\"code1\"}")]
    [InlineData("{\"v\":1,\"scope\":\"user\",\"scopeId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"shortCode\":\"code1\"}")]
    [InlineData("{\"v\":1,\"scope\":\"tenant\",\"scopeId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"shortCode\":\"bad/code\"}")]
    public void Invalid_hints_are_ignored(string payload)
    {
        Assert.False(LinkChange.TryDeserialize(payload, out _));
    }
}
