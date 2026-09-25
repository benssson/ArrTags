#:property RestorePackagesWithLockFile=false
// ArrTags documentation-status renderer.
//
// It is a .NET 10 file-based app (run `--check` as
// `dotnet run scripts/render-docs-state.cs -- --check`) and is NOT part of the
// plugin build or solution; it adds no plugin dependency.
//
// It reads the canonical machine state `docs/plan/state.json` and renders the
// generated blocks in `docs/status.md` and `PLANS.md`. The generated blocks are
// delimited by HTML-comment markers; everything outside the markers is
// hand-written.
//
// Usage:
//   dotnet run scripts/render-docs-state.cs            # render in place
//   dotnet run scripts/render-docs-state.cs -- --check # verify, exit 2 if stale
//
// Exit codes: 0 success, 2 stale or malformed input, 3 marker missing.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var root = Directory.GetCurrentDirectory();
var check = Environment.GetCommandLineArgs().Contains("--check");
const string StatusBegin = "<!-- BEGIN GENERATED: status -->";
const string StatusEnd = "<!-- END GENERATED: status -->";
const string MilestoneBegin = "<!-- BEGIN GENERATED: milestone-status -->";
const string MilestoneEnd = "<!-- END GENERATED: milestone-status -->";
const string ScopeBegin = "<!-- BEGIN GENERATED: active-scope -->";
const string ScopeEnd = "<!-- END GENERATED: active-scope -->";

var statePath = Path.Combine(root, "docs", "plan", "state.json");
if (!File.Exists(statePath)) { Console.Error.WriteLine($"missing {statePath}"); return 2; }
JsonNode state;
try { state = JsonNode.Parse(File.ReadAllText(statePath))!; }
catch (JsonException ex) { Console.Error.WriteLine($"invalid state.json: {ex.Message}"); return 2; }

string RenderBlock(string text, string begin, string end, string body, string label)
{
    var b = text.IndexOf(begin, StringComparison.Ordinal);
    var e = text.IndexOf(end, StringComparison.Ordinal);
    if (b < 0 || e < 0 || e < b)
        throw new InvalidOperationException($"markers for {label} not found");
    return text[..b] + begin + "\n" + body + "\n" + text[e..];
}

var cr = state["current_release"]!;
var version = (string)cr["version"]!;
var published = (bool)cr["published"]!;
var phases = state["phases"]!.AsArray();
var activePhases = phases.Where(p => (string?)p!["status"] != "COMPLETE")
                         .OrderBy(p => (int)p!["id"]!).ToList();
var completeIds = phases.Where(p => (string?)p!["status"] == "COMPLETE")
                        .Select(p => (int)p!["id"]!).OrderBy(x => x).ToList();
var open = state["limitations"]!["open"]!.AsArray().Select(x => (string)x!).ToList();

var status = new List<string>
{
    $"**Current release:** `{version}` (tag `{cr["tag"]}`) — {cr["status"]}; " +
        (published
            ? "published through the Jellyfin plugin catalog."
            : "the GitHub release, asset upload, and manifest push are not yet performed."),
    "",
};
var activeNote = (string?)state["active_scope_note"] ?? "";
var scope = state["active_scope"];
if (scope is null || scope.GetValueKind() == JsonValueKind.Null)
    status.Add($"**Active scope:** none. {activeNote}");
else
    status.Add($"**Active scope:** `{scope}`. {activeNote}");
status.Add("");
var art = cr["artifact"]!;
status.Add($"**Artifact:** `{art["path"]}` — {art["bytes"]} bytes, {art["entries"]} entries;");
status.Add($"SHA-256 `{art["sha256"]}`; MD5 `{art["md5"]}`.");
status.Add("");
var suite = cr["suite"]!;
string S(string k) { var s = suite[k]!; return $"{s["failed"]} / {s["passed"]} / {s["skipped"]} / {s["total"]}"; }
status.Add($"**Test suite (`{version}`)** (failed / passed / skipped / total): default");
status.Add($"{S("default")}; host-guarded {S("host_guarded")}; forced-native {S("forced_native")}.");
status.Add("");
status.Add($"**Verification:** live pinned-host matrix `{cr["live_verification"]}`;");
status.Add($"release security review `{cr["security_review"]}`; release review `{cr["release_review"]}`.");
status.Add("");
status.Add(activePhases.Count > 0
    ? $"**Active phases:** {string.Join(", ", activePhases.Select(p => $"Phase {p!["id"]}"))}; all other phases are complete."
    : $"**Phases:** all {phases.Count} complete ({completeIds.Min()}-{completeIds.Max()}); Gates {completeIds.Min()}-{completeIds.Max()} met. Completed plans are archived in `docs/plan/archive/`.");
status.Add("");
status.Add($"**Open limitations:** {open.Count} ({string.Join(", ", open.Select(o => $"`{o}`"))}); see `docs/limitations/00-index.md`.");

var milestone = new List<string>
{
    "| # | Milestone | Status | Exit gate |",
    "| --- | --- | --- | --- |",
};
if (activePhases.Count == 0)
    milestone.Add("| — | No active milestone | — | No release scope is currently accepted for planning. |");
