namespace UmbrellaManager;

using UmbrellaManager.Config;
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
    ConfirmPush,
    CancelPush,
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
    private readonly Dictionary<string, Dictionary<string, string>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tips = new(StringComparer.Ordinal);
    private readonly string _pinTitle;
    private CancellationTokenSource _cancel = new();
    private int _epoch;
    private bool _confirming;
    private bool _publishing;
    private bool _resultVisible;

    public SessionController(UmbrellaSource source, IReadOnlyList<string> branches, string pattern, string? pinTitle = null)
    {
        _source = source;
        _pattern = pattern;
        _pinTitle = string.IsNullOrWhiteSpace(pinTitle) ? LocalConfig.DefaultPinTitle : pinTitle;
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

    public IReadOnlyList<string>? PrProposal { get; private set; }

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
        if (_publishing && command != AppCommand.Quit)
        {
            return;
        }

        if (_resultVisible && command != AppCommand.Quit)
        {
            _resultVisible = false;
            PrProposal = null;
            if (command is AppCommand.ConfirmPush or AppCommand.CancelPush)
            {
                Raise();
                return;
            }
        }

        if (_confirming && command is not (AppCommand.ConfirmPush or AppCommand.CancelPush or AppCommand.Quit))
        {
            return;
        }

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
                StagePin();
                break;
            case AppCommand.AddModule:
                Say(IsProduction ? "Production ist schreibgeschützt." : "Hinzufügen folgt in der nächsten Phase.");
                break;
            case AppCommand.RemoveModule:
                Say(IsProduction ? "Production ist schreibgeschützt." : "Entfernen folgt in der nächsten Phase.");
                break;
            case AppCommand.CreatePr:
                if (IsProduction)
                {
                    Say("Promotion-PR folgt in der nächsten Phase.");
                    break;
                }

                AskPush();
                break;
            case AppCommand.ConfirmPush:
                if (_confirming)
                {
                    _ = PublishAsync();
                }

                break;
            case AppCommand.CancelPush:
                if (_confirming)
                {
                    _confirming = false;
                    PrProposal = null;
                    Say("Push abgebrochen. " + Status(Branch));
                }

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
                Message = Status(branch);
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
                    Message = Status(branch);
                    Raise();
                });
            }

            var tip = await _source.ReadTipAsync(branch, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                return;
            }

            if (tip is not null)
            {
                _tips[branch] = tip;
            }

            if (module is null)
            {
                return;
            }

            var snapshot = await _source.LoadGraphAsync(module, token, PendingSha(branch, module.Path)).ConfigureAwait(false);
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
                Message = Status(branch);
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
            var snapshot = await _source.LoadGraphAsync(module, token, Branch is null ? null : PendingSha(Branch, path)).ConfigureAwait(false);
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
                Message = Status(Branch);
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

    private void AskPush()
    {
        var changes = CurrentChanges();
        if (changes.Count == 0)
        {
            Say("Nichts vorgemerkt.");
            return;
        }

        if (Branch is null || !_tips.TryGetValue(Branch, out _))
        {
            Say("Der Stand des Branches ist unbekannt. Bitte mit r neu laden.");
            return;
        }

        _confirming = true;
        PrProposal = PinPublisher.Question(Branch, changes, _pinTitle);
        Say("Wirklich folgende Änderungen als Pull Request pushen? j ja, n nein");
    }

    private async Task PublishAsync()
    {
        var branch = Branch;
        if (branch is null || !_tips.TryGetValue(branch, out var tip))
        {
            _confirming = false;
            PrProposal = null;
            Say("Der Stand des Branches ist unbekannt. Bitte mit r neu laden.");
            return;
        }

        var changes = CurrentChanges();
        _confirming = false;
        _publishing = true;
        PrProposal = ["Lege Pull Requests an …"];
        Say("Lege Pull Requests an …");
        var epoch = _epoch;
        var token = Restart();
        try
        {
            var results = await _source.PublishPinsAsync(branch, tip, changes, _pinTitle, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                Post(() => { _publishing = false; });
                return;
            }

            Post(() =>
            {
                if (epoch != _epoch)
                {
                    return;
                }

                if (_pending.TryGetValue(branch, out var map))
                {
                    foreach (var result in results.Where(result => result.Created))
                    {
                        map.Remove(result.Path);
                    }
                }

                if (Selected is { IsFolder: false } row && _modules.TryGetValue(row.Path, out var module))
                {
                    var pending = PendingSha(branch, row.Path);
                    Graph = Graph.Select(commit => commit with { Tone = GraphBuilder.Tone(commit.Sha, module.Sha, pending) }).ToList();
                }

                _publishing = false;
                _resultVisible = true;
                PrProposal = results.Select(result => result.Text).ToList();
                Message = Status(branch);
                Raise();
            });
        }
        catch (OperationCanceledException)
        {
            Post(() =>
            {
                _publishing = false;
                PrProposal = null;
                Raise();
            });
        }
        catch (UserFacingException ex)
        {
            var message = ex.Message;
            Post(() =>
            {
                if (epoch != _epoch)
                {
                    return;
                }

                _publishing = false;
                PrProposal = null;
                Message = message;
                GraphMessage = message;
                Raise();
            });
        }
    }

    private List<PinChange> CurrentChanges()
    {
        var changes = new List<PinChange>();
        if (Branch is null || !_pending.TryGetValue(Branch, out var map))
        {
            return changes;
        }

        foreach (var pair in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _modules.TryGetValue(pair.Key, out var module);
            changes.Add(new PinChange(pair.Key, PinPublisher.ModuleName(pair.Key), module?.Sha, pair.Value, module?.Branch, module?.Url));
        }

        return changes;
    }

    private void StagePin()
    {
        if (IsProduction)
        {
            Say("Production ist schreibgeschützt.");
            return;
        }

        if (Branch is null || Focus != FocusPane.Graph || GraphIndex < 0 || GraphIndex >= Graph.Count)
        {
            Say("Commit im Graph wählen.");
            return;
        }

        if (Selected is not { IsFolder: false } row || !_modules.TryGetValue(row.Path, out var module))
        {
            Say("Ordner, kein Submodul.");
            return;
        }

        var chosen = Graph[GraphIndex].Sha;
        var map = PendingMap(Branch);
        if (Sha.Same(chosen, module.Sha))
        {
            map.Remove(row.Path);
        }
        else
        {
            map[row.Path] = chosen;
        }

        var pending = map.TryGetValue(row.Path, out var staged) ? staged : null;
        Graph = Graph.Select(commit => commit with { Tone = GraphBuilder.Tone(commit.Sha, module.Sha, pending) }).ToList();
        Say(Status(Branch));
    }

    private Dictionary<string, string> PendingMap(string branch)
    {
        if (!_pending.TryGetValue(branch, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            _pending[branch] = map;
        }

        return map;
    }

    private string? PendingSha(string branch, string path)
    {
        return _pending.TryGetValue(branch, out var map) && map.TryGetValue(path, out var sha) ? sha : null;
    }

    private string Status(string? branch)
    {
        var summary = "keine vorgemerkten Änderungen";
        if (branch is not null && _pending.TryGetValue(branch, out var map) && map.Count > 0)
        {
            summary = "vorgemerkt: " + string.Join(", ", map
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => ModuleName(pair.Key) + " " + Sha.Short(pair.Value)));
        }

        if (!_source.Offline)
        {
            return summary;
        }

        return summary == "keine vorgemerkten Änderungen"
            ? GitHub.GitHubText.Offline
            : GitHub.GitHubText.Offline + " · " + summary;
    }

    private static string ModuleName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
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
