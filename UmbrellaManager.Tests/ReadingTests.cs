using System.Text;
using System.Text.Json;

using Terminal.Gui.Input;
using UmbrellaManager.Input;
using UmbrellaManager.Services;
using UmbrellaManager.Views;
using UmbrellaManager.Model;
using UmbrellaManager.Parsing;

namespace UmbrellaManager.Tests;

public class ReadingTests
{
    private const string Pin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Other = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Feature = "cccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Branches_are_grouped_and_pin_branches_are_hidden()
    {
        var ordered = BranchOrder.Apply(
            [
                "topic/x",
                "production/19.0",
                "chore/pin-gb_a-aaaaaaa-dev/19.0",
                "staging/19.0",
                "dev/18.0",
                "dev/19.0",
            ],
            "chore/pin-*");

        Assert.Equal(["dev/18.0", "dev/19.0", "staging/19.0", "production/19.0", "topic/x"], ordered);
        Assert.Equal("staging/19.0", BranchOrder.StagingPartner("production/19.0"));
        Assert.Null(BranchOrder.StagingPartner("dev/19.0"));
        Assert.Null(BranchOrder.StagingPartner("production/"));
    }

    [Fact]
    public void Long_messages_break_on_spaces_inside_the_column()
    {
        var lines = LineWrap.Split(
            "GitHub meldet 404. Das Repo ist nicht sichtbar. Bei privaten Repos fehlt oft die SSO-Freigabe der Organisation.",
            40);

        Assert.Equal(
            [
                "GitHub meldet 404. Das Repo ist nicht",
                "sichtbar. Bei privaten Repos fehlt oft",
                "die SSO-Freigabe der Organisation.",
            ],
            lines);
        Assert.All(lines, line => Assert.True(line.Length <= 40));
        Assert.Empty(LineWrap.Split("", 20));
    }

    [Fact]
    public void Staging_history_keeps_gitlinks_and_only_ahead_pins_are_promotable()
    {
        var older = new string('c', 40);
        var staging = new string('b', 40);
        var zeros = new string('0', 40);
        var raw = $":160000 160000 {zeros} {older} A\tgb_a\n\n:160000 160000 {older} {staging} M\tgb_a\n";

        var pins = StagingHistory.Parse(raw);

        Assert.Equal([older, staging], pins);
        Assert.True(StagingHistory.IsPromotable(true, "ahead"));
        Assert.False(StagingHistory.IsPromotable(true, "identical"));
        Assert.False(StagingHistory.IsPromotable(true, "behind"));
        Assert.False(StagingHistory.IsPromotable(true, "diverged"));
        Assert.False(StagingHistory.IsPromotable(false, "ahead"));
    }

    [Fact]
    public void Remote_heads_keep_slashes_in_the_branch_name()
    {
        var names = BranchOrder.ParseRemoteHeads("abc\trefs/heads/dev/19.0\n");
        Assert.Equal(["dev/19.0"], names);
    }

