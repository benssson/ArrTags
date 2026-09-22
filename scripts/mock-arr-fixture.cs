#:property RestorePackagesWithLockFile=false
// ArrTags test-support fixture: a minimal, reproducible mock Sonarr/Radarr
// /api/v3 HTTP server used by the Phase 7 live verification tasks.
//
// It is a .NET 10 file-based app (run with `dotnet run scripts/mock-arr-fixture.cs`)
// and is NOT part of the plugin build or solution; it adds no plugin dependency.
//
// It serves the exact read endpoints the ArrTags provider clients call:
//   Radarr: GET /api/v3/system/status, GET /api/v3/movie, GET /api/v3/moviefile?movieId=
//   Sonarr: GET /api/v3/system/status, GET /api/v3/series,
//           GET /api/v3/episode?seriesId=&includeEpisodeFile=true,
//           GET /api/v3/episodeFile?seriesId=
//
// Response bodies are read from a fixtures directory on every request, so
// editing a fixture file changes the served metadata without restarting the
// fixture. This is what lets the verification exercise a changed provider
// observation.
//
// Usage:
//   dotnet run scripts/mock-arr-fixture.cs -- \
//       --fixtures <dir> [--radarr-port 7878] [--sonarr-port 8989]
//
// Environment:
//   ARRTAGS_MOCK_RADARR_KEY  required X-Api-Key for the Radarr listener (optional)
//   ARRTAGS_MOCK_SONARR_KEY  required X-Api-Key for the Sonarr listener (optional)
//
// Fixture files (relative to --fixtures):
//   radarr/system-status.json  radarr/movies.json
//   radarr/moviefile.json      radarr/moviefile-<movieId>.json (preferred when present)
//   sonarr/system-status.json  sonarr/series.json
//   sonarr/episode.json        sonarr/episode-<seriesId>.json (preferred when present)
//   sonarr/episodefile.json    sonarr/episodefile-<seriesId>.json (preferred when present)
//
// Outage simulation (the server stays up and logs the request):
//   create radarr/failure  -> every Radarr endpoint answers 503
//   create sonarr/failure  -> every Sonarr endpoint answers 503
//
// The API key value is never logged; only whether the header was present and
// valid.

using System.Net;
using System.Text;

var fixturesDir = Environment.GetEnvironmentVariable("ARRTAGS_MOCK_FIXTURES") ?? "fixtures";
var radarrPort = 7878;
var sonarrPort = 8989;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--fixtures" when i + 1 < args.Length:
            fixturesDir = args[++i];
            break;
        case "--radarr-port" when i + 1 < args.Length:
            radarrPort = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--sonarr-port" when i + 1 < args.Length:
            sonarrPort = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
    }
}

fixturesDir = Path.GetFullPath(fixturesDir);
var radarrKey = Environment.GetEnvironmentVariable("ARRTAGS_MOCK_RADARR_KEY");
var sonarrKey = Environment.GetEnvironmentVariable("ARRTAGS_MOCK_SONARR_KEY");

var logGate = new object();

void Log(string line)
{
    lock (logGate)
    {
        Console.WriteLine(line);
        Console.Out.Flush();
    }
}

string? Resolve(string provider, string path, string query)
{
    string? suffix = null;
    switch ((provider, path))
    {
        case ("radarr", "/api/v3/system/status"):
            suffix = "radarr/system-status.json";
            break;
        case ("radarr", "/api/v3/movie"):
            suffix = "radarr/movies.json";
            break;
        case ("radarr", "/api/v3/moviefile"):
            var movieId = QueryValue(query, "movieId");
            var perMovie = movieId is null ? null : $"radarr/moviefile-{movieId}.json";
            suffix = perMovie is not null && File.Exists(Path.Combine(fixturesDir, perMovie))
                ? perMovie
                : "radarr/moviefile.json";
            break;
        case ("sonarr", "/api/v3/system/status"):
            suffix = "sonarr/system-status.json";
            break;
        case ("sonarr", "/api/v3/series"):
            suffix = "sonarr/series.json";
            break;
        case ("sonarr", "/api/v3/episode"):
            var seriesId = QueryValue(query, "seriesId");
            var perSeries = seriesId is null ? null : $"sonarr/episode-{seriesId}.json";
            suffix = perSeries is not null && File.Exists(Path.Combine(fixturesDir, perSeries))
                ? perSeries
                : "sonarr/episode.json";
            break;
        case ("sonarr", "/api/v3/episodeFile"):
            var fileSeriesId = QueryValue(query, "seriesId");
            var perFileSeries = fileSeriesId is null ? null : $"sonarr/episodefile-{fileSeriesId}.json";
            suffix = perFileSeries is not null && File.Exists(Path.Combine(fixturesDir, perFileSeries))
                ? perFileSeries
                : "sonarr/episodefile.json";
            break;
        default:
            return null;
    }

    return Path.Combine(fixturesDir, suffix);
}

