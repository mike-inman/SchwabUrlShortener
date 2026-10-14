using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shortener.Application;

public enum LinkChangeScope { Tenant, Workspace }

public sealed record LinkChange(LinkChangeScope Scope, Guid ScopeId, string ShortCode)
{
    private sealed record Payload(int V, string Scope, Guid ScopeId, string ShortCode);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Serialize() => JsonSerializer.Serialize(new Payload(1,
        Scope == LinkChangeScope.Tenant ? "tenant" : "workspace", ScopeId, ShortCode), JsonOptions);

    public static bool TryDeserialize(string json, out LinkChange? change)
    {
        change = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("v").GetInt32() != 1 ||
                !Guid.TryParse(root.GetProperty("scopeId").GetString(), out var id) || id == Guid.Empty)
                return false;
            var scope = root.GetProperty("scope").GetString() switch
            {
                "tenant" => LinkChangeScope.Tenant,
                "workspace" => LinkChangeScope.Workspace,
                _ => (LinkChangeScope?)null
            };
            var code = root.GetProperty("shortCode").GetString();
            if (scope is null || string.IsNullOrWhiteSpace(code) ||
                !Domain.UrlValidation.IsValidShortCode(code)) return false;
            change = new(scope.Value, id, code);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }
}

public interface ILinkChangePublisher
{
    Task PublishAsync(LinkChange change, CancellationToken cancellationToken = default);
}