else
    foreach (var p in activePhases)
        milestone.Add($"| {p!["id"]} | {p!["name"]} | {p!["status"]} | {p!["gate"]} |");

var activeScopeBody = scope is null || scope.GetValueKind() == JsonValueKind.Null
    ? "**Active plan:** none."
    : $"**Active plan:** `{scope}`.";

var targets = new (string Path, string Begin, string End, string Body, string Label)[]
{
    (Path.Combine(root, "docs", "status.md"), StatusBegin, StatusEnd, string.Join("\n", status), "status"),
    (Path.Combine(root, "PLANS.md"), MilestoneBegin, MilestoneEnd, string.Join("\n", milestone), "milestone-status"),
    (Path.Combine(root, "PLANS.md"), ScopeBegin, ScopeEnd, activeScopeBody, "active-scope"),
};

var stale = new List<string>();
foreach (var t in targets)
{
    if (!File.Exists(t.Path)) { Console.Error.WriteLine($"missing {t.Path}"); return 3; }
    var original = File.ReadAllText(t.Path);
    string rendered;
    try { rendered = RenderBlock(original, t.Begin, t.End, t.Body, t.Label); }
    catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 3; }
    if (rendered == original)
    {
        Console.WriteLine($"{Path.GetFileName(t.Path)}: up to date");
        continue;
    }
    if (check) { stale.Add(Path.GetFileName(t.Path)); continue; }
    File.WriteAllText(t.Path, rendered);
    Console.WriteLine($"{Path.GetFileName(t.Path)}: rendered");
}
if (stale.Count > 0)
{
    Console.Error.WriteLine($"stale generated blocks: {string.Join(", ", stale)}");
    return 2;
}

if (check)
{
    var scopeNode = state["active_scope"];
    var planningDir = Path.Combine(root, "docs", "planning");
    if (scopeNode is not null && scopeNode.GetValueKind() == JsonValueKind.String)
    {
        if (!File.Exists(Path.Combine(root, (string)scopeNode!)))
        {
            Console.Error.WriteLine($"active_scope points to a missing plan: {scopeNode}");
            return 2;
        }
    }
    else
    {
        var plans = Directory.Exists(planningDir) ? Directory.GetFiles(planningDir, "*.md") : Array.Empty<string>();
        if (plans.Length > 0)
        {
            Console.Error.WriteLine($"active_scope is null but docs/planning contains: {string.Join(", ", plans.Select(Path.GetFileName))}");
            return 2;
        }
    }

    var agreement = PlanAgreementErrors(state, File.ReadAllText(Path.Combine(root, "PLANS.md")));
    if (agreement.Count > 0)
    {
        foreach (var e in agreement) Console.Error.WriteLine($"state/plan mismatch: {e}");
        return 2;
    }
    Console.WriteLine("PLANS.md and state.json agree");
}
return 0;

// For every phase present in PLANS.md, its task checkboxes must match state.json,
// and every non-complete phase in state.json must be present in PLANS.md.
List<string> PlanAgreementErrors(JsonNode state, string planText)
{
    var errors = new List<string>();
    var phases = state["phases"]!.AsArray();
    var completedReleases = new HashSet<string>(state["releases"]!.AsArray()
        .Where(r => (string?)r!["status"] == "COMPLETE")
        .Select(r => (string)r!["version"]!));
    var present = new HashSet<int>();
    foreach (Match m in Regex.Matches(planText, @"^### (\d+)\.\s", RegexOptions.Multiline))
        present.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));

    foreach (var p in phases)
    {
        var id = (int)p!["id"]!;
        var status = (string?)p["status"] ?? "";
        if (!present.Contains(id))
        {
            if (status != "COMPLETE")
                errors.Add($"active phase {id} ({status}) has no '### {id}.' section in PLANS.md");
            continue;
        }
        var release = (string?)p["release"] ?? "";
        if (completedReleases.Contains(release))
            errors.Add($"phase {id} belongs to completed release {release} and must be archived out of PLANS.md");
        foreach (var t in p["tasks"]!.AsArray())
        {
            var tid = (string)t!["id"]!;
            var done = (bool)t["done"]!;
            var m = Regex.Match(planText, @"^- \[([ xX])\]\s+" + Regex.Escape(tid) + @"\b", RegexOptions.Multiline);
            if (!m.Success)
                errors.Add($"phase {id} has task {tid} in state.json but no checkbox in PLANS.md");
            else if ((m.Groups[1].Value is "x" or "X") != done)
                errors.Add($"task {tid}: PLANS.md checkbox does not match state.json done={done.ToString().ToLowerInvariant()}");
        }
    }

    foreach (Match m in Regex.Matches(planText, @"^- \[([ xX])\]\s+(\d+\.\d+)\b", RegexOptions.Multiline))
    {
        var tid = m.Groups[2].Value;
        var pid = int.Parse(tid.Split('.')[0], CultureInfo.InvariantCulture);
        if (!present.Contains(pid)) continue;
        var phase = phases.FirstOrDefault(x => (int)x!["id"]! == pid);
        var known = phase?["tasks"]!.AsArray().Any(t => (string?)t!["id"] == tid) ?? false;
        if (!known) errors.Add($"PLANS.md task {tid} is missing from state.json");
    }
    return errors;
}