static string? QueryValue(string query, string key)
{
    if (string.IsNullOrEmpty(query))
    {
        return null;
    }

    foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var separator = pair.IndexOf('=', StringComparison.Ordinal);
        var name = separator < 0 ? pair : pair[..separator];
        if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
        {
            return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
    }

    return null;
}

void Handle(string provider, string? configuredKey, HttpListenerContext context)
{
    var request = context.Request;
    var path = request.Url?.AbsolutePath ?? string.Empty;
    var query = request.Url?.Query ?? string.Empty;
    var header = request.Headers["X-Api-Key"];
    var headerPresent = !string.IsNullOrEmpty(header);
    var keyValid = configuredKey is null || string.Equals(header, configuredKey, StringComparison.Ordinal);
    var keyState = configuredKey is null
        ? (headerPresent ? "present-unenforced" : "missing-unenforced")
        : (!headerPresent ? "missing" : (keyValid ? "valid" : "invalid"));

    string status;
    string? file = null;
    int statusCode;
    byte[] body;

    var failureMarker = Path.Combine(fixturesDir, provider, "failure");
    if (configuredKey is not null && !keyValid)
    {
        statusCode = 401;
        status = "401";
        body = Encoding.UTF8.GetBytes("{\"error\":\"unauthorized\"}");
    }
    else if (File.Exists(failureMarker))
    {
        statusCode = 503;
        status = "503-simulated";
        body = Encoding.UTF8.GetBytes("{\"error\":\"unavailable\"}");
    }
    else
    {
        file = Resolve(provider, path, query);
        if (file is null || !File.Exists(file))
        {
            statusCode = 404;
            status = "404";
            body = Encoding.UTF8.GetBytes("{\"error\":\"not found\"}");
        }
        else
        {
            statusCode = 200;
            status = "200";
            try
            {
                body = File.ReadAllBytes(file);
            }
            catch (IOException)
            {
                statusCode = 500;
                status = "500-read";
                body = Encoding.UTF8.GetBytes("{\"error\":\"read\"}");
            }
        }
    }

    try
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
        context.Response.OutputStream.Close();
    }
    catch (Exception)
    {
        // The client may have gone away; nothing to do.
    }

    var served = file is null ? "-" : Path.GetRelativePath(fixturesDir, file);
    Log($"{DateTimeOffset.UtcNow:O} provider={provider} {request.HttpMethod} {path}{query} apiKey={keyState} status={status} file={served}");
}

async Task ServeAsync(string provider, int port, string? configuredKey)
{
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();
    Log($"{DateTimeOffset.UtcNow:O} listening provider={provider} port={port} fixtures={fixturesDir} apiKeyRequired={configuredKey is not null}");

    while (true)
    {
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            break;
        }

        _ = Task.Run(() => Handle(provider, configuredKey, context));
    }
}

Log($"{DateTimeOffset.UtcNow:O} mock-arr-fixture starting radarr={radarrPort} sonarr={sonarrPort}");

var radarrTask = ServeAsync("radarr", radarrPort, radarrKey);
var sonarrTask = ServeAsync("sonarr", sonarrPort, sonarrKey);
await Task.WhenAll(radarrTask, sonarrTask);
