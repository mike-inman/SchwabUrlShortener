namespace Shortener.Web.Services;

public sealed class WorkspaceNavigationState
{
    private Func<Guid, Task>? selectWorkspace;

    public IReadOnlyList<WorkspaceSummary> Workspaces { get; private set; } = [];
    public Guid? SelectedId { get; private set; }
    public event Action? Changed;

    public void Configure(IReadOnlyList<WorkspaceSummary> workspaces, Guid selectedId, Func<Guid, Task> onSelect)
    {
        Workspaces = workspaces;
        SelectedId = selectedId;
        selectWorkspace = onSelect;
        Changed?.Invoke();
    }

    public Task SelectAsync(Guid id) =>
        selectWorkspace is not null && Workspaces.Any(workspace => workspace.Id == id)
            ? selectWorkspace(id)
            : Task.CompletedTask;

    public void SetSelected(Guid id)
    {
        if (!Workspaces.Any(workspace => workspace.Id == id)) return;
        SelectedId = id;
        Changed?.Invoke();
    }

    public void Clear(Func<Guid, Task> onSelect)
    {
        if (selectWorkspace != onSelect) return;
        selectWorkspace = null;
        // Keep the selected workspace while navigating from the dashboard to a link's Insights page.
        // A new dashboard load replaces this list with fresh authorized workspaces.
    }
}
