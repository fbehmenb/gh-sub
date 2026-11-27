using UmbrellaManager.Model;
using UmbrellaManager.Parsing;
using UmbrellaManager.Process;
using UmbrellaManager.Services;

namespace UmbrellaManager.Tests;

public class PullPinTests
{
    private const string Waiting = "7a75ee53d3d2dea2df18fad3dd52464493173fab";

    private const string Merged = "a7e938bc733f4ad8d9709e6ae7454b97cf7634c3";

    [Fact]
    public void Open_pulls_are_counted_and_pin_commits_are_marked()
    {
        var snapshot = PullPins.Read(OpenJson(), MergedJson());

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.OpenCount);
        Assert.False(snapshot.Truncated);
        Assert.Equal("2 offene PRs", PullPins.CountText(snapshot.OpenCount, snapshot.Truncated));
        Assert.Equal("1 offener PR", PullPins.CountText(1, false));
        Assert.Equal("keine offenen PRs", PullPins.CountText(0, false));
        Assert.Equal("100+ offene PRs", PullPins.CountText(100, true));

        var waiting = PullPins.Describe(Waiting, "staging/20.0", snapshot.Pins);
        var merged = PullPins.Describe(Merged, "production/20.0", snapshot.Pins);
        var otherTab = PullPins.Describe(Waiting, "dev/20.0", snapshot.Pins);

        Assert.Equal(PullState.Waiting, waiting.State);
        Assert.Equal("wartet in PR #49", waiting.Label);
        Assert.Equal(PullState.Merged, merged.State);
        Assert.Equal("ist gemerged", merged.Label);
        Assert.Equal(PullState.None, otherTab.State);
        Assert.Equal("", otherTab.Label);
    }

    [Fact]
    public void A_waiting_pull_wins_over_merged_and_stays_off_the_local_mark()
    {
        const string both = """
            [{"number":49,"state":"OPEN","baseRefName":"staging/20.0","body":"Neuer Pin: 7a75ee53d3d2dea2df18fad3dd52464493173fab"}]
            """;
        const string again = """
            [{"number":40,"state":"MERGED","baseRefName":"staging/20.0","body":"Neuer Pin: 7a75ee53d3d2dea2df18fad3dd52464493173fab"}]
            """;
        var snapshot = PullPins.Read(both, again);
        Assert.NotNull(snapshot);
        var note = PullPins.Describe(Waiting, "staging/20.0", snapshot.Pins);
        Assert.Equal("wartet in PR #49", note.Label);

        var row = new GraphRow(Waiting, "proxy", DateTimeOffset.UnixEpoch, [true], CommitTone.LightGreen);
        var marked = PullPins.Apply([row], "staging/20.0", snapshot.Pins);
        Assert.Equal(CommitTone.LightGreen, marked[0].Tone);
        Assert.Equal(PullState.Waiting, marked[0].Pull);
        Assert.Equal("wartet in PR #49", marked[0].PullLabel);
    }

    [Fact]
    public void Broken_pull_json_keeps_the_previous_state()
    {
        Assert.Null(PullPins.Read("kein json", "[]"));

        var controller = new SessionController(
            new UmbrellaSource(new SilentProcess(), new ResponseCache(), "C:\\repo"),
            ["staging/20.0"],
            "chore/pin-*");
        controller.NotePulls(PullPins.Read(OpenJson(), "[]"));
        controller.NotePulls(null);

        Assert.Equal("2 offene PRs", controller.OpenPullText);
        Assert.Equal("wartet in PR #49", controller.PullFor(Waiting).Label);
    }

    private static string OpenJson()
    {
        return """
            [
              {"number":12,"state":"OPEN","baseRefName":"dev/20.0","body":"kein Pin"},
              {"number":49,"state":"OPEN","baseRefName":"staging/20.0","body":"Alter Pin: a7e938bc733f4ad8d9709e6ae7454b97cf7634c3\nNeuer Pin: 7a75ee53d3d2dea2df18fad3dd52464493173fab\nBranch: 19.0"}
            ]
            """;
    }

    private static string MergedJson()
    {
        return """
            [
              {"number":47,"state":"MERGED","baseRefName":"production/20.0","body":"Neuer Pin: a7e938bc733f4ad8d9709e6ae7454b97cf7634c3"},
              {"number":3,"state":"CLOSED","baseRefName":"production/20.0","body":"Neuer Pin: a7e938bc733f4ad8d9709e6ae7454b97cf7634c3"}
            ]
            """;
    }

    private sealed class SilentProcess : IExternalProcess
    {
        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }
}
