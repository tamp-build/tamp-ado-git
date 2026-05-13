using System;
using System.Linq;
using System.Text;
using Tamp;
using Tamp.AdoGit;
using Xunit;

namespace Tamp.AdoGit.Tests;

public sealed class AdoGitTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/git"));
    private static Secret FakePat() => new("ado-pat", "abc123");

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- Auth header construction ----

    [Fact]
    public void BuildAuthHeader_Matches_Basic_Format()
    {
        var pat = new Secret("p", "supersecret");
        var header = AdoGit.BuildAuthHeader(pat);
        var expected = "AUTHORIZATION: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":supersecret"));
        Assert.Equal(expected, header);
    }

    [Fact]
    public void Plan_Prepends_Http_ExtraHeader_Config()
    {
        var plan = AdoGit.Fetch(FakeTool(), FakePat());
        Assert.Equal("-c", plan.Arguments[0]);
        Assert.StartsWith("http.extraHeader=AUTHORIZATION: Basic ", plan.Arguments[1]);
    }

    [Fact]
    public void Plan_Registers_Pat_In_Secrets_List()
    {
        var pat = FakePat();
        var plan = AdoGit.Fetch(FakeTool(), pat);
        Assert.Contains(pat, plan.Secrets);
    }

    [Fact]
    public void Plan_Rejects_Null_Tool()
    {
        Assert.Throws<ArgumentNullException>(() => AdoGit.Fetch(null!, FakePat()));
    }

    [Fact]
    public void Plan_Rejects_Null_Pat()
    {
        Assert.Throws<ArgumentNullException>(() => AdoGit.Fetch(FakeTool(), null!));
    }

    // ---- Fetch ----

    [Fact]
    public void Fetch_Defaults_Remote_To_Origin()
    {
        var plan = AdoGit.Fetch(FakeTool(), FakePat());
        Assert.Contains("fetch", plan.Arguments);
        Assert.Contains("origin", plan.Arguments);
    }

    [Fact]
    public void Fetch_Tags_Prune_Depth_Refspec()
    {
        var plan = AdoGit.Fetch(FakeTool(), FakePat(), s => s
            .SetRemote("upstream")
            .SetRefspec("refs/heads/main")
            .SetTags()
            .SetPrune()
            .SetDepth(50));
        Assert.Contains("--tags", plan.Arguments);
        Assert.Contains("--prune", plan.Arguments);
        Assert.Contains("--depth=50", plan.Arguments);
        Assert.Contains("upstream", plan.Arguments);
        Assert.Contains("refs/heads/main", plan.Arguments);
    }

    // ---- Push ----

    [Fact]
    public void Push_Requires_Ref()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AdoGit.Push(FakeTool(), FakePat(), _ => { }).Arguments.ToList());
    }

    [Fact]
    public void Push_Default_Shape()
    {
        var plan = AdoGit.Push(FakeTool(), FakePat(), s => s.SetRef("HEAD:refs/heads/main"));
        Assert.Contains("push", plan.Arguments);
        Assert.Contains("origin", plan.Arguments);
        Assert.Contains("HEAD:refs/heads/main", plan.Arguments);
    }

    [Fact]
    public void Push_ForceWithLease_Tags_Upstream_DryRun()
    {
        var plan = AdoGit.Push(FakeTool(), FakePat(), s => s
            .SetRef("feature")
            .SetForceWithLease()
            .SetTags()
            .SetUpstreamFlag()
            .SetDryRun());
        Assert.Contains("--force-with-lease", plan.Arguments);
        Assert.Contains("--tags", plan.Arguments);
        Assert.Contains("-u", plan.Arguments);
        Assert.Contains("--dry-run", plan.Arguments);
    }

    // ---- PullRebase ----

    [Fact]
    public void PullRebase_Adds_Autostash_By_Default()
    {
        var plan = AdoGit.PullRebase(FakeTool(), FakePat());
        Assert.Contains("pull", plan.Arguments);
        Assert.Contains("--rebase", plan.Arguments);
        Assert.Contains("--autostash", plan.Arguments);
    }

    [Fact]
    public void PullRebase_With_Branch()
    {
        var plan = AdoGit.PullRebase(FakeTool(), FakePat(), s => s.SetBranch("main"));
        Assert.Contains("origin", plan.Arguments);
        Assert.Contains("main", plan.Arguments);
    }

    // ---- Clone ----

    [Fact]
    public void Clone_Requires_Url()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AdoGit.Clone(FakeTool(), FakePat(), _ => { }).Arguments.ToList());
    }

    [Fact]
    public void Clone_With_Url_Branch_Depth_Target()
    {
        var plan = AdoGit.Clone(FakeTool(), FakePat(), s => s
            .SetUrl("https://dev.azure.com/org/proj/_git/repo")
            .SetTargetDirectory("./repo")
            .SetBranch("main")
            .SetDepth(1));
        Assert.Contains("clone", plan.Arguments);
        Assert.Contains("--depth=1", plan.Arguments);
        Assert.Equal("main", plan.Arguments[IndexOf(plan.Arguments, "--branch") + 1]);
        Assert.Contains("https://dev.azure.com/org/proj/_git/repo", plan.Arguments);
        Assert.Contains("./repo", plan.Arguments);
    }

    // ---- LsRemote ----

    [Fact]
    public void LsRemote_HeadsOnly_ExitCode()
    {
        var plan = AdoGit.LsRemote(FakeTool(), FakePat(), s => s
            .SetHeadsOnly().SetExitCode().SetRef("refs/heads/release-v2"));
        Assert.Contains("ls-remote", plan.Arguments);
        Assert.Contains("--heads", plan.Arguments);
        Assert.Contains("--exit-code", plan.Arguments);
        Assert.Contains("refs/heads/release-v2", plan.Arguments);
    }

    // ---- Extra config ----

    [Fact]
    public void AddConfig_Threads_Through_As_Dash_C_Pair()
    {
        var plan = AdoGit.Fetch(FakeTool(), FakePat(), s => s
            .AddConfig("user.email", "ci@example.com")
            .AddConfig("user.name", "CI Bot"));
        // First -c pair is the auto-injected auth header, then the two we added.
        var dashCIndices = Enumerable.Range(0, plan.Arguments.Count)
            .Where(i => plan.Arguments[i] == "-c").ToList();
        Assert.Equal(3, dashCIndices.Count);
        Assert.Contains("user.email=ci@example.com", plan.Arguments);
        Assert.Contains("user.name=CI Bot", plan.Arguments);
    }

    // ---- Raw ----

    [Fact]
    public void Raw_Allows_Arbitrary_Git_Verb()
    {
        var plan = AdoGit.Raw(FakeTool(), FakePat(), "rev-parse", "HEAD");
        Assert.Contains("rev-parse", plan.Arguments);
        Assert.Contains("HEAD", plan.Arguments);
        // Auth header still prepended
        Assert.Equal("-c", plan.Arguments[0]);
    }

    [Fact]
    public void Raw_Rejects_Empty_Args()
    {
        Assert.Throws<ArgumentException>(() => AdoGit.Raw(FakeTool(), FakePat()));
    }

    // ---- WorkingDirectory + env ----

    [Fact]
    public void WorkingDirectory_Propagates()
    {
        var plan = AdoGit.Fetch(FakeTool(), FakePat(), s => s.SetWorkingDirectory("/repo"));
        Assert.Equal("/repo", plan.WorkingDirectory);
    }

    [Fact]
    public void Executable_Matches_Tool_Path()
    {
        // AbsolutePath normalization differs by OS (Windows resolves "/fake/git" to
        // "C:\fake\git"); we assert the basename rather than the full string.
        var plan = AdoGit.Fetch(FakeTool(), FakePat());
        Assert.EndsWith("git", plan.Executable.TrimEnd(System.IO.Path.DirectorySeparatorChar));
    }
}
