namespace UmbrellaManager;

using UmbrellaManager.Config;
using UmbrellaManager.Input;
using UmbrellaManager.Model;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;
using UmbrellaManager.Services;

public enum AppCommand
{
    PreviousBranch,
    NextBranch,
    FocusNext,
    FocusTree,
    FocusGraph,
    MoveUp,
    MoveDown,
    ToggleFolder,
    StagePin,
    ChooseNewer,
    AddModule,
    RemoveModule,
    CreatePr,
    ConfirmPush,
    CancelPush,
    Reload,
    ReloadAll,
    Quit,
    ShowHelp,
    OpenSetup,
    UpdateBranch,
}

public enum NoticeTone
{
    Idle,
    Info,
    Ok,
    Conflict,
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
    private readonly Dictionary<string, string> _compareStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pinTitle;
    private string? _promotionNote;
    private CancellationTokenSource _cancel = new();
    private int _epoch;
    private bool _confirming;
    private bool _publishing;
    private bool _resultVisible;
    private bool _successVisible;
    private int _successNote;
    private DateTime _successStarted;
    internal TimeSpan SuccessHold { get; set; } = TimeSpan.FromSeconds(10);
    private bool _conflict;
    private bool _pulling;
    private bool _loadFailed;
    private bool _setupOpen;
    private int _setupIndex;
    private int _pollSeconds;
    private string? _setupFile;
    private string? _watchNote;
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);
    private CancellationTokenSource? _pollCancel;
    private List<PinChange>? _proposal;
    private List<PinChange> _pushChanges = [];
    private readonly List<string> _pushLines = [];

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

    public bool Loading { get; private set; }

    public bool LoadingFailed { get; private set; }

    public bool CheckingRemote => _confirming && _proposal is null;

    public bool SuccessOpen => _successVisible;

    public double SuccessFraction
    {
        get
        {
            if (!_successVisible || SuccessHold <= TimeSpan.Zero)
            {
                return _successVisible ? 1 : 0;
            }

            var gone = DateTime.UtcNow - _successStarted;
            return Math.Clamp(gone.TotalMilliseconds / SuccessHold.TotalMilliseconds, 0, 1);
        }
    }

    public bool StartupFailed { get; private set; }

    public string LoadingMessage { get; private set; } = "";

    public int LoadingDone { get; private set; }

    public int LoadingTotal { get; private set; }

    public IReadOnlyList<string>? PrProposal { get; private set; }

    public int PushDone { get; private set; }

    public int PushTotal { get; private set; }

    public bool PushRunning => _publishing;

    public NoticeTone Notice { get; private set; } = NoticeTone.Idle;

    public bool HelpVisible { get; private set; }

    public bool SetupOpen => _setupOpen;

    public IReadOnlyList<string> SetupLines
    {
        get
        {
            var lines = new List<string> { "Einrichtung" };
            for (var index = 0; index < UserSetup.PollSteps.Length; index++)
            {
                var mark = index == _setupIndex ? ">" : " ";
                lines.Add(mark + "  " + UserSetup.PollLabel(UserSetup.PollSteps[index]));
            }

            lines.Add("Enter speichert    s schließt");
            return lines;
        }
    }

    public bool IsStale(string branch) => _stale.Contains(branch);

    public int PendingCount(string branch)
    {
        return _pending.TryGetValue(branch, out var map) ? map.Count : 0;
    }

    public string? StagedPin(string path)
    {
        return Branch is null ? null : PendingSha(Branch, path);
    }

    public string RowActions
    {
        get
        {
            var onCommit = Focus == FocusPane.Graph && GraphIndex >= 0 && GraphIndex < Graph.Count;
            var module = Selected is { IsFolder: false } row ? row : (VisibleRow?)null;
            var anyStaged = Branch is not null && _pending.TryGetValue(Branch, out var map) && map.Count > 0;
            return RowHint.Describe(new RowHintInput(
                HelpVisible,
                _publishing,
                _resultVisible,
                _confirming,
                IsProduction,
                Focus,
                Selected is { IsFolder: true },
                module is not null,
                GraphMessage.StartsWith("Lade", StringComparison.Ordinal),
                onCommit,
                onCommit ? Graph[GraphIndex].Tone : CommitTone.Gray,
                module is not null && PendingSha(Branch ?? "", module.Value.Path) is not null,
                anyStaged,
                _conflict,
                onCommit && module is not null && IsNewerOnSameBranch(Graph[GraphIndex], module.Value.Sha),
                _setupOpen,
                onCommit && Graph[GraphIndex].Promotable,
                CheckingRemote,
                _successVisible));
        }
    }

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
        if (Loading && command != AppCommand.Quit)
        {
            Raise();
            return;
        }

        if (_conflict && command is not (AppCommand.CancelPush or AppCommand.Quit))
        {
            Raise();
            return;
        }

        if (_setupOpen && command != AppCommand.Quit)
        {
            HandleSetup(command);
            return;
        }

        if (command == AppCommand.ShowHelp)
        {
            HelpVisible = !HelpVisible;
            Raise();
            return;
        }

        if (HelpVisible)
        {
            HelpVisible = false;
        }

        if (_publishing && command != AppCommand.Quit)
        {
            Raise();
            return;
        }

        if (_resultVisible && command != AppCommand.Quit)
        {
            DismissResult(_successVisible);
            if (command is AppCommand.ConfirmPush or AppCommand.CancelPush)
            {
                return;
            }
        }

        if (_confirming && command is not (AppCommand.ConfirmPush or AppCommand.CancelPush or AppCommand.Quit))
        {
            Raise();
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
            case AppCommand.FocusTree:
                Focus = FocusPane.Tree;
                Raise();
                break;
            case AppCommand.FocusGraph:
                Focus = FocusPane.Graph;
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
            case AppCommand.ChooseNewer:
                ChooseNewer();
                break;
            case AppCommand.AddModule:
                Say(IsProduction ? "Production ist schreibgeschützt." : "Hinzufügen folgt in der nächsten Phase.");
                break;
            case AppCommand.RemoveModule:
                ClearStagedPin();
                break;
            case AppCommand.CreatePr:
                _ = PreparePushAsync();
                break;
            case AppCommand.ConfirmPush:
                if (_confirming && _proposal is not null)
                {
                    _ = PublishAsync();
                }

                break;
            case AppCommand.CancelPush:
                if (_conflict)
                {
                    _conflict = false;
                    _resultVisible = false;
                    PrProposal = null;
                    Notice = NoticeTone.Idle;
                    Say(Status(Branch));
                    break;
                }

                if (_confirming)
                {
                    _confirming = false;
                    _proposal = null;
                    PrProposal = null;
                    Notice = NoticeTone.Idle;
                    _ = Restart();
                    Say("Push abgebrochen. " + Status(Branch));
                }

                break;
            case AppCommand.Reload:
                _ = ReloadModuleAsync();
                break;
            case AppCommand.ReloadAll:
                _ = ReloadAsync();
                break;
            case AppCommand.OpenSetup:
                HelpVisible = false;
                _setupIndex = UserSetup.PollIndex(_pollSeconds);
                _setupOpen = true;
                Raise();
                break;
            case AppCommand.UpdateBranch:
                _ = UpdateBranchAsync();
                break;
            case AppCommand.Quit:
                Cancel();
                QuitRequested?.Invoke();
                break;
        }
    }

    public Task LoadCurrentAsync() => LoadBranchAsync(BranchIndex, ++_epoch);

    public void BeginLoading(string message)
    {
        _source.Progress = null;
        Loading = true;
        LoadingFailed = false;
        LoadingMessage = message;
        LoadingDone = 0;
        LoadingTotal = 4;
        HelpVisible = false;
        _setupOpen = false;
        Raise();
    }

    public Task StartupAsync() => RefreshAsync(null, true);

    public void Cancel()
    {
        _cancel.Cancel();
        _pollCancel?.Cancel();
    }

    public void RememberTips(IReadOnlyDictionary<string, string> tips)
    {
        _tips.Clear();
        foreach (var pair in tips)
        {
            _tips[pair.Key] = pair.Value;
        }

        _stale.Clear();
    }

    public void NoteRemote(IReadOnlyDictionary<string, string>? remote)
    {
        if (remote is null)
        {
            _watchNote = "Remote-Prüfung fehlgeschlagen.";
        }
        else
        {
            _stale.Clear();
            foreach (var branch in RemoteWatch.Stale(_tips, remote))
            {
                if (Branches.Contains(branch))
                {
                    _stale.Add(branch);
                }
            }

            var unknown = RemoteWatch.Unlisted(Branches, remote.Keys, _pattern);
            _watchNote = unknown.Count switch
            {
                0 => null,
                1 => "Neuer Branch " + unknown[0] + ", Tab+r lädt die Tabs.",
                _ => unknown.Count + " neue Branches, Tab+r lädt die Tabs.",
            };
        }

        Message = Status(Branch);
        Raise();
    }

    public void UseSetup(string filePath, int? seconds)
    {
        _setupFile = filePath;
        _setupIndex = UserSetup.PollIndex(seconds);
        SetPollInterval(UserSetup.Normalize(seconds));
    }

    private void HandleSetup(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.OpenSetup:
                _setupOpen = false;
                Say(Status(Branch));
                break;
            case AppCommand.MoveUp:
                _setupIndex = Math.Max(0, _setupIndex - 1);
                Raise();
                break;
            case AppCommand.MoveDown:
                _setupIndex = Math.Min(UserSetup.PollSteps.Length - 1, _setupIndex + 1);
                Raise();
                break;
            case AppCommand.StagePin:
                SavePoll();
                break;
            default:
                Raise();
                break;
        }
    }

    private void SavePoll()
    {
        var seconds = UserSetup.PollSteps[_setupIndex];
        if (_setupFile is not null)
        {
            var existing = UserSetup.Load(_setupFile, out _);
            if (existing is not null)
            {
                (existing with { PollIntervalSeconds = seconds }).Save(_setupFile);
            }
        }

        _setupOpen = false;
        SetPollInterval(seconds);
        Say("Prüfung " + UserSetup.PollLabel(seconds) + ". " + Status(Branch));
    }

    private void SetPollInterval(int seconds)
    {
        _pollSeconds = seconds;
        var previous = _pollCancel;
        _pollCancel = new CancellationTokenSource();
        previous?.Cancel();
        if (seconds > 0)
        {
            _ = PollLoopAsync(seconds, _pollCancel.Token);
        }
    }

    private async Task PollLoopAsync(int seconds, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await CheckRemoteAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckRemoteAsync(CancellationToken token)
    {
        if (_publishing || _pulling || _confirming || _setupOpen)
        {
            return;
        }

        try
        {
            var remote = await _source.ListRemoteHeadsAsync(token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Post(() =>
            {
                if (token.IsCancellationRequested || _publishing || _pulling || _confirming)
                {
                    return;
                }

                NoteRemote(remote);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task UpdateBranchAsync()
    {
        if (Branch is null || _pulling || _publishing || _confirming)
        {
            return;
        }

        var branch = Branch;
        var index = BranchIndex;
        _pulling = true;
        var before = _modules.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Sha, StringComparer.Ordinal);
        var previous = _tips.TryGetValue(branch, out var loaded) ? loaded : null;
        var epoch = ++_epoch;
        Say("Hole " + branch + " …");
        var token = Restart();
        try
        {
            await _source.FetchBranchTipAsync(branch, token).ConfigureAwait(false);
            await LoadBranchAsync(index, epoch).ConfigureAwait(false);
            if (epoch != _epoch || _loadFailed)
            {
                Post(() => { _pulling = false; });
                return;
            }

            Post(() =>
            {
                if (epoch != _epoch)
                {
                    _pulling = false;
                    return;
                }

                _stale.Remove(branch);
                _pulling = false;
                IReadOnlyDictionary<string, string> pending = _pending.TryGetValue(branch, out var map)
                    ? map
                    : new Dictionary<string, string>();
                var after = _modules.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Sha, StringComparer.Ordinal);
                var moved = PendingBase.Moved(pending, before, after);
                var now = _tips.TryGetValue(branch, out var tip) ? tip : null;
                var advanced = previous is not null && now is not null && !previous.Equals(now, StringComparison.OrdinalIgnoreCase);
                if (moved.Count > 0)
                {
                    Message = "Basis geändert: " + string.Join(", ", moved) + ". " + Status(branch);
                }
                else if (advanced)
                {
                    Message = "Stand geholt. " + Status(branch);
                }
                else
                {
                    Message = branch + " ist aktuell. " + Status(branch);
                }

                Raise();
            });
        }
        catch (OperationCanceledException)
        {
            Post(() => { _pulling = false; });
        }
        catch (UserFacingException ex)
        {
            Fail(epoch, ex.Message);
            Post(() => { _pulling = false; });
        }
    }

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

    private Task ReloadModuleAsync()
    {
        if (Loading)
        {
            return Task.CompletedTask;
        }

        if (Selected is not { IsFolder: false } row || !_modules.ContainsKey(row.Path))
        {
            Say("Zuerst ein Submodul wählen.");
            return Task.CompletedTask;
        }

        if (_modules.TryGetValue(row.Path, out var module))
        {
            _source.ClearModule(module);
        }

        return LoadGraphForAsync(++_epoch, row.Path);
    }

    private Task ReloadAsync()
    {
        if (Loading)
        {
            return Task.CompletedTask;
        }

        return RefreshAsync(Selected?.Path, false);
    }

    private async Task RefreshAsync(string? keepPath, bool startup)
    {
        var epoch = ++_epoch;
        BeginLoading(startup ? "Hole Remote-Branches …" : "Lade neu …");
        var token = Restart();
        _source.Progress = Detail;
        try
        {
            _source.ClearCache();
            var branches = await _source.LoadBranchesAsync(_pattern, token).ConfigureAwait(false);
            if (epoch != _epoch)
            {
                return;
            }

            if (branches.Count == 0)
            {
                if (startup)
                {
                    FailLoading("Keine Remote-Branches gefunden.");
                }
                else
                {
                    CloseLoading();
                    Say("Keine Remote-Branches gefunden.");
                }

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

            var tips = _source.BranchTips;
            await OnUiAsync(() =>
            {
                Branches = branches;
                RememberTips(tips);
            }).ConfigureAwait(false);
            Advance("Lese Submodule …");
            await LoadBranchAsync(index, epoch, keepPath).ConfigureAwait(false);
            if (epoch == _epoch)
            {
                CloseLoading();
            }
        }
        catch (OperationCanceledException)
        {
            if (epoch == _epoch)
            {
                CloseLoading();
            }
        }
        catch (Exception ex)
        {
            if (epoch != _epoch)
            {
                return;
            }

            var message = ex is UserFacingException ? ex.Message : GitHub.GitHubText.Redact(ex.Message);
            if (startup && Branches.Count == 0)
            {
                FailLoading(message);
            }
            else
            {
                CloseLoading();
                Fail(epoch, message);
            }
        }
        finally
        {
            if (epoch == _epoch)
            {
                _source.Progress = null;
            }
        }
    }

    private async Task LoadBranchAsync(int index, int epoch, string? keepPath = null)
    {
        _loadFailed = false;
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
            var treeIndex = PreferredRow(rows, keepPath);
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
                RememberPromotion(null);
                PinMarks = new Dictionary<string, string>();
                Message = Status(branch);
                Raise();
            });

            if (Loading)
            {
                Advance(BranchOrder.IsProduction(branch) ? "Prüfe Pins …" : "Lese Branch-Spitze …");
            }

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

            if (Loading)
            {
                Advance("Lade Commits von " + ModuleName(module.Path) + " …");
            }

            var loaded = await LoadModuleGraphAsync(branch, module, PendingSha(branch, module.Path), token).ConfigureAwait(false);
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

                Lanes = loaded.Snapshot.Lanes;
                Graph = loaded.Snapshot.Rows;
                GraphIndex = PinRow(loaded.Snapshot.Rows, module.Sha);
                GraphMessage = loaded.Snapshot.Message;
                RememberPromotion(loaded.Promotion);
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
            RememberPromotion(null);
            Raise();
        });
        var token = Restart();
        if (!_modules.TryGetValue(path, out var module))
        {
            return;
        }

        try
        {
            var branchName = Branch;
            var loaded = branchName is null
                ? new LoadedGraph(await _source.LoadGraphAsync(module, token, null).ConfigureAwait(false), null)
                : await LoadModuleGraphAsync(branchName, module, PendingSha(branchName, path), token).ConfigureAwait(false);
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

                Lanes = loaded.Snapshot.Lanes;
                Graph = loaded.Snapshot.Rows;
                GraphIndex = PinRow(loaded.Snapshot.Rows, module.Sha);
                GraphMessage = loaded.Snapshot.Message;
                RememberPromotion(loaded.Promotion);
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

    private async Task PreparePushAsync()
    {
        var changes = CurrentChanges();
        if (changes.Count == 0)
        {
            Say("Nichts vorgemerkt.");
            return;
        }

        if (Branch is null || !_tips.TryGetValue(Branch, out _))
        {
            Say("Der Stand des Branches ist unbekannt. Bitte mit Tab+r neu laden.");
            return;
        }

        var branch = Branch;
        var loadedTip = _tips[branch];
        _confirming = true;
        _conflict = false;
        _proposal = null;
        Notice = NoticeTone.Info;
        PushDone = 0;
        PushTotal = 0;
        NoteCheck("Gleich zur Sicherheit den aktuellen Repo-Stand ab …");
        var epoch = _epoch;
        var token = Restart();
        try
        {
            var remoteTip = await _source.FetchBranchTipAsync(branch, token).ConfigureAwait(false);
            var moved = !remoteTip.Equals(loadedTip, StringComparison.OrdinalIgnoreCase);
            if (moved)
            {
                NoteCheck("Prüfe Pins gegen den aktuellen Stand …");
            }

            var clean = new List<PinChange>();
            var conflicts = new List<string>();
            foreach (var change in changes)
            {
                if (!moved)
                {
                    clean.Add(change);
                    continue;
                }

                var loadedLink = await _source.ReadGitlinkAsync(loadedTip, change.Path, token).ConfigureAwait(false);
                var remoteLink = await _source.ReadGitlinkAsync(remoteTip, change.Path, token).ConfigureAwait(false);
                if (PinPublisher.GitlinkConflicts(loadedLink, remoteLink))
                {
                    conflicts.Add(change.Name);
                }
                else
                {
                    clean.Add(change);
                }
            }

            if (clean.Count == 0)
            {
                var blocked = "Konflikt " + string.Join(", ", conflicts) + ": der Pin auf " + branch + " wurde geändert. u holt den Stand.";
                Post(() =>
                {
                    if (epoch != _epoch)
                    {
                        return;
                    }

                    _confirming = false;
                    _proposal = null;
                    _resultVisible = true;
                    Notice = NoticeTone.Conflict;
                    PrProposal = [blocked];
                    Message = blocked;
                    Raise();
                });
                return;
            }

            NoteCheck("Lese Versionen …");
            var resolved = new List<PinChange>(clean.Count);
            foreach (var change in clean)
            {
                var version = await _source.ModuleVersionAsync(change.ModuleUrl, change.NewSha, token).ConfigureAwait(false);
                resolved.Add(change with { Version = version });
            }

            Post(() =>
            {
                if (epoch != _epoch || !_confirming)
                {
                    return;
                }

                _proposal = resolved;
                Notice = NoticeTone.Info;
                PushDone = 0;
                PushTotal = resolved.Count;
                PrProposal = PinPublisher.PlanLines(branch, resolved, conflicts, moved, _pinTitle);
                Say("Pull Requests auf " + branch);
            });
        }
        catch (OperationCanceledException)
        {
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

                _confirming = false;
                _proposal = null;
                PrProposal = null;
                Notice = NoticeTone.Idle;
                Message = message;
                Raise();
            });
        }
    }

    private async Task PublishAsync()
    {
        var branch = Branch;
        var changes = _proposal;
        if (branch is null || changes is null || !_tips.TryGetValue(branch, out var tip))
        {
            _confirming = false;
            _proposal = null;
            PrProposal = null;
            Notice = NoticeTone.Idle;
            Say("Der Stand des Branches ist unbekannt. Bitte mit Tab+r neu laden.");
            return;
        }

        _pushChanges = changes.ToList();
        _proposal = null;
        _confirming = false;
        _publishing = true;
        Notice = NoticeTone.Info;
        PushDone = 0;
        PushTotal = changes.Count;
        _pushLines.Clear();
        _pushLines.Add("Push auf " + branch);
        _pushLines.Add("");
        foreach (var change in changes)
        {
            _pushLines.Add(PinPublisher.ChangeLine(change) + "   wartet");
        }

        PrProposal = _pushLines.ToArray();
        Say("Push auf " + branch);
        var epoch = _epoch;
        var token = Restart();
        try
        {
            var results = await _source.PublishPinsAsync(
                branch,
                tip,
                changes,
                _pinTitle,
                token,
                tick => Post(() =>
                {
                    if (epoch != _epoch || !_publishing)
                    {
                        return;
                    }

                    NotePush(tick);
                })).ConfigureAwait(false);
            var currentTip = await _source.ReadTipAsync(branch, token).ConfigureAwait(false);
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

                if (currentTip is not null
                    && _tips.TryGetValue(branch, out var loaded)
                    && !currentTip.Equals(loaded, StringComparison.OrdinalIgnoreCase))
                {
                    _stale.Add(branch);
                }

                _publishing = false;
                _conflict = results.Any(result => result.Conflict);
                var created = results.Where(result => result.Created).ToList();
                if (!_conflict && created.Count > 0)
                {
                    ShowSuccess(SuccessDetail(created));
                    return;
                }

                _resultVisible = true;
                PushDone = results.Count;
                PushTotal = results.Count;
                _pushLines.Clear();
                _pushLines.Add("Push auf " + branch);
                _pushLines.Add("");
                for (var index = 0; index < results.Count; index++)
                {
                    var label = index < _pushChanges.Count
                        ? PinPublisher.ChangeLine(_pushChanges[index])
                        : results[index].Path;
                    _pushLines.Add(label + "   " + results[index].Text);
                }

                _pushLines.Add("");
                _pushLines.Add("beliebige Taste schließt");
                Notice = _conflict ? NoticeTone.Conflict : NoticeTone.Info;
                PrProposal = _pushLines.ToArray();
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
                Notice = NoticeTone.Idle;
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
                Notice = NoticeTone.Idle;
                Message = message;
                GraphMessage = message;
                Raise();
            });
        }
    }

    private void NotePush(PushTick tick)
    {
        var line = tick.Index + 2;
        if (line >= 0 && line < _pushLines.Count)
        {
            _pushLines[line] = tick.Text;
        }

        if (tick.Finished)
        {
            PushDone = tick.Index + 1;
        }

        PushTotal = tick.Total;
        PrProposal = _pushLines.ToArray();
        Raise();
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

    private async Task<LoadedGraph> LoadModuleGraphAsync(
        string branch,
        SubmoduleEntry module,
        string? pending,
        CancellationToken token)
    {
        if (!BranchOrder.IsProduction(branch))
        {
            var plain = await _source.LoadGraphAsync(module, token, pending).ConfigureAwait(false);
            return new LoadedGraph(plain, null);
        }

        var partner = BranchOrder.StagingPartner(branch);
        string? note = null;
        string? stagingTip = null;
        IReadOnlyList<string> history = [];
        if (partner is null || !Branches.Contains(partner))
        {
            note = partner is null ? "Kein Staging-Branch zu diesem Tab." : "Kein Tab " + partner + ".";
        }
        else
        {
            var pins = await _source.LoadStagingHistoryAsync(partner, module.Path, token).ConfigureAwait(false);
            stagingTip = pins.Current;
            history = pins.Pins;
            if (stagingTip is null)
            {
                note = "Staging hat für dieses Modul keinen Pin.";
            }
        }

        if (string.IsNullOrEmpty(module.Sha))
        {
            note ??= "Production hat keinen Pin.";
        }

        var snapshot = await _source.LoadGraphAsync(module, token, pending, stagingTip).ConfigureAwait(false);
        var compared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(module.Sha) && history.Count > 0)
        {
            var candidates = history
                .Where(sha => !Sha.Same(sha, module.Sha))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var pair in await _source.ComparePinsAsync(module, module.Sha, candidates, token).ConfigureAwait(false))
            {
                compared[pair.Key] = pair.Value;
            }
        }

        var promotable = compared
            .Where(pair => StagingHistory.IsPromotable(true, pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = snapshot.Rows;
        var missing = promotable.Where(sha => rows.All(row => !Sha.Same(row.Sha, sha))).ToList();
        if (missing.Count > 0)
        {
            var extras = await _source.LoadCommitsAsync(module, missing, token).ConfigureAwait(false);
            rows = GraphBuilder.Include(rows, extras, module.Sha, pending);
        }

        rows = GraphBuilder.MarkPromotion(rows, module.Sha, stagingTip, promotable);
        return new LoadedGraph(snapshot with { Rows = rows }, new PromotionState(note, compared));
    }

    private void RememberPromotion(PromotionState? promotion)
    {
        _compareStatus.Clear();
        if (promotion is null)
        {
            _promotionNote = null;
            return;
        }

        _promotionNote = promotion.Note;
        foreach (var pair in promotion.Compared)
        {
            _compareStatus[pair.Key] = pair.Value;
        }
    }

    private void StagePromotion()
    {
        if (Branch is null || Selected is not { IsFolder: false } row || !_modules.TryGetValue(row.Path, out var module))
        {
            Say(Selected is { IsFolder: true } ? "Ordner, kein Submodul." : "Modul wählen.");
            return;
        }

        if (Graph.Count == 0)
        {
            Say(string.IsNullOrEmpty(GraphMessage) ? "Kein Commit gewählt." : GraphMessage);
            return;
        }

        var onCommit = Focus == FocusPane.Graph
            && GraphIndex >= 0
            && GraphIndex < Graph.Count;
        if (onCommit && Graph[GraphIndex].Promotable)
        {
            var chosen = Graph[GraphIndex].Sha;
            var pending = PendingSha(Branch, row.Path);
            ApplyPin(Sha.Same(chosen, pending) ? null : chosen, row, module);
            return;
        }

        JumpToStaging(row, module);
    }

    private void JumpToStaging(VisibleRow row, SubmoduleEntry module)
    {
        var index = -1;
        for (var i = 0; i < Graph.Count; i++)
        {
            if (Graph[i].Mark is PinMark.Staging or PinMark.Both)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            Say(_promotionNote ?? "Staging-Pin ist nicht im Graph.");
            return;
        }

        Focus = FocusPane.Graph;
        GraphIndex = index;
        var target = Graph[index];
        if (!target.Promotable)
        {
            Say(Refusal(target));
            return;
        }

        ApplyPin(target.Sha, row, module);
    }

    private string Refusal(GraphRow row)
    {
        if (row.Mark == PinMark.Both)
        {
            return "Staging und Production sind gleich.";
        }

        if (!_compareStatus.TryGetValue(row.Sha, out var status))
        {
            return _promotionNote ?? "Dieser Commit lag nie auf Staging.";
        }

        return status switch
        {
            "identical" => "Staging und Production sind gleich.",
            "behind" => "Dieser Staging-Pin ist älter als Production.",
            "diverged" => "Dieser Staging-Pin ist verzweigt, nicht neuer als Production.",
            _ => "Dieser Staging-Pin konnte nicht mit Production verglichen werden.",
        };
    }

    private void ClearPromotion()
    {
        if (Selected is not { IsFolder: false } row || !_modules.TryGetValue(row.Path, out var module) || Branch is null)
        {
            Say("Modul wählen.");
            return;
        }

        if (PendingSha(Branch, row.Path) is null)
        {
            Say("Keine Vormerkung.");
            return;
        }

        ApplyPin(null, row, module);
    }

    private void StagePin()
    {
        if (IsProduction)
        {
            StagePromotion();
            return;
        }

        if (!TrySelectedModule(true, out var row, out var module))
        {
            return;
        }

        var chosen = Graph[GraphIndex].Sha;
        ApplyPin(Sha.Same(chosen, module.Sha) ? null : chosen, row, module);
    }

    private void ChooseNewer()
    {
        if (IsProduction)
        {
            Say("In Production nur Commits, die schon auf Staging lagen. Enter wählt den Staging-Stand.");
            return;
        }

        if (!TrySelectedModule(true, out var row, out var module))
        {
            return;
        }

        var chosen = Graph[GraphIndex];
        if (!IsNewerOnSameBranch(chosen, module.Sha))
        {
            Say("c wählt nur einen neueren Commit auf demselben Branch.");
            return;
        }

        ApplyPin(chosen.Sha, row, module);
    }

    private void ClearStagedPin()
    {
        if (IsProduction)
        {
            ClearPromotion();
            return;
        }

        if (Selected is not { IsFolder: false } row || !_modules.TryGetValue(row.Path, out var module) || Branch is null)
        {
            Say("Modul wählen.");
            return;
        }

        if (PendingSha(Branch, row.Path) is null)
        {
            Say("Keine Vormerkung.");
            return;
        }

        ApplyPin(null, row, module);
    }

    private bool TrySelectedModule(bool graph, out VisibleRow row, out SubmoduleEntry module)
    {
        row = default;
        module = null!;
        if (IsProduction)
        {
            Say("Production ist schreibgeschützt.");
            return false;
        }

        if (Branch is null || (graph && (Focus != FocusPane.Graph || GraphIndex < 0 || GraphIndex >= Graph.Count)))
        {
            Say("Commit im Graph wählen.");
            return false;
        }

        if (Selected is not { IsFolder: false } selected || !_modules.TryGetValue(selected.Path, out var found))
        {
            Say("Ordner, kein Submodul.");
            return false;
        }

        row = selected;
        module = found;
        return true;
    }

    private bool IsNewerOnSameBranch(GraphRow chosen, string? pinSha)
    {
        if (chosen.Dots.Length == 0 || !chosen.Dots[0] || Sha.Same(chosen.Sha, pinSha))
        {
            return false;
        }

        var pin = Graph.FirstOrDefault(commit => Sha.Same(commit.Sha, pinSha));
        return pin is null || chosen.Date > pin.Date;
    }

    private void ApplyPin(string? sha, VisibleRow row, SubmoduleEntry module)
    {
        var map = PendingMap(Branch!);
        if (sha is null)
        {
            map.Remove(row.Path);
        }
        else
        {
            map[row.Path] = sha;
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

        if (branch is not null && _stale.Contains(branch))
        {
            summary = branch + " ist auf dem Remote neuer. u holt den Stand. · " + summary;
        }

        if (_watchNote is not null)
        {
            summary = _watchNote + " · " + summary;
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

    private Task OnUiAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                action();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return done.Task;
    }

    private void NoteCheck(string message)
    {
        Post(() =>
        {
            if (!_confirming || _proposal is not null)
            {
                return;
            }

            PrProposal = [message];
            Message = message;
            Raise();
        });
    }

    private void ShowSuccess(string detail)
    {
        var note = ++_successNote;
        _successVisible = true;
        _successStarted = DateTime.UtcNow;
        _resultVisible = true;
        _conflict = false;
        Notice = NoticeTone.Ok;
        PushDone = 0;
        PushTotal = 0;
        PrProposal = ["Erfolgreich", detail, "Taste schließt"];
        Message = "Erfolgreich. " + detail + "  Taste schließt";
        Raise();
        _ = HoldSuccessAsync(note);
    }

    private async Task HoldSuccessAsync(int note)
    {
        await Task.Delay(SuccessHold).ConfigureAwait(false);
        Post(() =>
        {
            if (note != _successNote || !_successVisible)
            {
                return;
            }

            DismissResult(true);
        });
    }

    private void DismissResult(bool success)
    {
        _resultVisible = false;
        _successVisible = false;
        _successNote++;
        PrProposal = null;
        if (success || Notice == NoticeTone.Ok)
        {
            Notice = NoticeTone.Idle;
            Message = Status(Branch);
        }
        else
        {
            Notice = NoticeTone.Idle;
        }

        Raise();
    }

    private static string SuccessDetail(IReadOnlyList<PinResult> created)
    {
        return string.Join(", ", created.Select(result =>
        {
            var mark = result.Text;
            var split = mark.LastIndexOf("PR ", StringComparison.Ordinal);
            var number = split >= 0 ? mark[split..] : mark;
            return number + " · " + ModuleName(result.Path);
        }));
    }

    private void Detail(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var epoch = _epoch;
        Post(() =>
        {
            if (!Loading || LoadingFailed || epoch != _epoch)
            {
                return;
            }

            LoadingMessage = message;
            Raise();
        });
    }

    private void Advance(string message)
    {
        var epoch = _epoch;
        Post(() =>
        {
            if (!Loading || LoadingFailed || epoch != _epoch)
            {
                return;
            }

            LoadingMessage = message;
            if (LoadingDone < LoadingTotal)
            {
                LoadingDone++;
            }

            Raise();
        });
    }

    private void CloseLoading()
    {
        Post(() =>
        {
            if (!Loading || LoadingFailed)
            {
                return;
            }

            Loading = false;
            _source.Progress = null;
            Raise();
        });
    }

    private void FailLoading(string message)
    {
        StartupFailed = true;
        Post(() =>
        {
            Loading = true;
            LoadingFailed = true;
            LoadingMessage = message;
            _source.Progress = null;
            Raise();
        });
    }

    private void Fail(int epoch, string message)
    {
        if (epoch != _epoch)
        {
            return;
        }

        _loadFailed = true;

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

    private static int PreferredRow(IReadOnlyList<VisibleRow> rows, string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Path, path, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return FirstModule(rows);
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

    private sealed record LoadedGraph(GraphSnapshot Snapshot, PromotionState? Promotion);

    private sealed record PromotionState(string? Note, IReadOnlyDictionary<string, string> Compared);
}
