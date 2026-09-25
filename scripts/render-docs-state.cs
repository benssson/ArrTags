#:property RestorePackagesWithLockFile=false
// ArrTags documentation-status renderer.
//
// It is a .NET 10 file-based app (run with
// `dotnet run scripts/render-docs-state.cs [--check]`) and is NOT part of the
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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var root = Directory.GetCurrentDirectory();
var check = Environment.GetCommandLineArgs().Contains("--check");
const string StatusBegin = "<!-- BEGIN GENERATED: status -->";
const string StatusEnd = "<!-- END GENERATED: status -->";
const string MilestoneBegin = "<!-- BEGIN GENERATED: milestone-status -->";
const string MilestoneEnd = "<!-- END GENERATED: milestone-status -->";

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
status.Add($"**Open limitations:** {open.Count} ({string.Join(", ", open.Select(o => $"`{o}`"))}); see `docs/limitations.md`.");

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

var targets = new (string Path, string Begin, string End, string Body, string Label)[]
{
    (Path.Combine(root, "docs", "status.md"), StatusBegin, StatusEnd, string.Join("\n", status), "status"),
    (Path.Combine(root, "PLANS.md"), MilestoneBegin, MilestoneEnd, string.Join("\n", milestone), "milestone-status"),
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
return 0;
