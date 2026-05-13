using System;
using System.Collections.Generic;

namespace Tamp.AdoGit;

/// <summary>
/// Base for every AdoGit verb's settings. Captures the common knobs (working dir,
/// env, extra git config pairs) and emits the <c>git -c http.extraHeader=...</c>
/// prelude in <see cref="ToCommandPlan"/>.
/// </summary>
public abstract class AdoGitSettingsBase
{
    /// <summary>Working directory for the git command. Defaults to the tool's working directory if set, else the cwd of the runner.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>
    /// Extra <c>git -c &lt;key&gt;=&lt;value&gt;</c> pairs to set in addition to the auto-injected
    /// <c>http.extraHeader</c>. Useful for advanced cases (e.g. <c>core.askPass=true</c>).
    /// </summary>
    public List<(string Key, string Value)> ExtraConfig { get; } = new();

    /// <summary>Subclasses produce the verb + verb-specific arguments. The <c>git</c>-itself
    /// prefix (with the <c>-c http.extraHeader=...</c> prelude) is composed by the base.</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    internal CommandPlan ToCommandPlan(Tool tool, Secret pat)
    {
        var args = new List<string>();
        // Auth header — first so it applies to every subsequent verb that talks to the remote.
        args.Add("-c"); args.Add("http.extraHeader=" + AdoGit.BuildAuthHeader(pat));
        foreach (var (k, v) in ExtraConfig)
        {
            args.Add("-c"); args.Add($"{k}={v}");
        }
        args.AddRange(BuildVerbArguments());

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = new[] { pat },
        };
    }
}

/// <summary>Fluent setters for common knobs.</summary>
public static class AdoGitSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : AdoGitSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : AdoGitSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T AddConfig<T>(this T s, string key, string value) where T : AdoGitSettingsBase { s.ExtraConfig.Add((key, value)); return s; }
}

/// <summary>Settings for <c>git fetch</c>.</summary>
public sealed class AdoGitFetchSettings : AdoGitSettingsBase
{
    /// <summary>Remote name (default <c>origin</c>). Maps to the positional arg after <c>fetch</c>.</summary>
    public string? Remote { get; set; } = "origin";
    /// <summary>Optional refspec (e.g. <c>main</c>, <c>refs/heads/feature-x</c>).</summary>
    public string? Refspec { get; set; }
    /// <summary>Fetch tags (<c>--tags</c>).</summary>
    public bool Tags { get; set; }
    /// <summary>Prune deleted remote refs (<c>--prune</c>).</summary>
    public bool Prune { get; set; }
    /// <summary>Shallow fetch depth (<c>--depth=N</c>).</summary>
    public int? Depth { get; set; }

    public AdoGitFetchSettings SetRemote(string? remote) { Remote = remote; return this; }
    public AdoGitFetchSettings SetRefspec(string? refspec) { Refspec = refspec; return this; }
    public AdoGitFetchSettings SetTags(bool v = true) { Tags = v; return this; }
    public AdoGitFetchSettings SetPrune(bool v = true) { Prune = v; return this; }
    public AdoGitFetchSettings SetDepth(int? depth) { Depth = depth; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "fetch";
        if (Tags) yield return "--tags";
        if (Prune) yield return "--prune";
        if (Depth is { } d) yield return $"--depth={d}";
        if (!string.IsNullOrEmpty(Remote)) yield return Remote!;
        if (!string.IsNullOrEmpty(Refspec)) yield return Refspec!;
    }
}

/// <summary>Settings for <c>git push</c>.</summary>
public sealed class AdoGitPushSettings : AdoGitSettingsBase
{
    /// <summary>Remote (default <c>origin</c>).</summary>
    public string? Remote { get; set; } = "origin";
    /// <summary>Refspec (e.g. <c>HEAD:refs/heads/main</c>, <c>my-branch</c>). Required.</summary>
    public string? Ref { get; set; }
    /// <summary>Push tags (<c>--tags</c>).</summary>
    public bool Tags { get; set; }
    /// <summary>Force-push (<c>--force-with-lease</c>). Default uses <c>--force-with-lease</c>, not raw <c>--force</c> — the latter is destructive and easy to misuse.</summary>
    public bool ForceWithLease { get; set; }
    /// <summary>Set upstream (<c>-u</c>) on first push.</summary>
    public bool SetUpstream { get; set; }
    /// <summary>Dry run (<c>--dry-run</c>).</summary>
    public bool DryRun { get; set; }

    public AdoGitPushSettings SetRemote(string? remote) { Remote = remote; return this; }
    public AdoGitPushSettings SetRef(string refspec) { Ref = refspec; return this; }
    public AdoGitPushSettings SetTags(bool v = true) { Tags = v; return this; }
    public AdoGitPushSettings SetForceWithLease(bool v = true) { ForceWithLease = v; return this; }
    public AdoGitPushSettings SetUpstreamFlag(bool v = true) { SetUpstream = v; return this; }
    public AdoGitPushSettings SetDryRun(bool v = true) { DryRun = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(Ref)) throw new InvalidOperationException("Ref is required for push (set via SetRef).");
        yield return "push";
        if (Tags) yield return "--tags";
        if (ForceWithLease) yield return "--force-with-lease";
        if (SetUpstream) yield return "-u";
        if (DryRun) yield return "--dry-run";
        yield return Remote ?? "origin";
        yield return Ref!;
    }
}

