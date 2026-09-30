using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Diagnostics;
using ArrTags.Matching;
using ArrTags.Providers;
using ArrTags.Rendering;
using ArrTags.Updates;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 17.2 focused checks for the administrator-authenticated, read-only
/// diagnostics status endpoint (ADR-025 clause 1): the elevation-gated
/// controller surface, the single read-only route, the read-only dependency and
/// action, the fixed bounded serialized shape, and the authorization behavior
/// through the real ASP.NET Core MVC pipeline with the pinned host policy shape.
/// No live Jellyfin host is required; the pinned-host policy confirmation is a
/// host-guarded fact.
/// </summary>
public sealed class DiagnosticsStatusEndpointTests
{
    /// <summary>
    /// The pinned host's administrator role claim value. The host policy is
    /// defined in <c>Jellyfin.Server</c>'s <c>AddJellyfinApiAuthorization</c> as
    /// the custom-authentication scheme plus
    /// <c>.RequireClaim(ClaimTypes.Role, UserRoles.Administrator)</c>
    /// (docs/research/jellyfin-12-architecture.md section 9.3); the
    /// <c>UserRoles</c> type is host-only, so the harness pins the value.
    /// </summary>
    private const string AdministratorRole = "Administrator";

    [Fact]
    public void ControllerIsAnElevationGatedApiControllerWithOneGetRoute()
    {
        var type = typeof(ArrTagsStatusController);

        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>(inherit: false));

        var route = Assert.Single(type.GetCustomAttributes<RouteAttribute>(inherit: false));
        Assert.Equal(ArrTagsStatusController.RoutePrefix, route.Template);
        Assert.Equal("ArrTags/Status", ArrTagsStatusController.RoutePrefix);

        // ADR-025 clause 1: the endpoint is gated by the pinned host's
        // administrator elevation policy, so only an authenticated administrator
        // can read it.
        Assert.Equal("RequiresElevation", Policies.RequiresElevation);
        var authorize = Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        Assert.Equal(Policies.RequiresElevation, authorize.Policy);

