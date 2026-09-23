using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Serialization;
using ArrTags.Configuration;
using ArrTags.Rendering;
using Jellyfin.Extensions.Json;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 9 task 9.1 blocking round-trip spike. The elevation-gated
/// <c>PluginsController</c> POST deserializes the request body into the plugin
/// configuration type with <c>Jellyfin.Extensions.Json.JsonDefaults.Options</c>
/// (PascalCase, see <c>docs/research/jellyfin-12-architecture.md</c> section 9.3).
/// These tests prove that the persisted collection configuration properties are
/// populated rather than silently dropped, and that the same values survive the
/// XML configuration boundary, so a dashboard save cannot lose them (ADR-016
/// clause 7, first bullet).
/// </summary>
public class ConfigurationRoundTripTests
{
    private const string PluginsControllerTypeName = "Jellyfin.Api.Controllers.PluginsController";

    // A representative settings payload as the dashboard page would POST it
    // back: PascalCase property names matching the pinned deserialization
    // options, with both persisted collections populated.
    private const string RepresentativeSettingsPayload = """
        {
          "Sonarr": { "Enabled": false },
          "Radarr": { "Enabled": false },
          "WebhookSecret": "webhook-secret-value",
          "EnabledLibraries": [ "library-a", "library-b" ],
          "BadgeMoviePosters": true,
          "BadgeEpisodePosters": false,
          "Limits": { "QueueCapacity": 256 },
          "Renderer": {
            "Selectors": [
              { "Selector": "Quality", "Enabled": true, "Template": "{value}" },
              { "Selector": "Resolution", "Enabled": true, "Template": "{value}" },
              { "Selector": "Audio", "Enabled": false, "Template": "AUDIO" }
            ],
            "TechnicalBackground": "",
            "TechnicalText": "",
            "StatusBackground": "",
            "StatusText": ""
          }
        }
        """;

    [Fact]
    public void PinnedPostDeserializationOptionsArePascalCase()
    {
        // The pinned PluginsController POST assigns JsonDefaults.Options to the
        // serializer it uses (verified by the host-guarded fact below). A
        // PascalCase naming policy (null) is what makes the payload's property
        // names match; this pins the assumption the round-trip test relies on.
        Assert.Null(JsonDefaults.Options.PropertyNamingPolicy);
    }

    [Fact]
    public void PostRoundTripPopulatesEnabledLibraries()
    {
        var configuration = DeserializeRepresentativePayload();

        Assert.Equal(new[] { "library-a", "library-b" }, configuration.EnabledLibraries.ToArray());
    }

    [Fact]
    public void PostRoundTripPopulatesRendererSelectors()
    {
        var configuration = DeserializeRepresentativePayload();

        Assert.Equal(
            new[] { BadgeSelector.Quality, BadgeSelector.Resolution, BadgeSelector.Audio },
            configuration.Renderer.Selectors.Select(entry => entry.Selector).ToArray());
        Assert.Equal(
            new[] { true, true, false },
            configuration.Renderer.Selectors.Select(entry => entry.Enabled).ToArray());
        Assert.Equal("{value}", configuration.Renderer.Selectors[0].Template);
        Assert.Equal("AUDIO", configuration.Renderer.Selectors[2].Template);
    }

    [Fact]
    public void DeserializedConfigurationPassesValidation()
    {
        var configuration = DeserializeRepresentativePayload();

        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [Fact]
    public void PostRoundTripSurvivesXmlPersistAndReload()
    {
        var deserialized = DeserializeRepresentativePayload();

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, deserialized);

        using var reader = new StringReader(writer.ToString());
        var restored = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(new[] { "library-a", "library-b" }, restored.EnabledLibraries.ToArray());
        Assert.Equal(
            new[] { BadgeSelector.Quality, BadgeSelector.Resolution, BadgeSelector.Audio },
            restored.Renderer.Selectors.Select(entry => entry.Selector).ToArray());
        Assert.Equal(
            new[] { true, true, false },
            restored.Renderer.Selectors.Select(entry => entry.Enabled).ToArray());
    }

    [Fact]
    public void NullCollectionValuesAreTreatedAsEmpty()
    {
        // A malformed save must not introduce a null collection that later
        // throws during validation; the settable shape keeps the non-null
        // invariant the rest of the configuration code relies on.
        var configuration = JsonSerializer.Deserialize<PluginConfiguration>(
            """{ "EnabledLibraries": null, "Renderer": { "Selectors": null } }""",
            JsonDefaults.Options);

        Assert.NotNull(configuration);
        Assert.Empty(configuration!.EnabledLibraries);
        Assert.Empty(configuration.Renderer.Selectors);
        Assert.True(PluginConfigurationValidator.Validate(configuration).IsValid);
    }

    [JellyfinHostFact]
    public void PinnedPluginsControllerPostDeserializesWithThePinnedOptions()
    {
        var controllerType = LoadPluginsController();

        // The POST action is the supported configuration save path.
        var post = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SingleOrDefault(method => method.Name == "UpdatePluginConfiguration");
        Assert.NotNull(post);
        Assert.Contains(
            post!.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "HttpPostAttribute"
                && attribute.ConstructorArguments.Any(argument => Equals(argument.Value, "{pluginId}/Configuration")));

        // The pinned controller deserializes the body with a private
        // JsonSerializerOptions field assigned from JsonDefaults.Options in the
        // constructor. Constructing it with null dependencies is safe: the
        // constructor only assigns that field and does not dereference them.
        var optionsField = controllerType.GetField(
            "_serializerOptions",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(optionsField);
        Assert.Equal(typeof(JsonSerializerOptions), optionsField!.FieldType);

        var controller = Activator.CreateInstance(controllerType, new object?[] { null, null });
        Assert.NotNull(controller);
        Assert.Same(JsonDefaults.Options, optionsField.GetValue(controller));
    }

    private static PluginConfiguration DeserializeRepresentativePayload()
    {
        var configuration = JsonSerializer.Deserialize<PluginConfiguration>(
            RepresentativeSettingsPayload,
            JsonDefaults.Options);

        Assert.NotNull(configuration);
        return configuration!;
    }

    private static Type LoadPluginsController()
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR");
        Assert.False(string.IsNullOrWhiteSpace(hostDirectory), "ARRTAGS_JELLYFIN_HOST_DIR must be set for this fact.");

        var apiPath = Path.Combine(hostDirectory!, "Jellyfin.Api.dll");
        Assert.True(File.Exists(apiPath), $"Expected the pinned host Jellyfin.Api.dll at {apiPath}.");

        // Jellyfin.Api.dll references the other pinned host assemblies; resolve
        // them from the same directory so the controller type can be reflected.
        // The handler and assembly are cached so repeated facts in one process
        // do not register duplicate resolvers.
        var assembly = HostAssembly.Value;
        var controller = assembly.GetType(PluginsControllerTypeName, throwOnError: false);
        Assert.NotNull(controller);
        return controller!;
    }

    private static readonly Lazy<Assembly> HostAssembly = new(() =>
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR")!;
        var apiPath = Path.Combine(hostDirectory, "Jellyfin.Api.dll");

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(hostDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(apiPath);
    });
}
