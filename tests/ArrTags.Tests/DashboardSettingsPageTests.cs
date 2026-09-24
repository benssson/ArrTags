using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using ArrTags.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 9 task 9.2 coverage for the dashboard settings page (ADR-016 clauses 1,
/// 2, 6, and 7 second bullet). These tests prove the <c>IHasWebPages</c>
/// contract, that the embedded resource logical name resolves, that the page
/// follows the in-tree Jellyfin page pattern, that every user-adjustable
/// configuration property is represented and no unknown property is referenced,
/// and that the page embeds no secret or credential literal. The host-guarded
/// facts confirm the pinned <c>DashboardController</c> serves the page and that
/// the page-resource endpoint is anonymous while the configuration list stays
/// elevation-gated; they are skipped when no pinned host directory is supplied.
/// </summary>
public class DashboardSettingsPageTests
{
    private const string DashboardControllerTypeName = "Jellyfin.Api.Controllers.DashboardController";

    [Fact]
    public void GetPagesReturnsTheSingleDashboardSettingsPage()
    {
        var pages = CreatePlugin().GetPages().ToList();

        var page = Assert.Single(pages);
        Assert.Equal(Plugin.SettingsPageName, page.Name);
        Assert.Equal("ArrTags", page.Name);
        Assert.Equal(Plugin.SettingsPageResourceName, page.EmbeddedResourcePath);
        Assert.Equal("ArrTags.Configuration.config.html", page.EmbeddedResourcePath);
        Assert.False(page.EnableInMainMenu);
        Assert.Null(page.DisplayName);
    }

    [Fact]
    public void EmbeddedSettingsPageResourceResolvesByLogicalName()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);

        Assert.NotNull(stream);
        var page = ReadStream(stream!);
        Assert.False(string.IsNullOrWhiteSpace(page));
        Assert.Contains("id=\"configPage\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageDeclaresTheInTreeDashboardConfigurationPageContract()
    {
        var page = ReadEmbeddedPage();

        Assert.Contains("data-role=\"page\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "class=\"page type-interior pluginConfigurationPage configPage\"",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-require=\"emby-input,emby-button,emby-checkbox,emby-select\"",
            page,
            StringComparison.Ordinal);
        Assert.Contains("addEventListener('pageshow'", page, StringComparison.Ordinal);
        Assert.Contains("ApiClient.getPluginConfiguration", page, StringComparison.Ordinal);
        Assert.Contains("ApiClient.updatePluginConfiguration", page, StringComparison.Ordinal);
        Assert.Contains("Dashboard.processPluginConfigurationUpdateResult", page, StringComparison.Ordinal);

        // Library display names are inserted as text, never as HTML, so a
        // library name cannot inject markup into the dashboard.
        Assert.DoesNotContain("innerHTML", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageFieldsCoverEveryUserAdjustableConfigurationProperty()
    {
        var page = ReadEmbeddedPage();
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (var type in ConfigurationModelTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0
                    || !property.CanWrite
                    || property.SetMethod?.IsPublic != true)
                {
                    continue;
                }

                checkedCount++;
                if (!Regex.IsMatch(page, "\\b" + Regex.Escape(property.Name) + "\\b"))
                {
                    missing.Add(type.Name + "." + property.Name);
                }
            }
        }

        Assert.True(checkedCount > 0, "The configuration model must expose user-adjustable properties.");
        Assert.Empty(missing);
    }

    [Fact]
    public void PageOnlyReferencesKnownConfigurationProperties()
    {
        var page = ReadEmbeddedPage();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in ConfigurationModelTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                known.Add(property.Name);
            }
        }

        var matches = Regex.Matches(page, @"config(?:\.[A-Za-z_][A-Za-z0-9_]*)+");
        Assert.NotEmpty(matches);

        var unknown = new List<string>();
        foreach (Match match in matches)
        {
            foreach (var segment in match.Value.Split('.').Skip(1))
            {
                if (!known.Contains(segment))
                {
                    unknown.Add(match.Value);
                    break;
                }
            }
        }

        Assert.Empty(unknown);
    }

    [Fact]
    public void PageExposesAndRoundTripsThePerSelectorAllowlist()
    {
        var page = ReadEmbeddedPage();

        // Every selector has its own allowlist input.
        foreach (var selectorName in new[]
        {
            "Quality",
            "Resolution",
            "DynamicRange",
            "Source",
            "VideoCodec",
            "Audio",
            "CustomBadge",
            "UpgradePending",
        })
        {
            Assert.Contains(
                "selector-" + selectorName + "-allowedValues",
                page,
                StringComparison.Ordinal);
        }

        // Populate reads the persisted allowlist and submit writes it back.
        Assert.Contains("configured.AllowedValues", page, StringComparison.Ordinal);
        Assert.Contains("AllowedValues: parseAllowedValues", page, StringComparison.Ordinal);
    }

    [Fact]
    public void PageSourceContainsNoSecretOrCredentialLiteral()
    {
        var page = ReadEmbeddedPage();

        // A static embedded resource can never contain a configured secret. The
        // sentinels make the check non-vacuous if a future change templated a
        // value into the page.
        var configuration = new PluginConfiguration
        {
            WebhookSecret = "sentinel-webhook-secret-8f1c2d",
            Sonarr = new ArrConnectionConfiguration { ApiKey = "sentinel-sonarr-key-4a9b7e" },
            Radarr = new ArrConnectionConfiguration { ApiKey = "sentinel-radarr-key-1d6f3a" },
        };

        Assert.DoesNotContain(configuration.WebhookSecret, page, StringComparison.Ordinal);
        Assert.DoesNotContain(configuration.Sonarr.ApiKey, page, StringComparison.Ordinal);
        Assert.DoesNotContain(configuration.Radarr.ApiKey, page, StringComparison.Ordinal);

        // The three secret inputs are password fields with no embedded default
        // value; the value is only ever populated at runtime from Jellyfin's
        // existing administrator-gated configuration API.
        foreach (var id in new[] { "sonarrApiKey", "radarrApiKey", "webhookSecret" })
        {
            var tag = Regex.Match(page, "<input[^>]*id=\"" + id + "\"[^>]*>");
            Assert.True(tag.Success, $"Expected a secret input with id '{id}'.");
            Assert.Contains("type=\"password\"", tag.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("value=", tag.Value, StringComparison.Ordinal);
        }
    }

    [JellyfinHostFact]
    public void PinnedDashboardControllerServesTheArrTagsSettingsPage()
    {
        var controllerType = LoadDashboardController();
        var controller = CreateController(controllerType);
        var method = controllerType.GetMethod(
            "GetDashboardConfigurationPage",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        // The pinned action matches the page name case-insensitively.
        var result = method!.Invoke(controller, new object?[] { "arrtags" });
        var file = Assert.IsType<FileStreamResult>(result);
        Assert.StartsWith("text/html", file.ContentType, StringComparison.OrdinalIgnoreCase);

        using var reader = new StreamReader(file.FileStream);
        Assert.Equal(ReadEmbeddedPage(), reader.ReadToEnd());

        var notFound = method.Invoke(controller, new object?[] { "not-an-arrtags-page" });
        Assert.IsType<NotFoundResult>(notFound);
    }

    [JellyfinHostFact]
    public void PinnedDashboardControllerPageResourceIsAnonymousAndTheListIsElevationGated()
    {
        var controllerType = LoadDashboardController();

        // ADR-016 clause 6: the static page-resource endpoint is accepted as an
        // anonymous, non-data surface.
        var pageMethod = controllerType.GetMethod(
            "GetDashboardConfigurationPage",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(pageMethod);
        Assert.DoesNotContain(
            pageMethod!.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "AuthorizeAttribute");

        // The configuration list endpoint stays administrator-gated.
        var listMethod = controllerType.GetMethod(
            "GetConfigurationPages",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(listMethod);
        var authorize = Assert.Single(
            listMethod!.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "AuthorizeAttribute");
        var policy = authorize.NamedArguments
            .Where(argument => argument.MemberName == "Policy")
            .Select(argument => argument.TypedValue.Value as string)
            .FirstOrDefault();
        Assert.Equal("RequiresElevation", policy);

        // No class-level [Authorize] narrows the whole controller.
        Assert.DoesNotContain(
            controllerType.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "AuthorizeAttribute");
    }

    private static readonly Type[] ConfigurationModelTypes =
    {
        typeof(PluginConfiguration),
        typeof(ArrConnectionConfiguration),
        typeof(OperationalLimits),
        typeof(RendererConfiguration),
        typeof(BadgeSelectorConfiguration),
    };

    private static Plugin CreatePlugin()
    {
        var paths = DispatchProxy.Create<IApplicationPaths, PluginStateLocationTests.TestApplicationPaths>();
        ((PluginStateLocationTests.TestApplicationPaths)(object)paths).RootPath =
            Path.Combine(Path.GetTempPath(), "arrtags-page-" + Guid.NewGuid().ToString("N"));
        return new Plugin(paths, null!, null!);
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResourceName);
        Assert.NotNull(stream);
        return ReadStream(stream!);
    }

    private static string ReadStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static object CreateController(Type controllerType)
    {
        var plugin = CreatePlugin();
        var manifest = new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = "1.0.0.0",
            Status = PluginStatus.Active,
            TargetAbi = "12.0.0.0",
        };
        var localPlugin = new LocalPlugin(Path.Combine(Path.GetTempPath(), "ArrTags"), isSupported: true, manifest)
        {
            Instance = plugin,
        };

        var manager = DispatchProxy.Create<IPluginManager, FakePluginManager>();
        ((FakePluginManager)(object)manager).PluginsValue = new[] { localPlugin };

        return Activator.CreateInstance(controllerType, new object?[] { null, manager })!;
    }

    private static Type LoadDashboardController()
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR");
        Assert.False(string.IsNullOrWhiteSpace(hostDirectory), "ARRTAGS_JELLYFIN_HOST_DIR must be set for this fact.");

        var apiPath = Path.Combine(hostDirectory!, "Jellyfin.Api.dll");
        Assert.True(File.Exists(apiPath), $"Expected the pinned host Jellyfin.Api.dll at {apiPath}.");

        var controller = HostApiAssembly.Value.GetType(DashboardControllerTypeName, throwOnError: false);
        Assert.NotNull(controller);
        return controller!;
    }

    private static readonly Lazy<Assembly> HostApiAssembly = new(() =>
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR")!;
        var apiPath = Path.Combine(hostDirectory, "Jellyfin.Api.dll");

        // Jellyfin.Api.dll references the other pinned host assemblies; resolve
        // them from the same directory so the controller type can be reflected.
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(hostDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(apiPath);
    });

    /// <summary>
    /// A minimal <see cref="IPluginManager"/> double that supplies only the
    /// <c>Plugins</c> list the pinned <c>DashboardController</c> reads.
    /// </summary>
    public class FakePluginManager : DispatchProxy
    {
        public IReadOnlyList<LocalPlugin> PluginsValue { get; set; } = Array.Empty<LocalPlugin>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Plugins")
            {
                return PluginsValue;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