        // The endpoint is never anonymous, at class or action level.
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));
        foreach (var method in DeclaredActions(type))
        {
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false));
        }
    }

    [Fact]
    public void ControllerExposesExactlyOneReadOnlyGetActionAndNoWriteRoute()
    {
        var declared = DeclaredActions(typeof(ArrTagsStatusController));

        var action = Assert.Single(declared);
        Assert.Equal(nameof(ArrTagsStatusController.GetStatus), action.Name);
        Assert.Equal(typeof(ActionResult<DiagnosticsSnapshot>), action.ReturnType);

        var get = Assert.Single(action.GetCustomAttributes<HttpGetAttribute>(inherit: false));
        Assert.True(string.IsNullOrEmpty(get.Template));

        // Read-only means no write verb and no second route anywhere on the
        // controller: the only MVC route attribute is the class-level prefix.
        foreach (var method in declared)
        {
            Assert.Null(method.GetCustomAttribute<HttpPostAttribute>(inherit: false));
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>(inherit: false));
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>(inherit: false));
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>(inherit: false));
            Assert.Empty(method.GetCustomAttributes<RouteAttribute>(inherit: false));
        }
    }

    [Fact]
    public void ControllerDependsOnlyOnTheReadOnlySnapshotProvider()
    {
        var constructor = Assert.Single(typeof(ArrTagsStatusController).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(DiagnosticsSnapshotProvider), parameter.ParameterType);

        // The single dependency is read-only by construction: it exposes one
        // public method that captures the in-memory snapshot and no mutating
        // operation, so the action cannot mutate configuration, work, or
        // artwork.
        var providerMethod = Assert.Single(
            typeof(DiagnosticsSnapshotProvider).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);
        Assert.Equal(nameof(DiagnosticsSnapshotProvider.GetSnapshot), providerMethod.Name);
        Assert.Equal(typeof(DiagnosticsSnapshot), providerMethod.ReturnType);
        Assert.Empty(providerMethod.GetParameters());
    }

    [Fact]
    public void ActionReturnsTheLiveFixedShapeSnapshotAndChangesNoState()
    {
        var metrics = new DiagnosticsMetrics();
        using var queue = new LibraryWorkQueue(capacity: 8);
        var provider = new DiagnosticsSnapshotProvider(metrics, queue);
        var controller = new ArrTagsStatusController(provider);

        metrics.RecordCacheHit();
        metrics.RecordCacheMiss();
        metrics.RecordMatchingFailure(MediaMatchStatus.NotFound);
        metrics.RecordRenderFailure(RenderFailureReason.DecodeFailed);
        metrics.RecordStaleMetadata();
        metrics.RecordProviderHealth(ArrProviderKind.Sonarr, ArrConnectionHealth.Healthy);
        metrics.RecordProviderHealth(ArrProviderKind.Radarr, ArrConnectionHealth.Unavailable);
        Assert.True(queue.TryEnqueue(WorkItem()));

        var before = provider.GetSnapshot();

        var result = controller.GetStatus();

        var returned = Assert.IsType<DiagnosticsSnapshot>(result.Value);
        AssertSnapshotsEqual(before, returned);
        Assert.Equal(1, returned.QueueDepth);
        Assert.Equal(1, returned.CacheHits);
        Assert.Equal(1, returned.CacheMisses);
        Assert.Equal(1, returned.MatchingFailures.NotFound);
        Assert.Equal(1, returned.RenderFailures.DecodeFailed);
        Assert.Equal(1, returned.StaleMetadataTransitions);
        Assert.Equal(ArrConnectionHealth.Healthy, returned.SonarrHealth);
        Assert.Equal(ArrConnectionHealth.Unavailable, returned.RadarrHealth);

        // The read changed nothing: the counters, queue depth, and in-flight
        // count are exactly as they were before the action ran.
        AssertSnapshotsEqual(before, provider.GetSnapshot());
        Assert.Equal(1, queue.Count);
        Assert.Equal(0, queue.InFlightCount);
    }

    [Fact]
    public void SerializedSnapshotCarriesExactlyTheFixedCounterPathsAndNoStringArrayOrSecretCarrier()
    {
        var metrics = new DiagnosticsMetrics();
        using var queue = new LibraryWorkQueue(capacity: 8);
        var provider = new DiagnosticsSnapshotProvider(metrics, queue);

        metrics.RecordCacheHit();
        metrics.RecordCacheMiss();
        metrics.RecordMatchingFailure(MediaMatchStatus.NotFound);
        metrics.RecordMatchingFailure(MediaMatchStatus.Ambiguous);
        metrics.RecordMatchingFailure(MediaMatchStatus.Unsupported);
        foreach (var reason in Enum.GetValues<RenderFailureReason>())
        {
            metrics.RecordRenderFailure(reason);
        }

        metrics.RecordStaleMetadata();
        metrics.RecordProviderHealth(ArrProviderKind.Sonarr, ArrConnectionHealth.Healthy);
        metrics.RecordProviderHealth(ArrProviderKind.Radarr, ArrConnectionHealth.Unavailable);

        var json = JsonSerializer.Serialize(provider.GetSnapshot(), JsonDefaults.Options);
        using var document = JsonDocument.Parse(json);

        AssertFixedBoundedResponseShape(document.RootElement);

        // The fixed paths carry the recorded values.
        Assert.Equal(1, document.RootElement.GetProperty("CacheHits").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("CacheMisses").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("MatchingFailures").GetProperty("NotFound").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("MatchingFailures").GetProperty("Unsupported").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("RenderFailures").GetProperty("Cancelled").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("RenderFailures").GetProperty("DecodeFailed").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("StaleMetadataTransitions").GetInt64());
        Assert.Equal("Healthy", document.RootElement.GetProperty("SonarrHealth").GetString(), ignoreCase: true);
        Assert.Equal("Unavailable", document.RootElement.GetProperty("RadarrHealth").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task AnonymousRequestIsChallengedWithoutASnapshot()
    {
        using var harness = new StatusPipelineHarness();

        var response = await harness.GetStatusAsync(role: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task AuthenticatedNonAdministratorIsForbidden()
    {
        using var harness = new StatusPipelineHarness();

        var response = await harness.GetStatusAsync(role: "User");

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task AuthenticatedAdministratorReceivesTheExactBoundedSnapshot()
    {
        using var harness = new StatusPipelineHarness();
        harness.Metrics.RecordCacheHit();
        harness.Metrics.RecordCacheMiss();
        harness.Metrics.RecordMatchingFailure(MediaMatchStatus.Ambiguous);
        harness.Metrics.RecordRenderFailure(RenderFailureReason.LayoutFailed);
        harness.Metrics.RecordStaleMetadata();
        harness.Metrics.RecordProviderHealth(ArrProviderKind.Sonarr, ArrConnectionHealth.Healthy);
        harness.Metrics.RecordProviderHealth(ArrProviderKind.Radarr, ArrConnectionHealth.Unavailable);
        Assert.True(harness.Queue.TryEnqueue(WorkItem()));

        var expected = harness.SnapshotProvider.GetSnapshot();

        var response = await harness.GetStatusAsync(AdministratorRole);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        AssertFixedBoundedResponseShape(document.RootElement);

        // The response body is exactly the serialization of the live snapshot
        // under the endpoint's JSON options.
        var expectedJson = JsonSerializer.Serialize(expected, harness.JsonSerializerOptions);
        using var expectedDocument = JsonDocument.Parse(expectedJson);
        Assert.Equal(expectedDocument.RootElement.GetRawText(), document.RootElement.GetRawText());
    }

    [JellyfinHostFact]
    public void EndpointUsesThePinnedHostAdministratorGatingPolicy()
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR");
        Assert.False(string.IsNullOrWhiteSpace(hostDirectory), "ARRTAGS_JELLYFIN_HOST_DIR must be set for this fact.");

        var apiPath = Path.Combine(hostDirectory!, "Jellyfin.Api.dll");
        Assert.True(File.Exists(apiPath), $"Expected the pinned host Jellyfin.Api.dll at {apiPath}.");

        // The pinned host gates its administrator configuration controller with
        // the same policy the status endpoint declares (research section 9.3).
        var pluginsController = HostApiAssembly.Value.GetType(
            "Jellyfin.Api.Controllers.PluginsController",
            throwOnError: false);
        Assert.NotNull(pluginsController);

        Assert.Equal(Policies.RequiresElevation, ClassLevelAuthorizePolicy(pluginsController!));
        Assert.Equal(Policies.RequiresElevation, ClassLevelAuthorizePolicy(typeof(ArrTagsStatusController)));
    }

    private static readonly Lazy<Assembly> HostApiAssembly = new(() =>
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

    private static string? ClassLevelAuthorizePolicy(Type type)
    {
        var authorize = Assert.Single(
            type.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName == "Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
        return authorize.NamedArguments
            .SingleOrDefault(argument => argument.MemberName == "Policy")
            .TypedValue.Value as string;
    }

    private static MethodInfo[] DeclaredActions(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();
    }

    private static void AssertSnapshotsEqual(DiagnosticsSnapshot expected, DiagnosticsSnapshot actual)
    {
        Assert.Equal(expected.QueueDepth, actual.QueueDepth);
        Assert.Equal(expected.QueueInFlight, actual.QueueInFlight);
        Assert.Equal(expected.SonarrHealth, actual.SonarrHealth);
        Assert.Equal(expected.RadarrHealth, actual.RadarrHealth);
        Assert.Equal(expected.MatchingFailures.NotFound, actual.MatchingFailures.NotFound);
        Assert.Equal(expected.MatchingFailures.Ambiguous, actual.MatchingFailures.Ambiguous);
        Assert.Equal(expected.MatchingFailures.Unsupported, actual.MatchingFailures.Unsupported);
        Assert.Equal(expected.CacheHits, actual.CacheHits);
        Assert.Equal(expected.CacheMisses, actual.CacheMisses);
        Assert.Equal(expected.RenderFailures.DecodeFailed, actual.RenderFailures.DecodeFailed);
        Assert.Equal(expected.RenderFailures.RenderError, actual.RenderFailures.RenderError);
        Assert.Equal(expected.StaleMetadataTransitions, actual.StaleMetadataTransitions);
    }

    /// <summary>
    /// Asserts the serialized response has exactly the fixed, bounded leaf set:
    /// the two queue observations, the two bounded health enum values, the three
    /// matching classifications, the two cache counts, the eighteen render
    /// classifications, and the stale-metadata transition count. Every leaf is a
    /// number or one of the two declared <see cref="ArrConnectionHealth"/> names;
    /// no array, object, boolean, or null carrier can appear, so no secret,
    /// path, item name, identifier list, provider payload, or unbounded
    /// collection can be serialized.
    /// </summary>
    private static void AssertFixedBoundedResponseShape(JsonElement root)
    {
        var leafPaths = new List<string>();
        var stringLeaves = new List<(string Path, string Value)>();
        CollectLeaves(root, string.Empty, leafPaths, stringLeaves);

        var expected = ExpectedLeafPaths().ToArray();
        Assert.Equal(28, expected.Length);
        Assert.Equal(
            expected.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            leafPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray());

        Assert.Equal(
            new[] { "RadarrHealth", "SonarrHealth" },
            stringLeaves.Select(leaf => leaf.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray());
        foreach (var leaf in stringLeaves)
        {
            Assert.True(
                Enum.TryParse<ArrConnectionHealth>(leaf.Value, ignoreCase: true, out _),
                $"The response carries the non-bounded string value '{leaf.Value}' at '{leaf.Path}'.");
        }
    }

    private static void CollectLeaves(
        JsonElement element,
        string path,
        List<string> leafPaths,
        List<(string Path, string Value)> stringLeaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
                    CollectLeaves(property.Value, childPath, leafPaths, stringLeaves);
                }

                break;
            case JsonValueKind.Number:
                leafPaths.Add(path);
                break;
            case JsonValueKind.String:
                leafPaths.Add(path);
                stringLeaves.Add((path, element.GetString() ?? string.Empty));
                break;
            default:
                throw new InvalidOperationException(
                    $"The diagnostics response carries the non-bounded JSON value kind {element.ValueKind} at '{path}'.");
        }
    }

    private static IEnumerable<string> ExpectedLeafPaths()
    {
        yield return "QueueDepth";
        yield return "QueueInFlight";
        yield return "SonarrHealth";
        yield return "RadarrHealth";
        yield return "MatchingFailures.NotFound";
        yield return "MatchingFailures.Ambiguous";
        yield return "MatchingFailures.Unsupported";
        yield return "CacheHits";
        yield return "CacheMisses";
        yield return "StaleMetadataTransitions";
        foreach (var reason in Enum.GetValues<RenderFailureReason>())
        {
            yield return "RenderFailures." + reason;
        }
    }

    private static LibraryWorkItem WorkItem()
    {
        return new LibraryWorkItem(
            new WorkItemKey(Guid.NewGuid(), null, ArtworkImageSurface.Primary),
            LibraryWorkReason.Updated,
            configurationVersion: 1);
    }

    private sealed class PipelineResponse
    {
        public PipelineResponse(int statusCode, byte[] body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public int StatusCode { get; }

        public byte[] Body { get; }
    }

    /// <summary>
    /// Exercises the real ASP.NET Core MVC pipeline (controller discovery, the
    /// authorization filter, and result execution) for the status controller
    /// in-process. The harness registers the same policy shape the pinned host
    /// defines for <c>Policies.RequiresElevation</c>: an authenticated principal
    /// carrying the administrator role claim. The test authentication handler
    /// and the explicitly set <see cref="HttpContext.User"/> stand in for the
    /// host's authentication middleware; the pinned host's own policy definition
    /// is confirmed by the host-guarded fact.
    /// </summary>
    private sealed class StatusPipelineHarness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ActionDescriptorCollection _descriptors;

        public StatusPipelineHarness()
        {
            Metrics = new DiagnosticsMetrics();
            Queue = new LibraryWorkQueue(capacity: 8);
            SnapshotProvider = new DiagnosticsSnapshotProvider(Metrics, Queue);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<System.Diagnostics.DiagnosticListener>(
                new System.Diagnostics.DiagnosticListener("ArrTags.Tests.StatusMvc"));
            services.AddControllers()
                .AddApplicationPart(typeof(ArrTagsStatusController).Assembly)
                .AddMvcOptions(options =>
                {
                    // Endpoint routing moves [Authorize] enforcement from MVC's
                    // AuthorizeFilter to the AuthorizationMiddleware. This
                    // in-process harness invokes the MVC action directly (no
                    // middleware pipeline), so it disables endpoint routing to
                    // have MVC's own AuthorizeFilter enforce the same
                    // attribute metadata against the same policy.
                    options.EnableEndpointRouting = false;
                })
                .AddJsonOptions(options =>
                {
                    // The pinned host's MVC JSON options mirror
                    // JsonDefaults.Options: PascalCase names and string enums.
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonDefaults.Options.PropertyNamingPolicy;
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });
            services.AddSingleton(SnapshotProvider);
            services.AddSingleton(Queue);
            services.AddAuthorization(options => options.AddPolicy(
                Policies.RequiresElevation,
                policy => policy.RequireAuthenticatedUser().RequireClaim(ClaimTypes.Role, AdministratorRole)));
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });

            _provider = services.BuildServiceProvider();
            _descriptors = _provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors;
        }

        public DiagnosticsMetrics Metrics { get; }

        public LibraryWorkQueue Queue { get; }

        public DiagnosticsSnapshotProvider SnapshotProvider { get; }

        public JsonSerializerOptions JsonSerializerOptions =>
            _provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        public async Task<PipelineResponse> GetStatusAsync(string? role)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = _provider;
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Request.Path = "/" + ArrTagsStatusController.RoutePrefix;
            if (role is not null)
            {
                httpContext.Request.Headers[TestAuthenticationHandler.RoleHeaderName] = role;
                httpContext.User = TestAuthenticationHandler.CreatePrincipal(role);
            }

            var responseBody = new MemoryStream();
            httpContext.Response.Body = responseBody;

            var descriptor = _descriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Single(candidate => candidate.ControllerTypeInfo.AsType() == typeof(ArrTagsStatusController)
                    && candidate.ActionName == nameof(ArrTagsStatusController.GetStatus));

            var routeData = new RouteData();
            foreach (var routeValue in descriptor.RouteValues)
            {
                routeData.Values[routeValue.Key] = routeValue.Value;
            }

            var actionContext = new ActionContext(httpContext, routeData, descriptor);
            var invoker = _provider.GetRequiredService<IActionInvokerFactory>().CreateInvoker(actionContext);
            Assert.NotNull(invoker);
            await invoker!.InvokeAsync();

            return new PipelineResponse(httpContext.Response.StatusCode, responseBody.ToArray());
        }

        public void Dispose()
        {
            _provider.Dispose();
            Queue.Dispose();
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ArrTagsStatusTest";
        public const string RoleHeaderName = "X-ArrTags-Test-Role";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        public static ClaimsPrincipal CreatePrincipal(string role)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "arrtags-status-test"),
                    new Claim(ClaimTypes.Role, role),
                },
                SchemeName);
            return new ClaimsPrincipal(identity);
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers[RoleHeaderName].ToString();
            if (string.IsNullOrEmpty(role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(CreatePrincipal(role), SchemeName)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
