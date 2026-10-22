namespace UmbrellaManager;

using UmbrellaManager.Model;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;
using UmbrellaManager.Services;

public enum AppCommand
{
    PreviousBranch,
    NextBranch,
    FocusNext,
    MoveUp,
    MoveDown,
    ToggleFolder,
    StagePin,
    AddModule,
    RemoveModule,
    CreatePr,
    Reload,
    Quit,
}

public enum FocusPane
{
    Tree,
    Graph,
}

public sealed class SessionController
{
    private readonly UmbrellaSource _source;
    private readonly string _pattern;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubmoduleEntry> _modules = new(StringComparer.Ordinal);
    private CancellationTokenSource _cancel = new();
    private int _epoch;

    public SessionController(UmbrellaSource source, IReadOnlyList<string> branches, string pattern)
    {
        _source = source;
        _pattern = pattern;
        Branches = branches;
    }

    public event Action? Changed;

    public event Action? QuitRequested;

    public Action<Action> Post { get; set; } = static action => action();

    public IReadOnlyList<string> Branches { get; private set; }

    public int BranchIndex { get; private set; }

    public string? Branch => Branches.Count == 0 ? null : Branches[BranchIndex];

    public bool IsProduction => BranchOrder.IsProduction(Branch);

    public IReadOnlyList<TreeNode> Roots { get; private set; } = [];

    public int TreeIndex { get; private set; }

    public int GraphIndex { get; private set; }

    public FocusPane Focus { get; private set; } = FocusPane.Tree;

    public IReadOnlyList<string> Lanes { get; private set; } = [];

    public IReadOnlyList<GraphRow> Graph { get; private set; } = [];

    public string GraphMessage { get; private set; } = "";

    public string Message { get; private set; } = "keine vorgemerkten Änderungen";

    public IReadOnlyDictionary<string, string> PinMarks { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyList<VisibleRow> Rows => SubmoduleTree.Flatten(Roots, _collapsed);

    public VisibleRow? Selected
    {
        get
        {
            var rows = Rows;
            return TreeIndex >= 0 && TreeIndex < rows.Count ? rows[TreeIndex] : null;
        }
    }

    public void Execute(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.PreviousBranch:
                _ = SelectBranchAsync(-1);
                break;
            case AppCommand.NextBranch:
                _ = SelectBranchAsync(1);
                break;
            case AppCommand.FocusNext:
                Focus = Focus == FocusPane.Tree ? FocusPane.Graph : FocusPane.Tree;
                Raise();
                break;
            case AppCommand.MoveUp:
                Move(-1);
                break;
            case AppCommand.MoveDown:
                Move(1);
                break;
            case AppCommand.ToggleFolder:
                ToggleFolder();
                break;
            case AppCommand.StagePin:
                Say(IsProduction ? "Production ist schreibgeschützt." : "Pin vormerken folgt in der nächsten Phase.");
                break;
            case AppCommand.AddModule:
                Say(IsProduction ? "Production ist schreibgeschützt." : "Hinzufügen folgt in der nächsten Phase.");
                break;
            case AppCommand.RemoveModule:
                Say(IsProduction ? "Production ist schreibgeschützt." : "Entfernen folgt in der nächsten Phase.");
                break;
            case AppCommand.CreatePr:
                Say(IsProduction ? "Promotion-PR folgt in der nächsten Phase." : "Pull Requests folgen in der nächsten Phase.");
                break;
            case AppCommand.Reload:
                _ = ReloadAsync();
                break;
            case AppCommand.Quit:
                _cancel.Cancel();
                QuitRequested?.Invoke();
                break;
        }
    }

    public Task LoadCurrentAsync() => LoadBranchAsync(BranchIndex, ++_epoch);

    public void Cancel() => _cancel.Cancel();

    private async Task SelectBranchAsync(int delta)
    {
        if (Branches.Count == 0)
        {
            return;
        }

        var index = BranchIndex + delta;
        index %= Branches.Count;
        if (index < 0)
        {
            index += Branches.Count;
        }

        await LoadBranchAsync(index, ++_epoch).ConfigureAwait(false);
    }

