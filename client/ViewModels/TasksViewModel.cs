using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

public sealed partial class TaskItem : ObservableObject
{
    public long Id { get; private set; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DueLabel), nameof(IsOverdue), nameof(IsDueSoon))] private DateTime _due;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DueLabel), nameof(IsOverdue), nameof(IsDueSoon))] private bool _done;
    [ObservableProperty] private DateTime? _completedAt;
    public ObservableCollection<UserRef> Assignees { get; } = new();
    public UserRef? CreatedBy { get; private set; }

    public bool IsOverdue => !Done && Due.Date < DateTime.Today;
    public bool IsDueSoon => !Done && !IsOverdue && Due.Date <= DateTime.Today.AddDays(2);
    public bool IsMine => Assignees.Any(a => a.IsMe);
    public string DueLabel => Done
        ? "Done" + (CompletedAt is { } c ? " " + Formatting.Ago(c) : "")
        : IsOverdue ? $"Overdue · was due {Due:d MMM}"
        : Due.Date == DateTime.Today ? "Due today"
        : Due.Date == DateTime.Today.AddDays(1) ? "Due tomorrow"
        : $"Due {Due.ToString("ddd, d MMM", CultureInfo.CurrentCulture)}";
    public string AssigneeNames => Assignees.Count == 0 ? "Unassigned" : string.Join(", ", Assignees.Select(a => a.Name));

    public void Update(JsonObject t)
    {
        Id = t.Long("id");
        Text = t.Str("text");
        Due = DateTime.TryParseExact(t.Str("due"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;
        Done = t.Bool("done");
        CompletedAt = t["completedAt"] != null ? t.Time("completedAt") : null;
        CreatedBy = t.Obj("createdBy") is { } c ? Users.From(c) : null;
        Assignees.Clear();
        foreach (var a in t.Objects("assignees")) Assignees.Add(Users.From(a));
        OnPropertyChanged(nameof(AssigneeNames));
        OnPropertyChanged(nameof(IsMine));
        OnPropertyChanged(nameof(DueLabel));
    }
}

public sealed partial class AssigneeChoice : ObservableObject
{
    public required UserRef User { get; init; }
    [ObservableProperty] private bool _isChecked;
}

public sealed partial class TasksViewModel : PageViewModel
{
    private readonly List<TaskItem> _all = new();

    public TasksViewModel(long projectId, string projectName, bool canEdit)
    {
        ProjectId = projectId;
        ProjectName = projectName;
        CanEdit = canEdit;
    }

    public long ProjectId { get; }
    public string ProjectName { get; }
    public bool CanEdit { get; }
    public override string Title => $"Tasks · {ProjectName}";
    public override string NavKey => "projects";

    public sealed record FilterOption(string Key, string Label);

    public IReadOnlyList<FilterOption> Filters { get; } = new[]
    {
        new FilterOption("open", "Open"), new FilterOption("mine", "Assigned to me"), new FilterOption("overdue", "Overdue"),
        new FilterOption("week", "Due this week"), new FilterOption("done", "Done"), new FilterOption("all", "All"),
    };

    public ObservableCollection<TaskItem> Visible { get; } = new();
    public ObservableCollection<AssigneeChoice> Choices { get; } = new();

    [ObservableProperty] private string _filter = "open";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _loaded;

    // editor panel
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private TaskItem? _editing;
    [ObservableProperty] private string _draftText = "";
    [ObservableProperty] private DateTime? _draftDue = DateTime.Today.AddDays(7);
    [ObservableProperty] private string _editorError = "";

    public string EditorTitle => Editing == null ? "New task" : "Edit task";
    public bool IsEmpty => Loaded && Visible.Count == 0;
    public string EmptyText => _all.Count == 0 ? "No tasks yet. Add the first one." : "Nothing matches this filter.";
    public int OpenCount => _all.Count(t => !t.Done);
    public int MineCount => _all.Count(t => !t.Done && t.IsMine);
    public int OverdueCount => _all.Count(t => t.IsOverdue);

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnEditingChanged(TaskItem? value) => OnPropertyChanged(nameof(EditorTitle));

    protected override void Subscribe()
    {
        On("ev.task", e =>
        {
            if (e.Long("projectId") != ProjectId || e.Obj("task") is not { } t) return;
            var item = _all.FirstOrDefault(x => x.Id == t.Long("id"));
            if (item == null)
            {
                item = new TaskItem();
                _all.Add(item);
            }
            item.Update(t);
            ApplyFilter();
        });
        On("ev.task.deleted", e =>
        {
            if (e.Long("projectId") != ProjectId) return;
            _all.RemoveAll(x => x.Id == e.Long("id"));
            if (Editing?.Id == e.Long("id")) IsEditorOpen = false;
            ApplyFilter();
        });
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            // tasks are only sent to people who have the project open
            await Services.Connection.RequestAsync("project.open", new JsonObject { ["id"] = ProjectId });
            var tasks = await Services.Connection.RequestAsync("task.list", new JsonObject { ["projectId"] = ProjectId });
            var project = await Services.Connection.RequestAsync("project.get", new JsonObject { ["id"] = ProjectId });
            _all.Clear();
            foreach (var t in tasks.Objects("tasks"))
            {
                var item = new TaskItem();
                item.Update(t);
                _all.Add(item);
            }
            Choices.Clear();
            foreach (var m in project.Obj("project").Objects("members"))
                Choices.Add(new AssigneeChoice { User = Users.From(m.Obj("user")) });
        });
        Loaded = true;
        ApplyFilter();
    }

    public override void OnLeave()
    {
        if (Services.Connection.IsConnected) Services.Connection.Send("project.close", new JsonObject { ["id"] = ProjectId });
        base.OnLeave();
    }

    private void ApplyFilter()
    {
        string search = Search.Trim();
        var week = DateTime.Today.AddDays(7);
        IEnumerable<TaskItem> items = Filter switch
        {
            "mine" => _all.Where(t => !t.Done && t.IsMine),
            "overdue" => _all.Where(t => t.IsOverdue),
            "week" => _all.Where(t => !t.Done && t.Due.Date <= week),
            "done" => _all.Where(t => t.Done),
            "all" => _all,
            _ => _all.Where(t => !t.Done),
        };
        if (search.Length > 0)
            items = items.Where(t => t.Text.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                     || t.Assignees.Any(a => a.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)));
        var list = Filter == "done"
            ? items.OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue).ToList()
            : items.OrderBy(t => t.Done).ThenBy(t => t.Due).ThenBy(t => t.Id).ToList();
        Visible.Clear();
        foreach (var t in list) Visible.Add(t);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(OpenCount));
        OnPropertyChanged(nameof(MineCount));
        OnPropertyChanged(nameof(OverdueCount));
    }

    [RelayCommand] private void SetFilter(string key) => Filter = key;
    [RelayCommand] private void Back() => Services.Main.OpenProject(ProjectId);

    [RelayCommand]
    private void NewTask()
    {
        Editing = null;
        DraftText = "";
        DraftDue = DateTime.Today.AddDays(7);
        EditorError = "";
        foreach (var c in Choices) c.IsChecked = false;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void EditTask(TaskItem? task)
    {
        if (task == null || !CanEdit) return;
        Editing = task;
        DraftText = task.Text;
        DraftDue = task.Due;
        EditorError = "";
        foreach (var c in Choices) c.IsChecked = task.Assignees.Contains(c.User);
        IsEditorOpen = true;
    }

    [RelayCommand] private void CloseEditor() => IsEditorOpen = false;

    [RelayCommand]
    private async Task SaveTaskAsync()
    {
        EditorError = "";
        if (DraftText.Trim().Length == 0) { EditorError = "Describe the task."; return; }
        if (DraftDue == null) { EditorError = "Pick a due date."; return; }
        var assignees = new JsonArray();
        foreach (var c in Choices.Where(c => c.IsChecked)) assignees.Add(c.User.Id);
        var fields = new JsonObject
        {
            ["text"] = DraftText.Trim(),
            ["due"] = DraftDue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["assignees"] = assignees,
        };
        try
        {
            JsonObject reply;
            if (Editing == null)
            {
                fields["projectId"] = ProjectId;
                reply = await Services.Connection.RequestAsync("task.create", fields);
                var item = new TaskItem();
                item.Update(reply.Obj("task")!);
                _all.Add(item);
            }
            else
            {
                fields["id"] = Editing.Id;
                reply = await Services.Connection.RequestAsync("task.update", fields);
                Editing.Update(reply.Obj("task")!);
            }
            IsEditorOpen = false;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            EditorError = Describe(ex);
        }
    }

    [RelayCommand]
    private async Task ToggleDoneAsync(TaskItem? task)
    {
        if (task == null || !CanEdit) return;
        bool done = !task.Done;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("task.setDone", new JsonObject { ["id"] = task.Id, ["done"] = done });
            task.Update(reply.Obj("task")!);
            ApplyFilter();
            if (done) Services.Notifier.Success("Task completed.");
        });
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(TaskItem? task)
    {
        task ??= Editing;
        if (task == null) return;
        if (!await Services.Dialogs.ConfirmAsync("Delete task", $"Delete \"{task.Text}\"?", "Delete", danger: true)) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("task.delete", new JsonObject { ["id"] = task.Id })))
        {
            _all.Remove(task);
            if (Editing == task) IsEditorOpen = false;
            ApplyFilter();
        }
    }
}
