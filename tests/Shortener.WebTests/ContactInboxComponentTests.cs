using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shortener.Application;
using Shortener.Web.Components.Layout;
using Shortener.Web.Components.Pages;
using Shortener.Web.Services;
using Shortener.Web.Services.LiveUpdates;
using Xunit;

namespace Shortener.WebTests;

public sealed class ContactInboxComponentTests : BunitContext
{
    [Fact]
    public void Header_link_is_hidden_for_non_admin_and_shows_live_count_for_admin()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("michael");
        var api = Setup();
        var nonAdmin = Render<ContactAdminNav>();
        Assert.Empty(nonAdmin.FindAll("a[href='/admin/messages']"));

        api.Info = new ContactAdminInfo(true, 2);
        var live = Render<ContactAdminNav>();
        live.WaitForAssertion(() => Assert.Contains("Messages 2", live.Find("a[href='/admin/messages']").TextContent));
        api.Info = new ContactAdminInfo(true, 3);
        Services.GetRequiredService<FakeLiveUpdates>().Signal();
        live.WaitForAssertion(() => Assert.Contains("Messages 3", live.Find("a[href='/admin/messages']").TextContent),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Admin_page_encodes_message_text_and_non_admin_sees_no_content()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("michael");
        var api = Setup();
        var item = new ContactMessageItem(Guid.NewGuid(), DateTimeOffset.UtcNow, "Visitor",
            "visitor@example.test", "Question", "<script>alert('x')</script>", null, null, null);
        api.Page = new ContactMessagePage([item], null, 1);

        var blocked = Render<ContactMessages>();
        Assert.Contains("do not have access", blocked.Markup);
        Assert.DoesNotContain("Visitor", blocked.Markup);

        api.Info = new ContactAdminInfo(true, 1);
        var allowed = Render<ContactMessages>();
        allowed.WaitForAssertion(() => Assert.Contains("Visitor", allowed.Markup));
        Assert.Contains("&lt;script&gt;", allowed.Markup);
        Assert.Empty(allowed.FindAll("article script"));
        Assert.Contains("mailto:visitor%40example.test", allowed.Find("article a").GetAttribute("href"));
        Assert.Contains("Pending", allowed.Markup);
    }

    [Fact]
    public void Open_admin_page_refreshes_after_an_admin_hint_without_polling()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("michael");
        var api = Setup();
        api.Info = new ContactAdminInfo(true, 0);
        api.Page = new ContactMessagePage([], null, 0);
        var view = Render<ContactMessages>();
        view.WaitForAssertion(() => Assert.Contains("No contact messages yet", view.Markup));

        api.Page = new ContactMessagePage([
            new ContactMessageItem(Guid.NewGuid(), DateTimeOffset.UtcNow, "New visitor",
                "new@example.test", "Hello", "A new message", DateTimeOffset.UtcNow, null, null)
        ], null, 1);
        Services.GetRequiredService<FakeLiveUpdates>().Signal();
        view.WaitForAssertion(() => Assert.Contains("A new message", view.Markup), TimeSpan.FromSeconds(3));
        Assert.Contains("Sent", view.Markup);
    }

    private FakeInboxApi Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var api = new FakeInboxApi();
        var live = new FakeLiveUpdates();
        Services.AddSingleton<IContactInboxApiClient>(api);
        Services.AddSingleton(live);
        Services.AddSingleton<ILinkChangeSubscriber>(live);
        Services.AddSingleton(NullLogger<ContactMessages>.Instance);
        Services.AddSingleton(NullLogger<ContactAdminNav>.Instance);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["LiveUpdates:DebounceMilliseconds"] = "20" }).Build());
        return api;
    }

    private sealed class FakeInboxApi : IContactInboxApiClient
    {
        public ContactAdminInfo Info { get; set; } = new(false, 0);
        public ContactMessagePage Page { get; set; } = new([], null, 0);
        public Task<ContactAdminInfo> GetInfoAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => Task.FromResult(Info);
        public Task<ContactMessagePage> GetPageAsync(ClaimsPrincipal user, string? cursor = null,
            CancellationToken cancellationToken = default) => Task.FromResult(Page);
        public Task MarkHandledAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeLiveUpdates : ILinkChangeSubscriber
    {
        private readonly List<Func<Task>> callbacks = [];
        public bool IsConnected => true;
        public event Action<bool>? ConnectionChanged
        {
            add { }
            remove { }
        }
        public IDisposable Subscribe(LinkChangeScope scope, Guid scopeId, Func<Task> onChange)
        {
            Assert.Equal(LinkChangeScope.Admin, scope);
            Assert.Equal(ContactInboxHint.ScopeId, scopeId);
            callbacks.Add(onChange);
            return new Subscription(() => callbacks.Remove(onChange));
        }
        public void Signal()
        {
            foreach (var callback in callbacks.ToArray()) _ = callback();
        }
        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
