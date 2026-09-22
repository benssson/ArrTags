#:property RestorePackagesWithLockFile=false
// ArrTags Jellyfin plugin-repository manifest generator.
//
// It is a .NET 10 file-based app (run with
// `dotnet run scripts/write-manifest.cs -- ...`) and is NOT part of the plugin
// build or solution; it adds no plugin dependency.
//
// It reads the plugin metadata from `build.yaml` and upserts the matching
// version entry into a Jellyfin plugin-repository `manifest.json`
// (https://jellyfin.org/docs/general/server/plugins/#advanced-managing-the-plugin-repository),
// preserving any other plugin entries and version entries already present.
//
// Usage:
//   dotnet run scripts/write-manifest.cs -- \
//       --build-yaml build.yaml \
//       --manifest manifest.json \
//       --checksum <md5hex> \
//       --source-url <url> \
//       --timestamp <iso8601> \
//       [--output manifest.json]
//
//   dotnet run scripts/write-manifest.cs -- --print-changelog [--build-yaml build.yaml]
//
// `--print-changelog` prints only the `changelog` value from `build.yaml` to
// stdout and exits 0; it is used by `scripts/publish-release.sh` for the GitHub
// release notes.
//
// Exit codes: 0 success, 2 usage/input error.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

var buildYamlPath = "build.yaml";
var manifestPath = "manifest.json";
var outputPath = default(string);
var checksum = default(string);
var sourceUrl = default(string);
var timestamp = default(string);
var printChangelog = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--build-yaml" when i + 1 < args.Length:
            buildYamlPath = args[++i];
            break;
        case "--manifest" when i + 1 < args.Length:
            manifestPath = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--checksum" when i + 1 < args.Length:
            checksum = args[++i];
            break;
        case "--source-url" when i + 1 < args.Length:
            sourceUrl = args[++i];
            break;
        case "--timestamp" when i + 1 < args.Length:
            timestamp = args[++i];
            break;
        case "--print-changelog":
            printChangelog = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            return 2;
    }
}

outputPath ??= manifestPath;

if (!File.Exists(buildYamlPath))
{
    Console.Error.WriteLine($"build.yaml not found: {buildYamlPath}");
    return 2;
}

var metadata = ParseBuildYaml(buildYamlPath);

if (printChangelog)
{
    if (!metadata.TryGetValue("changelog", out var changelogValue) || changelogValue.Length == 0)
    {
        Console.Error.WriteLine($"build.yaml has no changelog value: {buildYamlPath}");
        return 2;
    }

    Console.WriteLine(changelogValue);
    return 0;
}

if (string.IsNullOrEmpty(checksum) || string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(timestamp))
{
    Console.Error.WriteLine(
        "Usage: dotnet run scripts/write-manifest.cs -- --build-yaml <file> --manifest <file> " +
        "--checksum <md5hex> --source-url <url> --timestamp <iso8601> [--output <file>]");
    return 2;
}

string? Required(string key) =>
    metadata.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

var name = Required("name");
var guid = Required("guid");
var version = Required("version");
var targetAbi = Required("targetAbi");
var overview = Required("overview");
var description = Required("description");
var category = Required("category");
var owner = Required("owner");
var changelog = Required("changelog");

if (name is null || guid is null || version is null || targetAbi is null || overview is null
    || description is null || category is null || owner is null || changelog is null)
{
    Console.Error.WriteLine(
        $"build.yaml is missing one or more required keys: name, guid, version, targetAbi, overview, " +
        $"description, category, owner, changelog ({buildYamlPath})");
    return 2;
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

JsonArray plugins;
if (File.Exists(manifestPath))
{
    var existingText = File.ReadAllText(manifestPath);
    if (string.IsNullOrWhiteSpace(existingText))
    {
        plugins = new JsonArray();
    }
    else
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(existingText);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Existing manifest is not valid JSON: {manifestPath}: {ex.Message}");
            return 2;
        }

        if (root is not JsonArray existingPlugins)
        {
            Console.Error.WriteLine($"Existing manifest is not a JSON array: {manifestPath}");
            return 2;
        }

        plugins = existingPlugins;
    }
}
else
{
    plugins = new JsonArray();
}

// Upsert keyed by the current guid.
JsonObject? plugin = null;
foreach (var node in plugins)
{
    if (node is JsonObject candidate
        && candidate.TryGetPropertyValue("guid", out var candidateGuid)
        && candidateGuid?.GetValue<string>() == guid)
    {
        plugin = candidate;
        break;
    }
}

var existingVersions = plugin?["versions"] as JsonArray ?? new JsonArray();

// Replace the entry with the same version, otherwise add it.
var versionEntry = new JsonObject
{
    ["checksum"] = checksum,
    ["changelog"] = changelog,
    ["targetAbi"] = targetAbi,
    ["sourceUrl"] = sourceUrl,
    ["timestamp"] = timestamp,
    ["version"] = version,
};