    private async Task ReloadAsync()
    {
        var epoch = ++_epoch;
        Say("Lade …");
        var token = Restart();
        try
        {
            _source.ClearCache();
            var branches = await _source.LoadBranchesAsync(_pattern, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                return;
            }

            var current = Branch;
            var index = 0;
            if (current is not null)
            {
                var found = -1;
                for (var i = 0; i < branches.Count; i++)
                {
                    if (branches[i] == current)
                    {
                        found = i;
                        break;
                    }
                }

                index = found >= 0 ? found : 0;
            }

            Post(() =>
            {
                Branches = branches;
            });
            await LoadBranchAsync(index, epoch).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (UserFacingException ex)
        {
            Fail(epoch, ex.Message);
        }
    }

    private async Task LoadBranchAsync(int index, int epoch)
    {
        if (Branches.Count == 0)
        {
            Say("Keine Remote-Branches gefunden.");
            return;
        }

        Post(() =>
        {
            Message = "Lade …";
            Raise();
        });
        var token = Restart();
        try
        {
            var branch = Branches[index];
            var roots = await _source.LoadTreeAsync(branch, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                return;
            }

            var rows = SubmoduleTree.Flatten(roots, new HashSet<string>(StringComparer.Ordinal));
            var treeIndex = FirstModule(rows);
            var selected = treeIndex >= 0 && treeIndex < rows.Count ? rows[treeIndex] : (VisibleRow?)null;
            var module = selected is { IsFolder: false } row ? SubmoduleTree.Find(roots, row.Path) : null;
            Post(() =>
            {
                if (epoch != _epoch)
                {
                    return;
                }

                BranchIndex = index;
                Roots = roots;
                _collapsed.Clear();
                _modules.Clear();
                IndexModules(roots);
                TreeIndex = treeIndex;
                Graph = [];
                Lanes = [];
                GraphMessage = selected?.IsFolder == true ? "Ordner, kein Submodul." : "Lade Commits …";
                PinMarks = new Dictionary<string, string>();
                Message = _source.Offline ? GitHub.GitHubText.Offline : "keine vorgemerkten Änderungen";
                Raise();
            });

            if (BranchOrder.IsProduction(branch))
            {
                var marks = await _source.LoadPinMarksAsync(Collect(roots), token).ConfigureAwait(false);
                if (epoch != _epoch)
                {
                    return;
                }

                Post(() =>
                {
                    if (epoch != _epoch)
                    {
                        return;
                    }

                    PinMarks = marks;
                    if (_source.Offline)
                    {
                        Message = GitHub.GitHubText.Offline;
                    }

                    Raise();
                });
            }

            if (module is null)
            {
                return;
            }

            var snapshot = await _source.LoadGraphAsync(module, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                return;
            }

            Post(() =>
            {
                if (epoch != _epoch)
                {
                    return;
                }

                Lanes = snapshot.Lanes;
                Graph = snapshot.Rows;
                GraphIndex = PinRow(snapshot.Rows, module.Sha);
                GraphMessage = snapshot.Message;
                if (_source.Offline)
                {
                    Message = GitHub.GitHubText.Offline;
                }

                Raise();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (UserFacingException ex)
        {
            Fail(epoch, ex.Message);
        }
    }

    private void Move(int delta)
    {
        if (Focus == FocusPane.Graph)
        {
            if (Graph.Count == 0)
            {
                return;
            }

            GraphIndex = Math.Clamp(GraphIndex + delta, 0, Graph.Count - 1);
            Raise();
            return;
        }

        var rows = Rows;
        if (rows.Count == 0)
        {
            return;
        }

        var previous = Selected?.Path;
        TreeIndex = Math.Clamp(TreeIndex + delta, 0, rows.Count - 1);
        Raise();
        if (Selected is { IsFolder: false } row && row.Path != previous)
        {
            _ = LoadGraphForAsync(++_epoch, row.Path);
        }
    }

    private async Task LoadGraphForAsync(int epoch, string path)
    {
        Post(() =>
        {
            Graph = [];
            GraphMessage = "Lade Commits …";
            Raise();
        });
        var token = Restart();
        if (!_modules.TryGetValue(path, out var module))
        {
            return;
        }

        try
        {
            var snapshot = await _source.LoadGraphAsync(module, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                return;
            }

            Post(() =>
            {
                if (epoch != _epoch)
                {
                    return;
                }

                Lanes = snapshot.Lanes;
                Graph = snapshot.Rows;
                GraphIndex = PinRow(snapshot.Rows, module.Sha);
                GraphMessage = snapshot.Message;
                if (_source.Offline)
                {
                    Message = GitHub.GitHubText.Offline;
                }

                Raise();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (UserFacingException ex)
        {
            Fail(epoch, ex.Message);
        }
    }

    private void ToggleFolder()
    {
        if (Focus != FocusPane.Tree || Selected is not { IsFolder: true } row)
        {
            return;
        }

        if (!_collapsed.Add(row.Path))
        {
            _collapsed.Remove(row.Path);
        }

        var rows = Rows;
        if (TreeIndex >= rows.Count)
        {
            TreeIndex = Math.Max(0, rows.Count - 1);
        }

        Raise();
    }

    private void Say(string message)
    {
        Message = message;
        Raise();
    }

    private void Fail(int epoch, string message)
    {
        if (epoch != _epoch)
        {
            return;
        }

        Post(() =>
        {
            if (epoch != _epoch)
            {
                return;
            }

            Message = message;
            GraphMessage = message;
            Raise();
        });
    }

    private CancellationToken Restart()
    {
        _cancel.Cancel();
        _cancel = new CancellationTokenSource();
        return _cancel.Token;
    }

    private static List<SubmoduleEntry> Collect(IEnumerable<TreeNode> nodes)
    {
        var modules = new List<SubmoduleEntry>();
        void Walk(IEnumerable<TreeNode> level)
        {
            foreach (var node in level)
            {
                if (node.IsModule)
                {
                    modules.Add(new SubmoduleEntry(node.Path, node.Sha, node.Url, node.Branch));
                }

                Walk(node.Children);
            }
        }

        Walk(nodes);
        return modules;
    }

    private void IndexModules(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsModule)
            {
                _modules[node.Path] = new SubmoduleEntry(node.Path, node.Sha, node.Url, node.Branch);
            }

            IndexModules(node.Children);
        }
    }

    private static int FirstModule(IReadOnlyList<VisibleRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsFolder)
            {
                return i;
            }
        }

        return 0;
    }

    private static int PinRow(IReadOnlyList<GraphRow> rows, string? sha)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Sha.Same(rows[i].Sha, sha))
            {
                return i;
            }
        }

        return 0;
    }

    private void Raise() => Changed?.Invoke();
}