    [Fact]
    public void Tree_nests_modules_under_folders()
    {
        const string modules = """
            [submodule "gb_modules/gb_a"]
            	path = gb_modules/gb_a
            	url = https://github.com/Gahrens-Battermann-GmbH-Co-KG/gb_a.git
            	branch = 19.0
            [submodule "gb_modules/gb_b"]
            	path = gb_modules/gb_b
            	url = git@github.com:Gahrens-Battermann-GmbH-Co-KG/gb_b.git
            	branch = 19.0
            [submodule "ratocon_modules/ratocon_x"]
            	path = ratocon_modules/ratocon_x
            	url = https://github.com/Org/ratocon_x.git
            """;
        var tree = $"""
            160000 commit {Pin}	gb_modules/gb_a
            100644 blob ddd	README.md
            160000 commit {Other}	gb_modules/gb_b
            160000 commit {Feature}	ratocon_modules/ratocon_x
            """;

        var roots = SubmoduleTree.Build(SubmoduleTree.Merge(modules, tree));
        Assert.Equal(["gb_modules", "ratocon_modules"], roots.Select(node => node.Name).ToArray());
        Assert.Equal(["gb_a", "gb_b"], roots[0].Children.Select(node => node.Name).ToArray());
        Assert.Equal(Pin, roots[0].Children[0].Sha);
        Assert.Equal("19.0", roots[0].Children[0].Branch);
        Assert.Equal("Gahrens-Battermann-GmbH-Co-KG", GitHubRepo.FromUrl(roots[0].Children[1].Url)!.Owner);
        var relative = GitHubRepo.Resolve(
            "https://github.com/Gahrens-Battermann-GmbH-Co-KG/odoo_umbrella.git",
            "../gb_activity_actions.git");
        Assert.Equal("https://github.com/Gahrens-Battermann-GmbH-Co-KG/gb_activity_actions.git", relative);
        Assert.Equal("gb_activity_actions", GitHubRepo.FromUrl(relative)!.Name);
        Assert.Equal(
            "git@github.com:Gahrens-Battermann-GmbH-Co-KG/gb_activity_actions.git",
            GitHubRepo.Resolve("git@github.com:Gahrens-Battermann-GmbH-Co-KG/odoo_umbrella.git", "../gb_activity_actions.git"));

        var collapsed = new HashSet<string>(StringComparer.Ordinal) { "gb_modules" };
        var rows = SubmoduleTree.Flatten(roots, collapsed);
        Assert.DoesNotContain(rows, row => row.Name == "gb_a");
        Assert.Contains(rows, row => row.Name == "gb_modules" && !row.Expanded && row.Branch is null);

        var open = SubmoduleTree.Flatten(roots, new HashSet<string>(StringComparer.Ordinal));
        Assert.Contains(open, row => row.Name == "gb_a" && row.Branch == "19.0" && row.Sha == Pin);
        Assert.Contains(open, row => row.Name == "ratocon_x" && row.Branch is null);
    }

    [Fact]
    public void Graph_shows_the_pin_dark_green_and_a_pending_pin_light_green()
    {
        var older = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = older.AddDays(2);
        var standard = new[]
        {
            new CommitInfo(Pin, "aktuell", older),
            new CommitInfo(Other, "danach", newer),
        };
        var extra = new[]
        {
            ("feature", (IReadOnlyList<CommitInfo>)[new CommitInfo(Feature, "partner", newer.AddDays(1))]),
        };

        var rows = GraphBuilder.Build("19.0", standard, extra, Pin, pendingSha: Feature);

        Assert.Equal(Feature, rows[0].Sha);
        Assert.Equal(CommitTone.LightGreen, rows[0].Tone);
        Assert.Equal(CommitTone.DarkGreen, rows.Single(row => row.Sha == Pin).Tone);
        Assert.Equal(CommitTone.Gray, rows.Single(row => row.Sha == Other).Tone);
        Assert.True(rows.Single(row => row.Sha == Pin).Dots[0]);
        Assert.False(rows.Single(row => row.Sha == Pin).Dots[1]);

        var marked = GraphBuilder.MarkPromotion(
            rows,
            Pin,
            Other,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Other });
        Assert.Equal(PinMark.Production, marked.Single(row => row.Sha == Pin).Mark);
        Assert.Equal(PinMark.Staging, marked.Single(row => row.Sha == Other).Mark);
        Assert.True(marked.Single(row => row.Sha == Other).Promotable);
        Assert.False(marked.Single(row => row.Sha == Pin).Promotable);
        Assert.False(marked.Single(row => row.Sha == Feature).Promotable);
        Assert.Equal("production = staging", GraphBuilder.Badge(PinMark.Both));

