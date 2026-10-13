using Npgsql;

namespace Shortener.IntegrationTests;

internal static class GuardedTestDatabase
{
    internal static bool IsAllowed(NpgsqlConnectionStringBuilder target)
    {
        if (target.Database != "Schwab_test_local") return false;
        var existingLocalTest = target.Port == 63651 &&
            target.Host is "127.0.0.1" or "localhost" or "::1";
        var isolatedComposeTest = target.Port == 5432 && target.Host == "db" &&
            Environment.GetEnvironmentVariable("SCHWAB_TEST_COMPOSE_ISOLATED") == "yes";
        return existingLocalTest || isolatedComposeTest;
    }
}
