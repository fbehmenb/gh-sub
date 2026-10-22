using UmbrellaManager.Input;
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
        Assert.Contains(rows, row => row.Name == "gb_modules" && !row.Expanded);
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
            ["Left", "Right", "Tab", "Up", "Down", "Space", "Enter", "A", "Delete", "C", "J", "N", "R", "Q"],
            keys);
        Assert.Equal(keys.Length, KeyMap.Bindings.Select(binding => binding.Command).Distinct().Count());
    }
}