        var same = GraphBuilder.MarkPromotion(rows, Pin, Pin, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pin });
        Assert.Equal(PinMark.Both, same.Single(row => row.Sha == Pin).Mark);
        Assert.False(same.Single(row => row.Sha == Pin).Promotable);
    }

    [Fact]
    public void Newer_partner_branch_becomes_a_lane_and_the_pin_branch_is_kept()
    {
        var day = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var standard = new BranchTip("19.0", Pin, day, false);
        var branches = new[]
        {
            standard,
            new BranchTip("feature", Feature, day.AddDays(1), false),
            new BranchTip("old", Other, day.AddDays(-1), false),
            new BranchTip("side", "dddddddddddddddddddddddddddddddddddddddd", day.AddDays(-2), true),
        };

        var lanes = GraphBuilder.SelectExtraLanes(standard, pinOnStandard: false, branches, new HashSet<string> { Pin });

        Assert.Equal(["side", "feature"], lanes.Select(lane => lane.Name).ToArray());
    }

    [Fact]
    public void Pin_on_standard_branch_is_ahead_or_identical()
    {
        Assert.True(CompareStatus.PinIsOnBranch("ahead"));
        Assert.True(CompareStatus.PinIsOnBranch("identical"));
        Assert.False(CompareStatus.PinIsOnBranch("behind"));
        Assert.False(CompareStatus.PinIsOnBranch("diverged"));
    }

    [Fact]
    public void Keys_are_defined_once()
    {
        var keys = KeyMap.Bindings.Select(binding => binding.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(
            ["Shift+Tab", "Tab", "Left", "Right", "Up", "Down", "PageUp", "PageDown", "Space", "Enter", "A", "Delete", "C", "P", "J", "N", "S", "U", "R", "Shift+R", "Q", "?", "O"],
            keys);
        Assert.Equal(keys.Length, KeyMap.Bindings.Select(binding => binding.Command).Distinct().Count());
        Assert.Equal(AppCommand.NextBranch, KeyMap.Match(Key.Tab));
        Assert.Equal(AppCommand.PreviousBranch, KeyMap.Match(Key.Tab.WithShift));
        Assert.Equal(AppCommand.FocusTree, KeyMap.Match(Key.CursorLeft));
        Assert.Equal(AppCommand.FocusGraph, KeyMap.Match(Key.CursorRight));
        Assert.Equal(AppCommand.MoveUp, KeyMap.Match(Key.CursorUp));
        Assert.Equal(AppCommand.MoveDown, KeyMap.Match(Key.CursorDown));
        Assert.Equal(AppCommand.PreviousModule, KeyMap.Match(Key.PageUp));
        Assert.Equal(AppCommand.NextModule, KeyMap.Match(Key.PageDown));
        Assert.Equal(AppCommand.Reload, KeyMap.Match(Key.R));
        Assert.Equal(AppCommand.ReloadAll, KeyMap.Match(Key.R.WithShift));
        Assert.Equal(AppCommand.Logout, KeyMap.Match(Key.O));
        Assert.Null(KeyMap.Match(Key.Esc));
    }

    [Fact]
    public void Default_branch_stays_a_lane_when_it_points_at_the_same_commit()
    {
        var day = new DateTimeOffset(2026, 10, 21, 17, 53, 49, TimeSpan.Zero);
        var standard = new BranchTip("19.0", Pin, day, false);
        var branches = new[]
        {
            standard,
            new BranchTip("20.0", Pin, day, false),
            new BranchTip("old", Other, day.AddDays(-2), false),
        };
        var shas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pin };

        Assert.Empty(GraphBuilder.SelectExtraLanes(standard, pinOnStandard: true, branches, shas));
        var lanes = GraphBuilder.SelectExtraLanes(standard, pinOnStandard: true, branches, shas, "20.0");
        Assert.Equal(["20.0"], lanes.Select(lane => lane.Name).ToArray());

        var parent = new CommitInfo(Other, "darunter", day.AddDays(-1));
        var commit = new CommitInfo(Pin, "spitze", day, Parents: [Other]);
        var rows = GraphBuilder.Build("19.0", [commit, parent], [("20.0", [commit])], Pin, null);
        var names = rows[0].Names;
        Assert.NotNull(names);
        Assert.Equal(["19.0", "20.0"], names);
        Assert.Equal("20.0", CommitGraph.ForkLabel(rows[0]));
        Assert.Equal(["●─● ", "│ ", "● "], Picture(rows));
    }

    [Fact]
    public void Branch_column_follows_the_tip_that_holds_the_pin()
    {
        var day = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var older = new CommitInfo(Other, "alt", day);
        var pin = new CommitInfo(Pin, "vendor", day.AddDays(1));
        var tips = new[]
        {
            new BranchTip("19.0", Other, day, false),
            new BranchTip("20.0", Pin, day.AddDays(1), false),
        };
        var rows = GraphBuilder.Build("19.0", [older], [("20.0", [pin])], Pin, null);

        Assert.Equal("20.0", PinBranch.FromGraph(rows, Pin, "19.0"));
        Assert.Equal("20.0", PinBranch.FromTips("19.0", Pin, tips));
        Assert.Equal("19.0", PinBranch.FromTips("19.0", Other, tips));
        Assert.Equal("19.0", PinBranch.FromTips("19.0", Feature, tips));
        Assert.Equal("19.0, 20.0", PinBranch.FromTips("19.0", Pin,
        [
            new BranchTip("20.0", Pin, day, false),
            new BranchTip("19.0", Pin, day, false),
        ]));
    }

    [Fact]
    public void Help_explains_every_key()
    {
        var help = string.Join('\n', KeyMap.Help);
        foreach (var phrase in new[] { "Submodule", "Commits", "bewegen", "Ordner", "Pin vormerken", "hinzufügen", "Entf", "PR", "pushen", "abbrechen", "Stand holen", "Einrichtung", "neu laden", "abmelden", "beenden", "?" })
        {
            Assert.Contains(phrase, help);
        }

        Assert.Contains("nächster Tab", help);
        Assert.Contains("vorheriger Tab", help);
        Assert.Contains("Bild↑", help);
        Assert.Contains("Bild↓", help);
        Assert.Contains("Umschalt+r", help);
        Assert.Equal(1, KeyMap.FooterHeight);
        Assert.Equal("? Hilfe", KeyMap.HelpHint);
        Assert.Contains("o abmelden", RowHint.Describe(new RowHintInput(
            true, false, false, false, false, FocusPane.Tree, false, false, false, false, CommitTone.Gray, false, false)));
        Assert.Contains("j meldet ab", RowHint.Describe(new RowHintInput(
            true, false, false, false, false, FocusPane.Tree, false, false, false, false, CommitTone.Gray, false, false, LogoutArmed: true)));
    }

    [Fact]
    public void Active_row_explains_enter_and_push()
    {
        var commit = RowHint.Describe(new RowHintInput(
            false, false, false, false, false, FocusPane.Graph, false, true, false, true, CommitTone.Gray, false, false));
        Assert.Contains("Enter diesen Commit vormerken", commit);
        Assert.DoesNotContain("p PR", commit);

        var staged = RowHint.Describe(new RowHintInput(
            false, false, false, false, false, FocusPane.Graph, false, true, false, true, CommitTone.LightGreen, true, true));
        Assert.Contains("Entf Vormerkung entfernen", staged);
        Assert.Contains("p PR vorbereiten", staged);

        var current = RowHint.Describe(new RowHintInput(
            false, false, false, false, false, FocusPane.Graph, false, true, false, true, CommitTone.DarkGreen, true, true));
        Assert.Contains("Entf Vormerkung entfernen", current);

        var fork = RowHint.Describe(new RowHintInput(
            false, false, false, false, false, FocusPane.Graph, false, true, false, true, CommitTone.Gray, false, false, ForkBranch: "20.0"));
        Assert.Contains("Enter diesen Commit vormerken · 20.0", fork);

        var newer = RowHint.Describe(new RowHintInput(
            false, false, false, false, false, FocusPane.Graph, false, true, false, true, CommitTone.Gray, false, false, false, true));
        Assert.Contains("c diesen neueren Commit wählen", newer);

        var folder = RowHint.Describe(new RowHintInput(
            false, false, false, false, false, FocusPane.Tree, true, false, false, false, CommitTone.Gray, false, false));
        Assert.Contains("Leertaste Ordner", folder);
        Assert.Contains("→ zu den Commits", folder);

        var production = RowHint.Describe(new RowHintInput(
            false, false, false, false, true, FocusPane.Graph, false, true, false, true, CommitTone.Gray, false, false));
        Assert.Contains("Enter auf den Staging-Pin", production);

        var promotable = RowHint.Describe(new RowHintInput(
            false, false, false, false, true, FocusPane.Graph, false, true, false, true, CommitTone.Gray, false, true, false, false, false, true));
        Assert.Contains("Enter diesen Staging-Pin vormerken", promotable);
        Assert.Contains("p PR vorbereiten", promotable);

        var asking = RowHint.Describe(new RowHintInput(
            false, false, false, true, false, FocusPane.Graph, false, true, false, true, CommitTone.Gray, true, true));
        Assert.Contains("j diese Änderungen pushen", asking);
        Assert.Contains("n abbrechen", asking);

        var lines = PinPublisher.PlanLines(
            "dev/19.0",
            [
                new PinChange("gb_a", "gb_a", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "19.0", null, "19.0.1.0.1"),
                new PinChange("gb_modules/gb_b", "gb_b", "cccccccccccccccccccccccccccccccccccccccc", "dddddddddddddddddddddddddddddddddddddddd", "19.0", null),
            ],
            [],
            false,
            "chore(<modul>): pin");
        Assert.Contains(lines, line => line.Contains("gb_a", StringComparison.Ordinal) && line.Contains("aaaaaaa → bbbbbbb", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("gb_b", StringComparison.Ordinal) && line.Contains("ccccccc → ddddddd", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("chore(gb_a): pin", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("nach dev/19.0 über chore/pin-gb_b-", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("j pushen", StringComparison.Ordinal) && line.Contains("n abbrechen", StringComparison.Ordinal));
        Assert.Equal("░░░░░░ 0/2", PinPublisher.ProgressBar(0, 2, 10));
        Assert.Equal("███░░░ 1/2", PinPublisher.ProgressBar(1, 2, 10));
        Assert.Equal("██████ 2/2", PinPublisher.ProgressBar(2, 2, 10));
        var running = LoadMeter.Bar(0, 4, 0, 16);
        Assert.Equal(16, running.Length);
        Assert.EndsWith(" 0/4", running);
        Assert.Contains('█', running);
        Assert.Contains('░', running);
        Assert.NotEqual(running, LoadMeter.Bar(0, 4, 2, 16));
        var halfway = LoadMeter.Bar(2, 4, 0, 20);
        Assert.StartsWith("█", halfway);
        Assert.EndsWith(" 2/4", halfway);
        Assert.DoesNotContain('░', LoadMeter.Bar(4, 4, 1, 16));
        Assert.Equal("░░░░", LoadMeter.Fill(0, 4));
        Assert.Equal("██░░", LoadMeter.Fill(0.5, 4));
        Assert.Equal("████", LoadMeter.Fill(1, 4));
        Assert.True(PinPublisher.IsConflicting("{\"mergeable\":\"CONFLICTING\"}"));
        Assert.False(PinPublisher.IsConflicting("{\"mergeable\":\"MERGEABLE\"}"));
        Assert.Equal(
            "Jetzt: nur n abbrechen",
            RowHint.Describe(new RowHintInput(false, false, true, false, false, FocusPane.Graph, false, true, false, true, CommitTone.Gray, true, true, true)));
    }

    [Fact]
    public void Manifest_version_is_read_from_the_github_contents_payload()
    {
        const string manifest = """
            {
                'name': 'Documents Archive',
                'version': '19.0.1.8.14',
            }
            """;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifest));
        var wrapped = encoded[..20] + "\n" + encoded[20..];
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["content"] = wrapped,
        });

        Assert.Equal("19.0.1.8.14", GitHubParse.ParseManifestVersion(json));
        Assert.Null(GitHubParse.ManifestVersion("{'name': 'ohne version'}"));
    }

    [Fact]
    public void Remote_tips_mark_a_moved_branch_and_keep_a_pending_base()
    {
        const string oldTip = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string newTip = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string other = "cccccccccccccccccccccccccccccccccccccccc";
        var text = oldTip + "\trefs/heads/dev/19.0\n" + other + "\trefs/heads/chore/pin-x-aaaaaaa-dev/19.0\n" + newTip + "\trefs/heads/topic/x\n";
        var tips = BranchOrder.ParseRemoteTips(text);
        Assert.Equal(oldTip, tips["dev/19.0"]);
        Assert.Equal(["dev/19.0"], RemoteWatch.Stale(new Dictionary<string, string> { ["dev/19.0"] = newTip }, tips));
        Assert.Equal(["topic/x"], RemoteWatch.Unlisted(["dev/19.0"], tips.Keys, "chore/pin-*"));
        Assert.Equal(
            ["gb_a"],
            PendingBase.Moved(
                new Dictionary<string, string> { ["gb_modules/gb_a"] = other },
                new Dictionary<string, string?> { ["gb_modules/gb_a"] = oldTip },
                new Dictionary<string, string?> { ["gb_modules/gb_a"] = newTip }));
        Assert.Empty(PendingBase.Moved(
            new Dictionary<string, string> { ["gb_a"] = other },
            new Dictionary<string, string?> { ["gb_a"] = oldTip },
            new Dictionary<string, string?> { ["gb_a"] = oldTip }));
        Assert.False(PinPublisher.GitlinkConflicts(oldTip, oldTip));
        Assert.True(PinPublisher.GitlinkConflicts(oldTip, newTip));
        Assert.Contains("setzt auf dem neuen Stand auf", PinPublisher.PushPreview("dev/19.0", [], ["gb_a"], true, "titel"));
    }

    [Fact]
    public void Graphql_branch_list_keeps_the_tip_date_so_a_newer_branch_becomes_a_lane()
    {
        const string json = """
            {"data":{"repository":{"refs":{"nodes":[
              {"name":"19.0","target":{"oid":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","committedDate":"2026-10-19T04:23:57Z"}},
              {"name":"19.0-staging","target":{"oid":"cccccccccccccccccccccccccccccccccccccccc","committedDate":"2026-10-23T06:43:28Z"}},
              {"name":"leer","target":null}
            ]}}}}
            """;
        var tips = GitHubParse.ParseBranches(json);
        var staging = Assert.Single(tips, tip => tip.Name == "19.0-staging");
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 6, 43, 28, TimeSpan.Zero), staging.TipDate);
        Assert.DoesNotContain(tips, tip => tip.Name == "leer");

        var standard = tips.Single(tip => tip.Name == "19.0");
        var lanes = GraphBuilder.SelectExtraLanes(standard, pinOnStandard: true, tips, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pin });
        Assert.Equal(["19.0-staging"], lanes.Select(lane => lane.Name).ToArray());

        var undated = tips.Select(tip => tip with { TipDate = null }).ToArray();
        Assert.Empty(GraphBuilder.SelectExtraLanes(undated[0], pinOnStandard: true, undated, new HashSet<string> { Pin }));
    }

    [Fact]
    public void Commit_parse_keeps_the_parent_sha()
    {
        const string commits = """
            [{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","parents":[{"sha":"cccccccccccccccccccccccccccccccccccccccc"}],"commit":{"message":"betreff\nrest","committer":{"date":"2026-10-01T00:00:00Z"}}}]
            """;
        var commit = Assert.Single(GitHubParse.ParseCommits(commits));
        Assert.Equal("betreff", commit.Subject);
        Assert.Equal(["cccccccccccccccccccccccccccccccccccccccc"], commit.Parents);
    }

    [Fact]
    public void Commit_graph_connects_a_merge_and_names_the_branch_tip()
    {
        var day = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        const string tip = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string merge = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string left = "cccccccccccccccccccccccccccccccccccccccc";
        const string right = "dddddddddddddddddddddddddddddddddddddddd";
        const string root = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var standard = new[]
        {
            new CommitInfo(tip, "A", day.AddDays(4), Parents: [merge]),
            new CommitInfo(merge, "B", day.AddDays(3), Parents: [left, right]),
            new CommitInfo(left, "C", day.AddDays(2), Parents: [root]),
            new CommitInfo(root, "E", day, Parents: []),
        };
        var extra = new[]
        {
            ("feature", (IReadOnlyList<CommitInfo>)[new CommitInfo(right, "D", day.AddDays(1), Parents: [root])]),
        };

        var rows = GraphBuilder.Build("19.0", standard, extra, pinSha: null, pendingSha: null);
        Assert.Equal(["19.0"], rows[0].Names);
        Assert.Equal("pin ", GraphBuilder.Stamp(CommitTone.DarkGreen));
        Assert.Equal("neu ", GraphBuilder.Stamp(CommitTone.LightGreen));
        Assert.Equal("    ", GraphBuilder.Stamp(CommitTone.Gray));
        Assert.Contains(rows, row => row.Sha == right && row.Names!.Contains("feature"));
        Assert.Equal('*', rows[0].Cells![0].Symbol);
        Assert.Contains(rows, row => row.Cells!.Any(cell => cell.Symbol is '/' or '\\'));
        var glyphs = string.Concat(rows.Single(row => row.Sha == merge).Cells!.Select(cell => cell.Symbol));
        Assert.Contains("\\", glyphs);
        var link = CommitGraph.Link(rows[1].Cells);
        Assert.Contains(link, cell => cell.Symbol == '|');
        Assert.Equal(
            ["● ", "│   ", "●─╮ ", "│ │ ", "● │ ", "│ │ ", "│ ● ", "│ │ ", "●─╯ "],
            Picture(rows));
    }

    [Fact]
    public void Unmerged_feature_branch_forks_beside_the_default_line()
    {
        var day = new DateTimeOffset(2026, 10, 21, 17, 53, 49, TimeSpan.Zero);
        const string middle = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string newest = "ffffffffffffffffffffffffffffffffffffffff";
        var standard = new[]
        {
            new CommitInfo(Pin, "spitze", day, Parents: [Other]),
            new CommitInfo(Other, "darunter", day.AddDays(-1), Parents: []),
        };
        var feature = new[]
        {
            new CommitInfo(newest, "preview", day.AddDays(3), Parents: [middle]),
            new CommitInfo(middle, "apis", day.AddDays(2), Parents: [Feature]),
            new CommitInfo(Feature, "bump", day.AddDays(1), Parents: [Pin]),
        };

        var rows = GraphBuilder.Build("20.0", standard, [("GBoo/413-portierung-20", feature)], pinSha: null, pendingSha: null);

        Assert.Equal(["GBoo/413-portierung-20"], rows[0].Names);
        Assert.Equal(' ', rows[0].Cells![0].Symbol);
        Assert.Equal('*', rows[0].Cells![1].Symbol);
        var tip = rows.Single(row => row.Sha == Pin);
        Assert.Equal(["20.0"], tip.Names);
        Assert.Equal('*', tip.Cells![0].Symbol);
        Assert.Contains(tip.Cells!, cell => cell.Symbol == '/');
        Assert.All(feature, commit => Assert.False(rows.Single(row => row.Sha == commit.Sha).Dots[0]));
        Assert.Equal(
            ["  ● ", "  │ ", "  ● ", "  │ ", "  ● ", "│ │ ", "●─╯ ", "│ ", "● "],
            Picture(rows));
    }

    [Fact]
    public void Feature_fork_keeps_the_curve_into_a_merge_commit()
    {
        var day = new DateTimeOffset(2026, 10, 21, 17, 53, 49, TimeSpan.Zero);
        const string side = "dddddddddddddddddddddddddddddddddddddddd";
        const string ahead = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var standard = new[]
        {
            new CommitInfo(Pin, "spitze", day, Parents: [Other, side]),
            new CommitInfo(side, "zweiter", day.AddDays(-1), Parents: [Other]),
            new CommitInfo(Other, "erster", day.AddDays(-2), Parents: []),
        };
        var feature = new[]
        {
            new CommitInfo(ahead, "preview", day.AddDays(1), Parents: [Pin]),
        };

        var rows = GraphBuilder.Build("20.0", standard, [("GBoo/413-portierung-20", feature)], pinSha: null, pendingSha: null);

        Assert.Equal(
            ["  ● ", "│ │   ", "●─┴─╮ ", "│   │ ", "│   ● ", "│   │ ", "●───╯ "],
            Picture(rows));
    }

    [Fact]
    public void Commit_graph_paints_rounded_branches()
    {
        var arriving = CommitGraph.Paint([
            new GraphCell('*', 0),
            new GraphCell('/', 1),
            new GraphCell('\\', 2),
        ]);
        Assert.Equal("●─┴─╮ ", Line(arriving));
        Assert.Equal(1, arriving[2].Lane);
        Assert.Equal("│ │   ", Line(CommitGraph.PaintLink([
            new GraphCell('*', 0),
            new GraphCell('/', 1),
            new GraphCell('\\', 2),
        ])));
        Assert.Equal("●─╮ ", Line(CommitGraph.Paint([new GraphCell('*', 0), new GraphCell('\\', 1)])));
        Assert.Equal("●─╯ ", Line(CommitGraph.Paint([new GraphCell('*', 0), new GraphCell('/', 1)])));
        Assert.Equal("╰─● ", Line(CommitGraph.Paint([new GraphCell('\\', 0), new GraphCell('*', 1)])));
        Assert.Equal("╭─● ", Line(CommitGraph.Paint([new GraphCell('/', 0), new GraphCell('*', 1)])));
        Assert.Equal("●─┼─╮ ", Line(CommitGraph.Paint([
            new GraphCell('*', 0),
            new GraphCell('|', 1),
            new GraphCell('\\', 2),
        ])));
        Assert.Equal("●─┼───╮ ", Line(CommitGraph.Paint([
            new GraphCell('*', 0),
            new GraphCell('+', 1),
            new GraphCell('-', 2),
            new GraphCell('\\', 3),
        ])));
        Assert.Equal("│   ", Line(CommitGraph.PaintLink([new GraphCell('*', 0), new GraphCell('\\', 1)])));
        Assert.Equal("│ │ ", Line(CommitGraph.PaintLink([new GraphCell('*', 0), new GraphCell('/', 1)])));
        Assert.Equal("│ │ ", Line(CommitGraph.PaintLink([new GraphCell('\\', 0), new GraphCell('*', 1)])));
        Assert.Equal("  │ ", Line(CommitGraph.PaintLink([new GraphCell('/', 0), new GraphCell('*', 1)])));
        var branch = CommitGraph.Paint([new GraphCell('*', 0), new GraphCell('\\', 1)]);
        Assert.Equal(0, branch[0].Lane);
        Assert.Equal(1, branch[2].Lane);
    }

    private static string Line(IReadOnlyList<CommitGraph.Glyph> glyphs)
    {
        return string.Concat(glyphs.Select(glyph => glyph.Symbol));
    }

    private static List<string> Picture(IReadOnlyList<GraphRow> rows)
    {
        var lines = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            lines.Add(Line(CommitGraph.PaintRow(rows[i])));
            if (i + 1 < rows.Count)
            {
                lines.Add(Line(CommitGraph.PaintLink(rows[i + 1].Cells)));
            }
        }

        return lines;
    }
}
