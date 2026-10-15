using System.Security.Claims;
using System.Net;
using System.Reflection;
using ApexCharts;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shortener.Web.Components.Layout;
using Shortener.Web.Components.Pages;
using Shortener.Web.Services;
using Shortener.Web.Services.LiveUpdates;
using Shortener.Application;
using Xunit;

namespace Shortener.WebTests;

public sealed class ComponentTests : BunitContext
{
    [Fact]
    public void Heatmap_conversion_merges_repeated_local_hour_at_fall_back()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var counts = InsightsTime.ByLocalWeekdayHour([
            new(new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero), 2),
            new(new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), 3)
        ], zone);
        Assert.Equal(5, counts[(6, 1)]);
    }

    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly FakeLiveUpdates liveUpdates = new();
    private static readonly CreatedLink Created = new("abc", "https://example.com", "/r/abc",
        new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
    private static readonly ManagedLink Managed = new("abc", "https://example.com", Created.CreatedAtUtc, null, 4);

    [Fact]
    public void Anonymous_navigation_shows_sign_in_without_sign_out()
    {
        AddAuthorization();
        Setup();

        var navigation = Render<NavMenu>();
        Assert.Single(navigation.FindAll("a[href='/contact']"));

        Assert.Single(navigation.FindAll("a[href='/account/signin'][data-enhance-nav='false']"));
        Assert.Empty(navigation.FindAll("form[action='/account/signout']"));
    }

    [Fact]
    public void Authenticated_navigation_shows_sign_out_without_sign_in()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        Setup();

        var navigation = Render<NavMenu>();

        Assert.Single(navigation.FindAll("form[action='/account/signout']"));
        Assert.Empty(navigation.FindAll("a[href='/account/signin']"));
    }

    [Fact]
    public void Signed_out_page_is_anonymous_and_offers_next_steps()
    {
        AddAuthorization();
        Setup();

        var page = Render<SignedOut>();

        Assert.True(Attribute.IsDefined(typeof(SignedOut), typeof(AllowAnonymousAttribute)));
        Assert.Contains("You're signed out.", page.Find("h1").TextContent);
        Assert.Single(page.FindAll("a[href='/account/signin'][data-enhance-nav='false']"));
        Assert.Single(page.FindAll("a[href='/']"));
    }

    [Fact]
    public void Logout_hint_is_sent_only_when_claim_is_present()
    {
        var withHint = new OpenIdConnectMessage();
        SignOutHints.Apply(new ClaimsPrincipal(new ClaimsIdentity([new Claim("login_hint", "opaque-test-hint")], "test")), withHint);
        Assert.Equal("opaque-test-hint", withHint.GetParameter("logout_hint"));

        var withoutHint = new OpenIdConnectMessage();
        SignOutHints.Apply(new ClaimsPrincipal(new ClaimsIdentity("test")), withoutHint);
        Assert.Null(withoutHint.GetParameter("logout_hint"));
    }

    [Fact]
    public void Brand_navigation_and_home_keep_accessible_guest_form()
    {
        AddAuthorization();
        Setup();
        var navigation = Render<NavMenu>();
        Assert.Equal("Ziplio home", navigation.Find("a.brand").GetAttribute("aria-label"));
        Assert.Contains("ziplio", navigation.Find("a.brand span").TextContent);

        var home = Render<Home>();
        Assert.Equal("Every link deserves a smarter destination.", home.Find("h1").TextContent);
        Assert.Equal("Try a guest link", home.Find("#guest-title").TextContent);
        Assert.Equal("true", home.Find(".zl-demo").GetAttribute("aria-hidden"));
        Assert.NotNull(home.Find("#guest-url"));
        Assert.NotNull(home.Find("#guest-code"));
    }

    [Fact]
    public async Task Guest_form_shows_loading_then_expiry_without_management_controls()
    {
        var pending = new TaskCompletionSource<LinkResult>();
        var api = Setup();
        api.Guest = _ => pending.Task;
        var view = Render<Home>();
        Assert.Contains("seven days", view.Markup);
        view.Find("#guest-url").Change("https://example.com");
        var submit = view.Find("form").SubmitAsync();
        Assert.Contains("Creating your link", view.Markup);
        pending.SetResult(new(Created, new Uri("https://api.example.test/r/abc")));
        await submit;
        view.WaitForAssertion(() => Assert.Contains("Guest link ready", view.Markup));
        Assert.Contains("Expires:", view.Markup);
        Assert.Equal("https://api.example.test/r/abc", view.Find("button.copy-link").GetAttribute("data-copy-text"));
        Assert.Equal("Copy short link abc", view.Find("button.copy-link").GetAttribute("aria-label"));
        Assert.DoesNotContain("Details", view.Markup);
        Assert.DoesNotContain("Redirects:", view.Markup);
    }

    [Fact]
    public async Task Guest_form_shows_code_hint_then_clears_and_focuses_after_success()
    {
        var api = Setup();
        CreateLinkRequest? submitted = null;
        api.Guest = request => { submitted = request; return Task.FromResult(new LinkResult(Created, new Uri("https://api.example.test/r/abc"))); };
        var view = Render<Home>();
        Assert.Equal("[A-Za-z0-9]{4,12}", view.Find("#guest-code").GetAttribute("pattern"));
        Assert.Equal("12", view.Find("#guest-code").GetAttribute("maxlength"));
        Assert.Contains("4–12 letters or numbers (optional)", view.Find("#guest-code-hint").TextContent);
        view.Find("#guest-url").Change("https://example.com/page");
        view.Find("#guest-code").Change("Alpha4");
        await view.Find("form").SubmitAsync();
        Assert.Equal("Alpha4", submitted?.ShortCode);
        Assert.Equal("", view.Find("#guest-url").GetAttribute("value") ?? "");
        Assert.Equal("", view.Find("#guest-code").GetAttribute("value") ?? "");
        Assert.NotNull(view.Find("#guest-url").GetAttribute("autofocus"));
    }

    [Theory]
    [InlineData("example.com", "Alpha4", "guest-url-error", CreateLinkFormValidation.UrlMessage)]
    [InlineData("https://example.com", "bad!", "guest-code-error", CreateLinkFormValidation.CodeMessage)]
    public async Task Guest_form_retains_input_and_links_validation_to_field(
        string url, string code, string errorId, string message)
    {
        var api = Setup();
        var calls = 0;
        api.Guest = _ => { calls++; throw new InvalidOperationException(); };
        var view = Render<Home>();
        view.Find("#guest-url").Change(url);
        view.Find("#guest-code").Change(code);
        await view.Find("form").SubmitAsync();
        Assert.Equal(0, calls);
        Assert.Equal(url, view.Find("#guest-url").GetAttribute("value"));
        Assert.Equal(code, view.Find("#guest-code").GetAttribute("value"));
        Assert.Equal(message, view.Find("#" + errorId).TextContent);
        Assert.Contains(errorId, view.Find(errorId.Contains("url") ? "#guest-url" : "#guest-code").GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task Guest_duplicate_api_error_is_next_to_code_and_keeps_input()
    {
        var api = Setup();
        api.Guest = _ => throw new ShortenerApiException("Generic conflict", HttpStatusCode.Conflict);
        var view = Render<Home>();
        view.Find("#guest-url").Change("https://example.com");
        view.Find("#guest-code").Change("Alpha4");
        await view.Find("form").SubmitAsync();
        Assert.Equal(CreateLinkFormValidation.DuplicateMessage, view.Find("#guest-code-error").TextContent);
        Assert.Equal("Alpha4", view.Find("#guest-code").GetAttribute("value"));
    }

    [Theory]
    [InlineData(CreateLinkField.DestinationUrl, "guest-url-error", CreateLinkFormValidation.UrlMessage)]
    [InlineData(CreateLinkField.ShortCode, "guest-code-error", CreateLinkFormValidation.CodeMessage)]
    public async Task Guest_api_400_identifies_the_reported_field(CreateLinkField field, string errorId, string message)
    {
        var api = Setup();
        api.Guest = _ => throw new ShortenerApiException("Generic fallback", HttpStatusCode.BadRequest, field);
        var view = Render<Home>();
        view.Find("#guest-url").Change("https://example.com");
        view.Find("#guest-code").Change("Alpha4");
        await view.Find("form").SubmitAsync();
        Assert.Equal(message, view.Find("#" + errorId).TextContent);
        Assert.DoesNotContain("Generic fallback", view.Markup);
    }

    [Fact]
    public async Task Guest_form_shows_safe_api_failure()
    {
        var api = Setup();
        api.Guest = _ => throw new ShortenerApiException("That custom code is already in use. Choose another.");
        var view = Render<Home>();
        view.Find("#guest-url").Change("https://example.com");
        await view.Find("form").SubmitAsync();
        view.WaitForAssertion(() => Assert.Contains("already in use", view.Markup));
        Assert.DoesNotContain("Guest link ready", view.Markup);
    }

    [Fact]
    public async Task Dashboard_loads_creates_and_displays_analytics_for_signed_in_user()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        api.ManagedCreate = _ => Task.FromResult(new LinkResult(Created, new Uri("https://api.example.test/r/abc")));
        api.Analytics = _ => Task.FromResult(Managed);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Contains("Your links", view.Markup));
        Assert.DoesNotContain("Updates automatically", view.Markup);
        Assert.Single(view.FindAll(".link-row-actions"));
        Assert.Equal("https://api.example.test/r/abc", view.Find("td button.copy-link").GetAttribute("data-copy-text"));
        Assert.Equal("My links", view.Find("h1").TextContent);
        Assert.Contains("abc", view.Markup);
        view.Find("#managed-url").Change("https://example.com");
        await view.Find("form").SubmitAsync();
        view.WaitForAssertion(() => Assert.Contains("Link created", view.Markup));
        Assert.Equal("https://api.example.test/r/abc", view.Find(".toast button.copy-link").GetAttribute("data-copy-text"));
        Assert.Equal("https://api.example.test/r/abc", view.Find(".result button.copy-link").GetAttribute("data-copy-text"));
        Assert.DoesNotContain(">Refresh</button>", view.Markup);
        Assert.Contains("selected-link", view.Markup);
        await view.Find("button[aria-label='Details for abc']").ClickAsync();
        view.WaitForAssertion(() => Assert.Contains("Redirects: 4", view.Markup));
        Assert.Contains("Details for", view.Markup);
        Assert.True(api.AuthenticatedCalls >= 3);
    }

    [Fact]
    public async Task Managed_form_clears_and_focuses_after_success()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        api.ManagedCreate = _ => Task.FromResult(new LinkResult(Created, new Uri("https://api.example.test/r/abc")));
        var view = Render<Dashboard>();
        Assert.Contains("4–12 letters or numbers (optional)", view.Find("#managed-code-hint").TextContent);
        view.Find("#managed-url").Change("https://example.com");
        view.Find("#managed-code").Change("Alpha4");
        await view.Find("form").SubmitAsync();
        view.WaitForAssertion(() => Assert.Equal("", view.Find("#managed-url").GetAttribute("value") ?? ""));
        Assert.Equal("", view.Find("#managed-code").GetAttribute("value") ?? "");
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("example.com", "Alpha4", "managed-url-error", CreateLinkFormValidation.UrlMessage)]
    [InlineData("https://example.com", "bad!", "managed-code-error", CreateLinkFormValidation.CodeMessage)]
    public async Task Managed_form_retains_input_and_links_validation_to_field(
        string url, string code, string errorId, string message)
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        api.ManagedCreate = _ => throw new InvalidOperationException("Should not call API");
        var view = Render<Dashboard>();
        view.Find("#managed-url").Change(url);
        view.Find("#managed-code").Change(code);
        await view.Find("form").SubmitAsync();
        Assert.Equal(url, view.Find("#managed-url").GetAttribute("value"));
        Assert.Equal(code, view.Find("#managed-code").GetAttribute("value"));
        Assert.Equal(message, view.Find("#" + errorId).TextContent);
        Assert.Contains(errorId, view.Find(errorId.Contains("url") ? "#managed-url" : "#managed-code").GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task Managed_duplicate_api_error_is_next_to_code_and_keeps_input()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        api.ManagedCreate = _ => throw new ShortenerApiException("Generic conflict", HttpStatusCode.Conflict);
        var view = Render<Dashboard>();
        view.Find("#managed-url").Change("https://example.com");
        view.Find("#managed-code").Change("Alpha4");
        await view.Find("form").SubmitAsync();
        Assert.Equal(CreateLinkFormValidation.DuplicateMessage, view.Find("#managed-code-error").TextContent);
        Assert.Equal("Alpha4", view.Find("#managed-code").GetAttribute("value"));
    }

    [Theory]
    [InlineData(CreateLinkField.DestinationUrl, "managed-url-error", CreateLinkFormValidation.UrlMessage)]
    [InlineData(CreateLinkField.ShortCode, "managed-code-error", CreateLinkFormValidation.CodeMessage)]
    public async Task Managed_api_400_identifies_the_reported_field(CreateLinkField field, string errorId, string message)
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        api.ManagedCreate = _ => throw new ShortenerApiException("Generic fallback", HttpStatusCode.BadRequest, field);
        var view = Render<Dashboard>();
        view.Find("#managed-url").Change("https://example.com");
        view.Find("#managed-code").Change("Alpha4");
        await view.Find("form").SubmitAsync();
        Assert.Equal(message, view.Find("#" + errorId).TextContent);
        Assert.DoesNotContain("Generic fallback", view.Markup);
    }

    [Fact]
    public void Dashboard_uses_browser_time_zone_for_created_and_last_accessed()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        var timestamp = new DateTimeOffset(2026, 10, 14, 19, 31, 0, TimeSpan.Zero);
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed with
        {
            CreatedAtUtc = timestamp, LastAccessedAtUtc = timestamp
        }]);
        api.Analytics = _ => Task.FromResult(Managed with { LastAccessedAtUtc = timestamp });
        JSInterop.SetupModule("./insights.js").Setup<ViewerPreferences>("viewerPreferences")
            .SetResult(new("America/Chicago", false));
        var view = Render<Dashboard>();
        var local = TimeZoneInfo.ConvertTime(timestamp, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
        view.WaitForAssertion(() => Assert.Contains(local.ToString("g"), view.Find("tbody").TextContent));
        Assert.Contains("Times shown in America/Chicago", view.Markup);
        Assert.DoesNotContain(timestamp.ToString("g"), view.Find("tbody").TextContent);
        view.Find("button[aria-label='Details for abc']").Click();
        view.WaitForAssertion(() => Assert.Contains(local.ToString("f"), view.Find("section.result").TextContent));
    }

    [Fact]
    public async Task Dashboard_duplicate_notice_does_not_claim_creation()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        api.ManagedCreate = _ => Task.FromResult(new LinkResult(Created,
            new Uri("https://api.example.test/r/abc"), AlreadyExists: true));
        var view = Render<Dashboard>();
        view.Find("#managed-url").Change("https://example.com");
        await view.Find("form").SubmitAsync();
        view.WaitForAssertion(() => Assert.Contains("Link already exists", view.Markup));
        Assert.DoesNotContain("Link created", view.Markup);
        Assert.Contains("Details for", view.Markup);
        view.WaitForAssertion(() => Assert.DoesNotContain("Link already exists", view.Markup), TimeSpan.FromSeconds(9));
        Assert.Contains("Details for", view.Markup);
    }

    [Fact]
    public void Dashboard_updates_redirect_counts_without_a_refresh_button()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        auth.SetClaims(new Claim("tid", Tenant.ToString()));
        var api = Setup();
        var snapshot = Managed;
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([snapshot]);
        api.Analytics = _ => Task.FromResult(snapshot);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Contains("Redirects", view.Markup));
        Assert.DoesNotContain(">Refresh</button>", view.Markup);
        view.Find("button[aria-label='Details for abc']").Click();
        snapshot = Managed with { HitCount = 5 };
        liveUpdates.Signal(LinkChangeScope.Tenant, Tenant);
        view.WaitForAssertion(() => { Assert.Contains("<td>5</td>", view.Markup); Assert.Contains("Redirects: 5", view.Markup); }, TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task Dashboard_is_idle_and_coalesces_a_signal_burst()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        auth.SetClaims(new Claim("tid", Tenant.ToString()));
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Equal(1, api.AuthenticatedCalls));
        await Task.Delay(100);
        Assert.Equal(1, api.AuthenticatedCalls);
        for (var i = 0; i < 50; i++) liveUpdates.Signal(LinkChangeScope.Tenant, Tenant);
        await Task.Delay(250);
        Assert.Equal(2, api.AuthenticatedCalls);
        await view.Instance.DisposeAsync();
        view.Dispose();
        liveUpdates.Signal(LinkChangeScope.Tenant, Tenant);
        await Task.Delay(50);
        Assert.Equal(2, api.AuthenticatedCalls);
    }

    [Fact]
    public async Task Dashboard_refreshes_during_sustained_signals()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        auth.SetClaims(new Claim("tid", Tenant.ToString()));
        var api = Setup();
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["LiveUpdates:DebounceMilliseconds"] = "1000" }).Build());
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Equal(1, api.AuthenticatedCalls));

        for (var i = 0; i < 25; i++)
        {
            liveUpdates.Signal(LinkChangeScope.Tenant, Tenant);
            await Task.Delay(200);
        }
        await Task.Delay(300);
        Assert.InRange(api.AuthenticatedCalls - 1, 4, 6);
        await view.Instance.DisposeAsync();
        view.Dispose();
    }

    [Fact]
    public async Task Workspace_switch_and_disposal_cancel_pending_refreshes()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var personal = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        var team = new WorkspaceSummary(Guid.NewGuid(), "Team", 1);
        api.Workspaces = _ => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([personal, team]);
        api.WorkspaceList = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Equal(1, liveUpdates.SubscriptionCount));
        Assert.Single(api.WorkspaceReads);

        liveUpdates.Signal(LinkChangeScope.Workspace, personal.Id);
        await Services.GetRequiredService<WorkspaceNavigationState>().SelectAsync(team.Id);
        Assert.Equal(2, api.WorkspaceReads.Count);
        await Task.Delay(200);
        Assert.Equal(2, api.WorkspaceReads.Count);

        liveUpdates.Signal(LinkChangeScope.Workspace, team.Id);
        await view.Instance.DisposeAsync();
        view.Dispose();
        await Task.Delay(200);
        Assert.Equal(2, api.WorkspaceReads.Count);
    }

    [Fact]
    public void Dashboard_shows_loading_then_error()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var pending = new TaskCompletionSource<IReadOnlyList<ManagedLink>>();
        var api = Setup();
        api.List = _ => pending.Task;
        var view = Render<Dashboard>();
        Assert.Contains("Loading managed links", view.Markup);
        pending.SetException(new ShortenerApiException("The shortener API is unavailable. Try again later."));
        view.WaitForAssertion(() => Assert.Contains("API is unavailable", view.Markup));
    }

    [Fact]
    public void Dashboard_renews_the_session_when_the_token_cache_is_empty()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => throw new ManagedReauthenticationRequiredException();
        Render<Dashboard>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/account/renew", navigation.Uri);
    }

    [Fact]
    public void Dashboard_stops_automatic_renewal_after_one_failed_attempt()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => throw new ManagedReauthenticationRequiredException();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/dashboard?reauth=1");
        var view = Render<Dashboard>();
        Assert.Contains("could not restore", view.Markup);
        Assert.Contains("Sign in again", view.Markup);
        Assert.EndsWith("/dashboard?reauth=1", navigation.Uri);
    }

    [Fact]
    public void Dashboard_clears_the_renewal_marker_after_access_is_restored()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup();
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed]);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/dashboard?reauth=1");
        Render<Dashboard>();
        Assert.EndsWith("/dashboard", navigation.Uri);
    }

    [Fact]
    public void One_workspace_shows_name_under_title_without_a_select()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var personal = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        api.Workspaces = _ => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([personal]);
        api.WorkspaceList = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([]);

        var navigation = Render<NavMenu>();
        var dashboard = Render<Dashboard>();

        dashboard.WaitForAssertion(() => Assert.Equal("Personal workspace", dashboard.Find(".workspace-caption").TextContent));
        Assert.Empty(navigation.FindAll("#workspace-select"));
        Assert.Empty(dashboard.FindAll("#workspace-select"));
        Assert.Equal(personal.Id, Assert.Single(api.WorkspaceReads));
    }

    [Fact]
    public async Task Multiple_workspaces_show_nav_switcher_and_load_selected_workspace()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var personal = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        var team = new WorkspaceSummary(Guid.NewGuid(), "Team", 1);
        api.Workspaces = _ => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([personal, team]);
        api.WorkspaceList = id => Task.FromResult<IReadOnlyList<ManagedLink>>(
            id == team.Id ? [Managed with { ShortCode = "team" }] : [Managed]);

        var navigation = Render<NavMenu>();
        var dashboard = Render<Dashboard>();

        navigation.WaitForAssertion(() => Assert.Single(navigation.FindAll("#workspace-select")));
        Assert.Contains("workspace-switcher", navigation.Find("a.brand").NextElementSibling?.ClassName);
        Assert.Empty(dashboard.FindAll("#workspace-select"));
        Assert.Empty(dashboard.FindAll(".workspace-caption"));
        Assert.Equal(personal.Id, Assert.Single(api.WorkspaceReads));

        await navigation.Find("#workspace-select").ChangeAsync(team.Id.ToString());
        dashboard.WaitForAssertion(() => Assert.Contains("team", dashboard.Find("tbody").TextContent));
        Assert.Equal(team.Id, api.WorkspaceReads.Last());
    }

    [Fact]
    public void Dashboard_batches_sparklines_for_all_rows()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var personal = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        api.Workspaces = _ => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([personal]);
        api.WorkspaceList = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([Managed, Managed with { ShortCode = "second" }]);
        api.Sparklines = _ => Task.FromResult<IReadOnlyList<LinkSparkline>>([
            new("abc", [new(DateTimeOffset.UtcNow, 1), new(DateTimeOffset.UtcNow.AddDays(1), 2)]),
            new("second", [new(DateTimeOffset.UtcNow, 3), new(DateTimeOffset.UtcNow.AddDays(1), 1)])]);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("svg.insights-sparkline").Count));
        Assert.Single(api.SparklineReads);
        Assert.Equal(personal.Id, api.SparklineReads[0]);
        Assert.Equal(2, view.FindAll("a[href$='/insights']").Count);
    }

    [Fact]
    public void Insights_empty_state_uses_selected_workspace_and_shows_tiles()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var personal = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        Func<Guid, Task> dashboardSelection = _ => Task.CompletedTask;
        var navigation = Services.GetRequiredService<WorkspaceNavigationState>();
        navigation.Configure([personal], personal.Id, dashboardSelection);
        navigation.Clear(dashboardSelection);
        Assert.Equal(personal.Id, navigation.SelectedId);
        api.InsightsData = (id, range) =>
        {
            Assert.Equal(personal.Id, id);
            Assert.Equal("7d", range);
            return Task.FromResult(new LinkInsights(0, 0, 0, [], [], [], null, []));
        };
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./insights.js");
        var view = Render<Insights>(parameters => parameters.Add(x => x.ShortCode, "abc"));
        Assert.Contains("No clicks yet", view.Markup);
        Assert.Contains("No clicks in the last 28 days", view.Markup);
        var emptyOptions = (ApexChartBaseOptions)typeof(Insights).GetField("heatOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view.Instance)!;
        Assert.Single(emptyOptions.PlotOptions!.Heatmap!.ColorScale!.Ranges!);
        Assert.Equal("#1E2C44", emptyOptions.PlotOptions.Heatmap.ColorScale.Ranges[0].Color);
        Assert.Equal(4, view.FindAll(".insights-tile h2").Count);
        Assert.Contains("Insights excludes detected bots", view.Markup);
    }

    [Fact]
    public void Insights_refreshes_for_selected_workspace_signal()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var workspace = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        Services.GetRequiredService<WorkspaceNavigationState>().Configure([workspace], workspace.Id, _ => Task.CompletedTask);
        var reads = 0;
        api.InsightsData = (id, _) =>
        {
            Assert.Equal(workspace.Id, id);
            reads++;
            return Task.FromResult(new LinkInsights(0, 0, 0, [], [], [], null, []));
        };
        JSInterop.Mode = JSRuntimeMode.Loose;
        var view = Render<Insights>(parameters => parameters.Add(x => x.ShortCode, "abc"));
        view.WaitForAssertion(() => Assert.Equal(1, reads));
        view.WaitForAssertion(() => Assert.Equal(1, liveUpdates.SubscriptionCount));
        liveUpdates.Signal(LinkChangeScope.Workspace, Guid.NewGuid());
        Assert.Equal(1, reads);
        liveUpdates.Signal(LinkChangeScope.Workspace, workspace.Id);
        view.WaitForAssertion(() => Assert.Equal(2, reads), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Insights_renders_charts_and_accessible_summaries_with_clicks()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("operator");
        var api = Setup(customer: true);
        var workspace = new WorkspaceSummary(Guid.NewGuid(), "Personal", 0);
        Services.GetRequiredService<WorkspaceNavigationState>().Configure([workspace], workspace.Id, _ => Task.CompletedTask);
        var bucket = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
        api.InsightsData = (_, _) => Task.FromResult(new LinkInsights(3, 1, 2,
            [new InsightPoint(bucket, 3)], [new InsightGroup("Direct", 3)],
            [new InsightGroup("Desktop", 3)], new InsightLastClick(bucket, "Direct", "Desktop"),
            [new InsightPoint(bucket, 3)]));
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./insights.js");
        var view = Render<Insights>(parameters => parameters.Add(x => x.ShortCode, "abc"));
        Assert.True(view.Markup.Contains("Clicks over time", StringComparison.Ordinal), view.Markup);
        Assert.Equal(4, view.FindAll(".insights-tile h2").Count);
        Assert.Contains("visually-hidden", view.Markup);
        Assert.Contains("Top referrers", view.Markup);
        Assert.Contains("When your audience clicks", view.Markup);
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var options = (ApexChartBaseOptions)typeof(Insights).GetField("heatOptions", fields)!.GetValue(view.Instance)!;
        var heatmap = options.PlotOptions!.Heatmap!;
        Assert.False(heatmap.Distributed);
        Assert.False(options.DataLabels!.Enabled);
        Assert.Null(options.Tooltip?.Custom);
        Assert.Equal("#1E2C44", heatmap.ColorScale!.Ranges![0].Color);
        Assert.Equal("#59E6CF", heatmap.ColorScale.Ranges[^1].Color);
        Assert.Equal(3, heatmap.ColorScale.Ranges[^1].To);
        var seriesOrder = (string[])typeof(Insights).GetField("HeatSeriesOrder", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.Equal(new[] { "Sun", "Sat", "Fri", "Thu", "Wed", "Tue", "Mon" }, seriesOrder);
        var cellType = typeof(Insights).GetNestedType("HeatCell", BindingFlags.NonPublic)!;
        var tooltip = typeof(Insights).GetMethod("HeatTooltip", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal("Tue 3p · 4 clicks", tooltip.Invoke(null, [Activator.CreateInstance(cellType, "Tue", "3p", 4L)]));
        Assert.Equal("Tue 3p · 1 click", tooltip.Invoke(null, [Activator.CreateInstance(cellType, "Tue", "3p", 1L)]));
        Assert.Contains("clicks across the last 28 days", view.Markup);
    }

    private FakeApi Setup(bool customer = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var api = new FakeApi();
        Services.AddSingleton<IShortenerApiClient>(api);
        Services.AddSingleton(new IdentityAvailability(true, customer));
        Services.AddSingleton(new WorkspaceNavigationState());
        Services.AddSingleton(NullLogger<Home>.Instance);
        Services.AddSingleton(NullLogger<Dashboard>.Instance);
        Services.AddSingleton(NullLogger<Insights>.Instance);
        Services.AddSingleton<ILinkChangeSubscriber>(liveUpdates);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["LiveUpdates:DebounceMilliseconds"] = "100" }).Build());
        return api;
    }

    private sealed class FakeLiveUpdates : ILinkChangeSubscriber
    {
        private readonly List<(LinkChangeScope Scope, Guid Id, Func<Task> Callback)> callbacks = [];
        public int SubscriptionCount => callbacks.Count;
        public bool IsConnected => true;
        public event Action<bool>? ConnectionChanged
        {
            add { }
            remove { }
        }
        public IDisposable Subscribe(LinkChangeScope scope, Guid scopeId, Func<Task> onChange)
        {
            var entry = (scope, scopeId, onChange);
            callbacks.Add(entry);
            return new FakeSubscription(() => callbacks.Remove(entry));
        }
        public void Signal(LinkChangeScope scope, Guid id)
        {
            foreach (var entry in callbacks.Where(x => x.Scope == scope && x.Id == id).ToArray())
                _ = entry.Callback();
        }
        private sealed class FakeSubscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    private sealed class FakeApi : IShortenerApiClient
    {
        public Func<CreateLinkRequest, Task<LinkResult>> Guest { get; set; } = _ => throw new NotImplementedException();
        public Func<CreateLinkRequest, Task<LinkResult>> ManagedCreate { get; set; } = _ => throw new NotImplementedException();
        public Func<ClaimsPrincipal, Task<IReadOnlyList<ManagedLink>>> List { get; set; } = _ => throw new NotImplementedException();
        public Func<string, Task<ManagedLink>> Analytics { get; set; } = _ => throw new NotImplementedException();
        public Func<ClaimsPrincipal, Task<IReadOnlyList<WorkspaceSummary>>> Workspaces { get; set; } = _ => throw new NotImplementedException();
        public Func<Guid, Task<IReadOnlyList<ManagedLink>>> WorkspaceList { get; set; } = _ => throw new NotImplementedException();
        public Func<Guid, string, Task<LinkInsights>> InsightsData { get; set; } = (_, _) => throw new NotImplementedException();
        public Func<Guid, Task<IReadOnlyList<LinkSparkline>>> Sparklines { get; set; } = _ => Task.FromResult<IReadOnlyList<LinkSparkline>>([]);
        public List<Guid> WorkspaceReads { get; } = [];
        public List<Guid> SparklineReads { get; } = [];
        public int AuthenticatedCalls { get; private set; }
        public Task<LinkResult> CreateGuestAsync(CreateLinkRequest request, CancellationToken cancellationToken = default) => Guest(request);
        public Task<LinkResult> CreateManagedAsync(CreateLinkRequest request, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); AuthenticatedCalls++; return ManagedCreate(request); }
        public Task<IReadOnlyList<ManagedLink>> GetManagedLinksAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); AuthenticatedCalls++; return List(user); }
        public Task<ManagedLink> GetManagedLinkAsync(string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); AuthenticatedCalls++; return Task.FromResult(Managed); }
        public Task<ManagedLink> GetManagedAnalyticsAsync(string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); AuthenticatedCalls++; return Analytics(shortCode); }
        public Task<IReadOnlyList<WorkspaceSummary>> GetWorkspacesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); return Workspaces(user); }
        public Task<IReadOnlyList<ManagedLink>> GetWorkspaceManagedLinksAsync(Guid workspaceId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); WorkspaceReads.Add(workspaceId); return WorkspaceList(workspaceId); }
        public Task<ManagedLink> GetWorkspaceManagedLinkAsync(Guid workspaceId, string shortCode, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); return Task.FromResult(Managed); }
        public Task<LinkInsights> GetWorkspaceInsightsAsync(Guid workspaceId, string shortCode, string range, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); return InsightsData(workspaceId, range); }
        public Task<IReadOnlyList<LinkSparkline>> GetWorkspaceSparklinesAsync(Guid workspaceId, int days, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        { Assert.True(user.Identity?.IsAuthenticated); SparklineReads.Add(workspaceId); return Sparklines(workspaceId); }
        public Uri PublicUrl(string shortCode) => new("https://api.example.test/r/" + shortCode);
    }
}
