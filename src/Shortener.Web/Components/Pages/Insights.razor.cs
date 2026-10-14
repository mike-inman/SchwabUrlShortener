using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Shortener.Application;
using Shortener.Web.Services;
using Shortener.Web.Services.LiveUpdates;

namespace Shortener.Web.Components.Pages;

public partial class Insights
{

    [Parameter] public string ShortCode { get; set; } = string.Empty;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;
    private readonly CancellationTokenSource lifetime = new();
    private IDisposable? subscription;
    private CancellationTokenSource? pending;
    private Task? pendingTask;
    private IJSObjectReference? module;
    private Guid? workspaceId;
    private ManagedLink? link;
    private LinkInsights? data;
    private string range = "7d";
    private string? error;
    private bool loading = true;
    private bool liveUpdateError;
    private bool localTimeAvailable;
    private bool reducedMotion;
    private bool browserReady;
    private TimeZoneInfo timeZone = TimeZoneInfo.Utc;
    private ApexChart<InsightPoint>? clickChart;
    private ApexChart<InsightGroup>? referrerChart;
    private ApexChart<InsightGroup>? deviceChart;
    private ApexChart<HeatCell>? heatChart;
    private IReadOnlyList<HeatCell> heatCells = [];
    private static readonly string[] Weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private ApexChartOptions<InsightPoint> clickOptions = new();
    private ApexChartOptions<InsightGroup> referrerOptions = new();
    private ApexChartOptions<InsightGroup> deviceOptions = new();
    private ApexChartOptions<HeatCell> heatOptions = new();

