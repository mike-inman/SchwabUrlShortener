using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shortener.Web.Components.Layout;
using Shortener.Web.Components.Pages;
using Shortener.Web.Services;
using Xunit;

namespace Shortener.WebTests;

public sealed class ComponentTests : BunitContext
{
    private static readonly CreatedLink Created = new("abc", "https://example.com", "/r/abc",
        new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
    private static readonly ManagedLink Managed = new("abc", "https://example.com", Created.CreatedAtUtc, null, 4);

    [Fact]
    public void Anonymous_navigation_shows_sign_in_without_sign_out()
    {
        AddAuthorization();
        Setup();

        var navigation = Render<NavMenu>();

        Assert.Single(navigation.FindAll("a[href='/account/signin']"));
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
        Assert.Single(page.FindAll("a[href='/account/signin']"));
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
        Assert.DoesNotContain("Details", view.Markup);
        Assert.DoesNotContain("Redirects:", view.Markup);
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
        Assert.Equal("My links", view.Find("h1").TextContent);
        Assert.Contains("abc", view.Markup);
        view.Find("#managed-url").Change("https://example.com");
        await view.Find("form").SubmitAsync();
        view.WaitForAssertion(() => Assert.Contains("Link created", view.Markup));
        Assert.DoesNotContain(">Refresh</button>", view.Markup);
        Assert.Contains("selected-link", view.Markup);
        await view.Find("button.btn-sm").ClickAsync();
        view.WaitForAssertion(() => Assert.Contains("Redirects: 4", view.Markup));
        Assert.Contains("Details for", view.Markup);
        Assert.True(api.AuthenticatedCalls >= 3);
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
        var api = Setup();
        var snapshot = Managed;
        api.List = _ => Task.FromResult<IReadOnlyList<ManagedLink>>([snapshot]);
        api.Analytics = _ => Task.FromResult(snapshot);
        var view = Render<Dashboard>();
        view.WaitForAssertion(() => Assert.Contains("Redirects", view.Markup));
        Assert.DoesNotContain(">Refresh</button>", view.Markup);
        view.Find("button.btn-sm").Click();
        snapshot = Managed with { HitCount = 5 };
        view.WaitForAssertion(() => { Assert.Contains("<td>5</td>", view.Markup); Assert.Contains("Redirects: 5", view.Markup); }, TimeSpan.FromSeconds(8));
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

    private FakeApi Setup(bool customer = false)
    {
        var api = new FakeApi();
        Services.AddSingleton<IShortenerApiClient>(api);
        Services.AddSingleton(new IdentityAvailability(true, customer));
        Services.AddSingleton(new WorkspaceNavigationState());
        Services.AddSingleton(NullLogger<Home>.Instance);
        Services.AddSingleton(NullLogger<Dashboard>.Instance);
        return api;
    }

    private sealed class FakeApi : IShortenerApiClient
    {
        public Func<CreateLinkRequest, Task<LinkResult>> Guest { get; set; } = _ => throw new NotImplementedException();
        public Func<CreateLinkRequest, Task<LinkResult>> ManagedCreate { get; set; } = _ => throw new NotImplementedException();
        public Func<ClaimsPrincipal, Task<IReadOnlyList<ManagedLink>>> List { get; set; } = _ => throw new NotImplementedException();
        public Func<string, Task<ManagedLink>> Analytics { get; set; } = _ => throw new NotImplementedException();
        public Func<ClaimsPrincipal, Task<IReadOnlyList<WorkspaceSummary>>> Workspaces { get; set; } = _ => throw new NotImplementedException();
        public Func<Guid, Task<IReadOnlyList<ManagedLink>>> WorkspaceList { get; set; } = _ => throw new NotImplementedException();
        public List<Guid> WorkspaceReads { get; } = [];
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
        public Uri PublicUrl(string shortCode) => new("https://api.example.test/r/" + shortCode);
    }
}