/// <summary>Settings for <c>git pull --rebase</c>.</summary>
public sealed class AdoGitPullRebaseSettings : AdoGitSettingsBase
{
    /// <summary>Remote (default <c>origin</c>).</summary>
    public string? Remote { get; set; } = "origin";
    /// <summary>Optional branch (e.g. <c>main</c>).</summary>
    public string? Branch { get; set; }
    /// <summary>Auto-stash uncommitted changes (<c>--autostash</c>). Default true — saves a common foot-stub.</summary>
    public bool Autostash { get; set; } = true;

    public AdoGitPullRebaseSettings SetRemote(string? remote) { Remote = remote; return this; }
    public AdoGitPullRebaseSettings SetBranch(string? branch) { Branch = branch; return this; }
    public AdoGitPullRebaseSettings SetAutostash(bool v = true) { Autostash = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "pull";
        yield return "--rebase";
        if (Autostash) yield return "--autostash";
        if (!string.IsNullOrEmpty(Remote)) yield return Remote!;
        if (!string.IsNullOrEmpty(Branch)) yield return Branch!;
    }
}

/// <summary>Settings for <c>git clone &lt;url&gt; [target]</c>.</summary>
public sealed class AdoGitCloneSettings : AdoGitSettingsBase
{
    /// <summary>Repository URL. Required.</summary>
    public string? Url { get; set; }
    /// <summary>Target directory. Optional.</summary>
    public string? TargetDirectory { get; set; }
    /// <summary>Shallow clone depth (<c>--depth=N</c>).</summary>
    public int? Depth { get; set; }
    /// <summary>Specific branch to check out (<c>--branch</c>).</summary>
    public string? Branch { get; set; }

    public AdoGitCloneSettings SetUrl(string url) { Url = url; return this; }
    public AdoGitCloneSettings SetTargetDirectory(string? target) { TargetDirectory = target; return this; }
    public AdoGitCloneSettings SetDepth(int? depth) { Depth = depth; return this; }
    public AdoGitCloneSettings SetBranch(string? branch) { Branch = branch; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(Url)) throw new InvalidOperationException("Url is required for clone (set via SetUrl).");
        yield return "clone";
        if (Depth is { } d) yield return $"--depth={d}";
        if (!string.IsNullOrEmpty(Branch)) { yield return "--branch"; yield return Branch!; }
        yield return Url!;
        if (!string.IsNullOrEmpty(TargetDirectory)) yield return TargetDirectory!;
    }
}

/// <summary>Settings for <c>git ls-remote</c>.</summary>
public sealed class AdoGitLsRemoteSettings : AdoGitSettingsBase
{
    /// <summary>Remote (default <c>origin</c>).</summary>
    public string? Remote { get; set; } = "origin";
    /// <summary>Optional ref to filter (e.g. <c>refs/heads/main</c>).</summary>
    public string? Ref { get; set; }
    /// <summary>Show only heads (<c>--heads</c>).</summary>
    public bool HeadsOnly { get; set; }
    /// <summary>Show only tags (<c>--tags</c>).</summary>
    public bool TagsOnly { get; set; }
    /// <summary>Exit non-zero when no refs match (<c>--exit-code</c>). Useful for "does this branch exist" gates.</summary>
    public bool ExitCode { get; set; }

    public AdoGitLsRemoteSettings SetRemote(string? remote) { Remote = remote; return this; }
    public AdoGitLsRemoteSettings SetRef(string? refspec) { Ref = refspec; return this; }
    public AdoGitLsRemoteSettings SetHeadsOnly(bool v = true) { HeadsOnly = v; return this; }
    public AdoGitLsRemoteSettings SetTagsOnly(bool v = true) { TagsOnly = v; return this; }
    public AdoGitLsRemoteSettings SetExitCode(bool v = true) { ExitCode = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "ls-remote";
        if (HeadsOnly) yield return "--heads";
        if (TagsOnly) yield return "--tags";
        if (ExitCode) yield return "--exit-code";
        if (!string.IsNullOrEmpty(Remote)) yield return Remote!;
        if (!string.IsNullOrEmpty(Ref)) yield return Ref!;
    }
}

/// <summary>Raw escape hatch — passes the supplied arguments through after the auth header.</summary>
public sealed class AdoGitRawSettings : AdoGitSettingsBase
{
    private readonly List<string> _args = new();
    public void AddArgs(IEnumerable<string> args) => _args.AddRange(args);
    protected override IEnumerable<string> BuildVerbArguments() => _args;
}