    protected override async Task OnInitializedAsync()
    {
        if (!Identity.Customer || Workspaces.SelectedId is not { } selected ||
            Workspaces.Workspaces.All(x => x.Id != selected))
        {
            error = "Choose a workspace from My links to open Insights.";
            loading = false;
            return;
        }
        workspaceId = selected;
        Workspaces.Changed += OnWorkspaceChanged;
        LiveUpdates.ConnectionChanged += OnConnectionChanged;
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || workspaceId is null) return;
        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./insights.js");
            var preferences = await module.InvokeAsync<ViewerPreferences>("viewerPreferences");
            if (preferences is not null)
            {
                reducedMotion = preferences.ReducedMotion;
                try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(preferences.TimeZone); localTimeAvailable = true; }
                catch (TimeZoneNotFoundException) { timeZone = TimeZoneInfo.Utc; }
            }
        }
        catch (JSException ex) { Logger.LogWarning("Insights browser preferences unavailable. ExceptionType={ExceptionType}", ex.GetType().Name); }
        ConfigureCharts();
        if (data is not null) heatCells = BuildHeat(data);
        browserReady = true;
        StateHasChanged();
        subscription = LiveUpdates.Subscribe(LinkChangeScope.Workspace, workspaceId.Value, () => InvokeAsync(QueueRefresh));
        liveUpdateError = !LiveUpdates.IsConnected;
    }

    private async Task LoadAsync()
    {
        if (workspaceId is not { } id) return;
        try
        {
            var user = (await AuthenticationState).User;
            link = await Api.GetWorkspaceManagedLinkAsync(id, ShortCode, user, lifetime.Token);
            data = await Api.GetWorkspaceInsightsAsync(id, ShortCode, range, user, lifetime.Token);
            heatCells = BuildHeat(data);
            error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning("Insights load failed. ExceptionType={ExceptionType}", ex.GetType().Name);
            error = "Insights are unavailable. Try again later.";
        }
        finally { loading = false; }
    }

    private async Task SelectRangeAsync(string selected)
    {
        if (selected == range) return;
        range = selected;
        loading = true;
        await LoadAsync();
        await UpdateChartsAsync();
    }

    private void QueueRefresh()
    {
        if (lifetime.IsCancellationRequested || pending is not null) return;
        pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        pendingTask = ThrottleAsync(pending);
    }

    private async Task ThrottleAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(int.Parse(Configuration["LiveUpdates:DebounceMilliseconds"]!), cancellation.Token);
            await InvokeAsync(async () =>
            {
                if (cancellation.IsCancellationRequested || !ReferenceEquals(pending, cancellation)) return;
                pending = null;
                await LoadAsync();
                await UpdateChartsAsync();
                StateHasChanged();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { cancellation.Dispose(); }
    }

    private async Task UpdateChartsAsync()
    {
        if (data is null || data.TotalClicks == 0) return;
        if (clickChart is not null) await clickChart.UpdateSeriesAsync();
        if (referrerChart is not null) await referrerChart.UpdateSeriesAsync();
        if (deviceChart is not null) await deviceChart.UpdateSeriesAsync();
        if (heatChart is not null) await heatChart.UpdateSeriesAsync();
    }

    private void OnWorkspaceChanged() => _ = InvokeAsync(() =>
    {
        if (Workspaces.SelectedId == workspaceId) return;
        subscription?.Dispose();
        pending?.Cancel();
        workspaceId = Workspaces.SelectedId;
        data = null;
        error = "The selected workspace changed. Return to My links to choose a link.";
        StateHasChanged();
    });

    private void OnConnectionChanged(bool connected) => _ = InvokeAsync(() => { liveUpdateError = !connected; StateHasChanged(); });

    private static string BucketLabel(InsightPoint point) => point.BucketStartUtc.ToString("O");
    private static string Trend(LinkInsights value) => value.PreviousPeriodTotal == 0 ? "New" :
        $"{(value.TotalClicks - value.PreviousPeriodTotal) * 100.0 / value.PreviousPeriodTotal:+0;-0;0}%";
    private string LastClickTime(LinkInsights value) => value.LastClick is null ? "Never" :
        TimeZoneInfo.ConvertTime(value.LastClick.OccurredAtUtc, timeZone).ToString("g");
    private static string LastClickDetail(LinkInsights value) => value.LastClick is null ? "No clicks yet" :
        $"From {value.LastClick.ReferrerGroup ?? "unknown"} on {value.LastClick.DeviceClass ?? "unknown"}";
    private string BusiestHour(LinkInsights value)
    {
        var busiest = BuildHeat(value).OrderByDescending(x => x.Clicks).FirstOrDefault();
        return busiest is null || busiest.Clicks == 0 ? "No clicks yet" : $"{busiest.Weekday}, {busiest.HourLabel}";
    }
    private string HeatSummary() => heatCells.Count == 0 ? "No hourly click data." :
        $"Busiest hour: {BusiestHour(data!)}. {heatCells.Sum(x => x.Clicks)} clicks across the last 28 days.";
    private IReadOnlyList<HeatCell> BuildHeat(LinkInsights value)
    {
        var counts = InsightsTime.ByLocalWeekdayHour(value.HeatmapHours, timeZone);
        return Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour =>
            new HeatCell(Weekdays[day], $"{(hour % 12 == 0 ? 12 : hour % 12)}{(hour < 12 ? "a" : "p")}", counts.GetValueOrDefault((day, hour))))).ToArray();
    }
    private void ConfigureCharts()
    {
        clickOptions = new() { Chart = Chart(320), Colors = ["#59E6CF"], Theme = new() { Mode = Mode.Dark },
            Stroke = new() { Curve = Curve.Smooth, Width = 2.6 }, Grid = new() { BorderColor = "rgba(42,63,99,.7)", StrokeDashArray = 4 },
            Xaxis = new() { Type = XAxisType.Datetime } };
        referrerOptions = new() { Chart = Chart(260), Colors = ["#59E6CF", "#43BFAE", "#35998D", "#27746D", "#1F5B56"],
            PlotOptions = new() { Bar = new() { Horizontal = true, BorderRadius = 6 } }, Theme = new() { Mode = Mode.Dark } };
        deviceOptions = new() { Chart = Chart(260), Colors = ["#59E6CF", "#7C93B8", "#3A5680"],
            PlotOptions = new() { Pie = new() { Donut = new() { Size = "72%" } } }, Theme = new() { Mode = Mode.Dark } };
        heatOptions = new() { Chart = Chart(300), Theme = new() { Mode = Mode.Dark },
            PlotOptions = new() { Heatmap = new() { Radius = 4, EnableShades = false } } };
    }
    private Chart Chart(int height) => new() { Background = "transparent", ForeColor = "#9DB0CB", Height = height,
        Animations = new() { Enabled = !reducedMotion, Easing = Easing.Easeinout, Speed = 900,
            DynamicAnimation = new() { Enabled = !reducedMotion, Speed = 450 } }, Toolbar = new() { Show = false } };

    public async ValueTask DisposeAsync()
    {
        Workspaces.Changed -= OnWorkspaceChanged;
        LiveUpdates.ConnectionChanged -= OnConnectionChanged;
        subscription?.Dispose();
        pending?.Cancel();
        await lifetime.CancelAsync();
        if (pendingTask is not null) { try { await pendingTask; } catch (OperationCanceledException) { } }
        pending?.Dispose();
        if (module is not null) await module.DisposeAsync();
        lifetime.Dispose();
    }
    private sealed record ViewerPreferences(string TimeZone, bool ReducedMotion);
    private sealed record HeatCell(string Weekday, string HourLabel, long Clicks);
}
