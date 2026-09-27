using System;
using System.Collections.Generic;
using System.Text;
using Tamp;

namespace Tamp.AdoGit;

/// <summary>
/// PAT-injected git wrapper for Azure DevOps.
/// </summary>
/// <remarks>
/// <para>
/// Every git network operation against ADO needs PAT-injected <c>http.extraHeader</c> because
/// Git Credential Manager (GCM) picks the wrong tenant identity on multi-tenant developer
/// machines. The fix is a single line of <c>-c http.extraHeader=AUTHORIZATION: Basic &lt;b64&gt;</c>
/// per command; this wrapper bakes that into every verb so build scripts and automation
/// stop repeating the boilerplate.
/// </para>
/// <para>
/// Resolve the underlying git tool via <c>[FromPath("git")]</c> and supply the PAT via
/// <c>[Secret]</c>:
/// </para>
/// <code>
/// [FromPath("git")] readonly Tool Git = null!;
/// [Secret("ADO PAT", EnvironmentVariable = "ADO_PAT")] readonly Secret AdoPat = null!;
///
/// Target Fetch => _ => _.Executes(() =&gt; AdoGit.Fetch(Git, AdoPat, s =&gt; s.SetRemote("origin")));
/// Target Push  => _ => _.Executes(() =&gt; AdoGit.Push(Git, AdoPat, s =&gt; s.SetRemote("origin").SetRef("HEAD:refs/heads/main")));
/// </code>
/// <para>
/// Both the PAT <b>and</b> its base64 credential — the <c>base64(":" + pat)</c> that actually
/// appears on the command line inside the auth header — are added to the
/// <see cref="CommandPlan.Secrets"/> list. The encoded form is a <em>different literal</em>, so it is
/// registered as a derived <see cref="Secret"/> (via <see cref="Secret.Derive"/>) rather than relying
/// on the raw PAT alone, which (redaction being a literal match) would not cover it. The runner's
/// redaction table then scrubs either form from any logged output.
/// </para>
/// </remarks>
public static class AdoGit
{
    /// <summary><c>git fetch</c> with PAT auth.</summary>
    public static CommandPlan Fetch(Tool tool, Secret pat, Action<AdoGitFetchSettings>? configure = null)
        => Build<AdoGitFetchSettings>(tool, pat, configure);

    /// <summary><c>git push</c> with PAT auth.</summary>
    public static CommandPlan Push(Tool tool, Secret pat, Action<AdoGitPushSettings> configure)
        => Build<AdoGitPushSettings>(tool, pat, configure);

    /// <summary><c>git pull --rebase</c> with PAT auth.</summary>
    public static CommandPlan PullRebase(Tool tool, Secret pat, Action<AdoGitPullRebaseSettings>? configure = null)
        => Build<AdoGitPullRebaseSettings>(tool, pat, configure);

    /// <summary><c>git clone</c> with PAT auth.</summary>
    public static CommandPlan Clone(Tool tool, Secret pat, Action<AdoGitCloneSettings> configure)
        => Build<AdoGitCloneSettings>(tool, pat, configure);

    /// <summary><c>git ls-remote</c> with PAT auth — useful for "does this branch exist on origin" checks.</summary>
    public static CommandPlan LsRemote(Tool tool, Secret pat, Action<AdoGitLsRemoteSettings>? configure = null)
        => Build<AdoGitLsRemoteSettings>(tool, pat, configure);

    /// <summary>
    /// Raw escape hatch. Constructs <c>git -c http.extraHeader=&lt;auth&gt; &lt;arguments...&gt;</c>.
    /// Use only when the typed verbs don't cover what you need; file a TAM ticket if you find
    /// yourself reaching for this frequently.
    /// </summary>
    public static CommandPlan Raw(Tool tool, Secret pat, params string[] arguments)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (pat is null) throw new ArgumentNullException(nameof(pat));
        if (arguments is null || arguments.Length == 0)
            throw new ArgumentException("Raw requires at least one git argument (verb).", nameof(arguments));
        var s = new AdoGitRawSettings();
        s.AddArgs(arguments);
        return s.ToCommandPlan(tool, pat);
    }

    // ---- Object-init overloads (TAM-161) ----
    // Parallel surface to the fluent verbs above. Both styles produce identical
    // CommandPlans; fluent stays canonical in docs and `tamp init` templates.
    //
    //     AdoGit.Push(Git, AdoPat, new() { Remote = "origin", Ref = "HEAD:refs/heads/main" });
    //
    // is equivalent to:
    //
    //     AdoGit.Push(Git, AdoPat, s => s.SetRemote("origin").SetRef("HEAD:refs/heads/main"));
    public static CommandPlan Fetch(Tool tool, Secret pat, AdoGitFetchSettings settings) => Plan(tool, pat, settings);
    public static CommandPlan Push(Tool tool, Secret pat, AdoGitPushSettings settings) => Plan(tool, pat, settings);
    public static CommandPlan PullRebase(Tool tool, Secret pat, AdoGitPullRebaseSettings settings) => Plan(tool, pat, settings);
    public static CommandPlan Clone(Tool tool, Secret pat, AdoGitCloneSettings settings) => Plan(tool, pat, settings);
    public static CommandPlan LsRemote(Tool tool, Secret pat, AdoGitLsRemoteSettings settings) => Plan(tool, pat, settings);

    private static CommandPlan Build<T>(Tool tool, Secret pat, Action<T>? configure)
        where T : AdoGitSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (pat is null) throw new ArgumentNullException(nameof(pat));
        var settings = new T();
        configure?.Invoke(settings);
        return settings.ToCommandPlan(tool, pat);
    }

    private static CommandPlan Plan<T>(Tool tool, Secret pat, T settings)
        where T : AdoGitSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (pat is null) throw new ArgumentNullException(nameof(pat));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool, pat);
    }

    /// <summary>
    /// Encode a PAT as the Basic-auth header value expected by ADO:
    /// <c>AUTHORIZATION: Basic &lt;base64(":pat")&gt;</c>. ADO accepts the username
    /// portion as empty; the PAT is passed as the password. Exposed for tests and
    /// for the rare adopter who wants to compose the header manually.
    /// </summary>
    internal static string BuildAuthHeader(Secret pat)
    {
        // Building the header needs the raw value. That's fine here: the transmitted credential
        // (BuildAuthToken) is registered as a DERIVED Secret in ToCommandPlan, so the encoded form
        // — not just the raw PAT — is covered by the redaction table.
#pragma warning disable TAMP004
        return "AUTHORIZATION: Basic " + BuildAuthToken(pat.Reveal());
#pragma warning restore TAMP004
    }

    /// <summary>
    /// The base64 credential transmitted in the auth header — <c>base64(":" + pat)</c>. This is the
    /// decodable secret material actually placed on the command line, and a <em>different literal</em>
    /// than the raw PAT — so it is registered as a derived <see cref="Secret"/>
    /// (<see cref="Secret.Derive"/>) when the command plan is built, so the runner's redaction table
    /// scrubs it. Redaction matches literally; registering only the raw PAT would leave this uncovered.
    /// </summary>
    internal static string BuildAuthToken(string token)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token));
}