var versionList = existingVersions
    .OfType<JsonObject>()
    .Select(entry => (JsonObject)entry.DeepClone())
    .ToList();

var replaced = false;
for (var i = 0; i < versionList.Count; i++)
{
    if (versionList[i]["version"]?.GetValue<string>() == version)
    {
        versionList[i] = versionEntry;
        replaced = true;
        break;
    }
}

if (!replaced)
{
    versionList.Add(versionEntry);
}

// Keep versions sorted newest-first using a numeric dotted-version comparison.
versionList.Sort((left, right) => CompareVersions(
    right["version"]?.GetValue<string>() ?? string.Empty,
    left["version"]?.GetValue<string>() ?? string.Empty));

var versions = new JsonArray();
foreach (var entry in versionList)
{
    versions.Add((JsonNode)entry);
}

if (plugin is null)
{
    plugin = new JsonObject
    {
        ["category"] = category,
        ["guid"] = guid,
        ["name"] = name,
        ["description"] = description,
        ["owner"] = owner,
        ["overview"] = overview,
        ["versions"] = versions,
    };
    plugins.Add((JsonNode)plugin);
}
else
{
    plugin["category"] = category;
    plugin["guid"] = guid;
    plugin["name"] = name;
    plugin["description"] = description;
    plugin["owner"] = owner;
    plugin["overview"] = overview;
    plugin["versions"] = versions;
}

var outputFullPath = Path.GetFullPath(outputPath);
var outputDirectory = Path.GetDirectoryName(outputFullPath);
if (!string.IsNullOrEmpty(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

// Stable, deterministic JSON: 2-space indent, UTF-8 without BOM, one trailing
// newline. Identical inputs therefore produce byte-identical output.
var serialized = plugins.ToJsonString(jsonOptions);
File.WriteAllText(outputFullPath, serialized + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine($"Wrote Jellyfin repository manifest: {outputFullPath} ({plugins.Count} plugin(s))");

return 0;

// Parses the small `build.yaml` shape this repository uses: top-level
// `key: "value"` scalars and folded `key: >` block scalars (description,
// changelog). Unknown keys and indented list items (for example `artifacts:`)
// are ignored.
static Dictionary<string, string> ParseBuildYaml(string path)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    var blockKey = default(string);
    var blockLines = new List<string>();

    void FlushBlock()
    {
        if (blockKey is not null)
        {
            values[blockKey] = FoldBlock(blockLines);
            blockKey = null;
            blockLines.Clear();
        }
    }

    foreach (var raw in File.ReadAllLines(path))
    {
        if (blockKey is not null)
        {
            if (raw.Length == 0)
            {
                blockLines.Add(string.Empty);
                continue;
            }

            if (raw[0] is ' ' or '\t')
            {
                blockLines.Add(raw.Trim());
                continue;
            }

            FlushBlock();
        }

        var trimmed = raw.TrimStart();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            continue;
        }

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            continue;
        }

        var key = trimmed[..colon].Trim();
        var rest = trimmed[(colon + 1)..].Trim();

        if (rest is ">" or ">-" or "|" or "|-")
        {
            blockKey = key;
            blockLines.Clear();
            continue;
        }

        if (rest.Length == 0)
        {
            // For example `artifacts:`; its indented items are not scalars we use.
            continue;
        }

        values[key] = Unquote(rest);
    }

    FlushBlock();
    return values;
}

// Folds a YAML `>` block scalar: single line breaks become spaces, blank lines
// become newlines.
static string FoldBlock(List<string> lines)
{
    var builder = new StringBuilder();
    var pendingBlankLines = 0;
    var first = true;

    foreach (var line in lines)
    {
        if (line.Length == 0)
        {
            pendingBlankLines++;
            continue;
        }

        if (first)
        {
            builder.Append(line);
            first = false;
        }
        else if (pendingBlankLines > 0)
        {
            builder.Append('\n', pendingBlankLines);
            builder.Append(line);
        }
        else
        {
            builder.Append(' ');
            builder.Append(line);
        }

        pendingBlankLines = 0;
    }

    return builder.ToString();
}

static string Unquote(string value)
{
    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
    {
        return value[1..^1]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
    {
        return value[1..^1];
    }

    return value;
}

static int[] ParseVersion(string version)
{
    var parts = version.Split('.');
    var numbers = new int[parts.Length];
    for (var i = 0; i < parts.Length; i++)
    {
        numbers[i] = int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    return numbers;
}

static int CompareVersions(string left, string right)
{
    var leftParts = ParseVersion(left);
    var rightParts = ParseVersion(right);
    var length = Math.Max(leftParts.Length, rightParts.Length);

    for (var i = 0; i < length; i++)
    {
        var leftNumber = i < leftParts.Length ? leftParts[i] : 0;
        var rightNumber = i < rightParts.Length ? rightParts[i] : 0;
        if (leftNumber != rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }
    }

    return 0;
}
