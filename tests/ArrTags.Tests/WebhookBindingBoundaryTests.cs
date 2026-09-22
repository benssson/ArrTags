using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Secrets;
using ArrTags.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Regression tests for release security finding SEC-1: the inbound webhook
/// boundary must authenticate before MVC model binding materializes the form
/// value providers and reads the request body. These tests exercise the real
/// ASP.NET Core MVC pipeline (controller discovery, filters, model binding, and
/// result execution) in-process rather than calling the action directly, and
/// they assert that an unauthenticated request never reads the body for any
/// content type. They require no live Jellyfin or Arr instance.
/// </summary>
public sealed class WebhookBindingBoundaryTests
{
    private const string Secret = "correct-horse-battery-staple";

    [Fact]
    public async Task UnauthenticatedJsonBodyReturnsUniformUnauthorizedWithoutReadingTheBody()
    {
        using var harness = new WebhookPipelineHarness();

        var result = await harness.PostAsync("Sonarr", "application/json", RadarrDownload(), secret: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal(0, harness.RequestBody.ReadCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task UnauthenticatedFormBodyReturnsUniformUnauthorizedWithoutReadingTheBody()
    {
        using var harness = new WebhookPipelineHarness();
        var body = Encoding.UTF8.GetBytes("{\"eventType\":\"Download\"}");

        var result = await harness.PostAsync("Sonarr", "application/x-www-form-urlencoded", body, secret: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal(0, harness.RequestBody.ReadCalls);
        Assert.Equal(0, harness.Intake.Count);
        Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(result.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthenticatedMultipartBodyReturnsUniformUnauthorizedWithoutReadingTheBody()
    {
        using var harness = new WebhookPipelineHarness();
        var body = Encoding.UTF8.GetBytes("--xyz\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nvalue\r\n--xyz--\r\n");

        var result = await harness.PostAsync("Sonarr", "multipart/form-data; boundary=xyz", body, secret: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal(0, harness.RequestBody.ReadCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task UnauthenticatedOversizedFormBodyReturnsUnauthorizedBeforeTheFrameworkReadsIt()
    {
        using var harness = new WebhookPipelineHarness();
        var body = Encoding.UTF8.GetBytes(new string('a', 300_000));

        var result = await harness.PostAsync("Sonarr", "application/x-www-form-urlencoded", body, secret: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal(0, harness.RequestBody.ReadCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task AuthenticatedNonJsonBodyIsRejectedBoundedWithoutReadingTheBody()
    {
        using var harness = new WebhookPipelineHarness();
        var body = Encoding.UTF8.GetBytes(new string('a', 300_000));

        var result = await harness.PostAsync("Sonarr", "application/x-www-form-urlencoded", body, secret: Secret);

        Assert.True(
            result.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status413PayloadTooLarge,
            FormattableString.Invariant($"Expected a bounded 400/413 rejection, observed {result.StatusCode}."));
        Assert.Equal(0, harness.RequestBody.ReadCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task AuthenticatedNonJsonMultipartBodyIsRejectedBoundedWithoutReadingTheBody()
    {
        using var harness = new WebhookPipelineHarness();
        var body = Encoding.UTF8.GetBytes(new string('a', 300_000));

        var result = await harness.PostAsync("Sonarr", "multipart/form-data; boundary=xyz", body, secret: Secret);

        Assert.True(
            result.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status413PayloadTooLarge,
            FormattableString.Invariant($"Expected a bounded 400/413 rejection, observed {result.StatusCode}."));
        Assert.Equal(0, harness.RequestBody.ReadCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task AuthenticatedJsonBodyIsAcceptedAndEnqueued()
    {
        using var harness = new WebhookPipelineHarness();

        var result = await harness.PostAsync("Radarr", "application/json", RadarrDownload(), secret: Secret);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.Equal(1, harness.Intake.Count);
    }

    [Fact]
    public async Task AuthenticatedMalformedJsonBodyIsRejectedWithBadRequest()
    {
        using var harness = new WebhookPipelineHarness();

        var result = await harness.PostAsync("Radarr", "application/json", Encoding.UTF8.GetBytes("{"), secret: Secret);

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task AuthenticatedOversizedJsonBodyIsRejectedWithPayloadTooLarge()
    {
        using var harness = new WebhookPipelineHarness();
        var oversized = new byte[harness.Configuration.Current.Limits.WebhookMaxPayloadBytes + 1];

        var result = await harness.PostAsync("Radarr", "application/json", oversized, secret: Secret);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task JsonContentTypeWithCharsetIsAccepted()
    {
        using var harness = new WebhookPipelineHarness();

        var result = await harness.PostAsync("Radarr", "application/json; charset=utf-8", RadarrDownload(), secret: Secret);

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.Equal(1, harness.Intake.Count);
    }

    private static byte[] RadarrDownload()
    {
        return Encoding.UTF8.GetBytes(
            "{\"eventType\":\"Download\",\"instanceName\":\"Radarr\","
            + "\"movie\":{\"id\":123,\"title\":\"Example\",\"year\":2020},"
            + "\"movieFile\":{\"id\":456,\"relativePath\":\"Example.mkv\"},\"isUpgrade\":true}");
    }

    private sealed class WebhookPipelineHarness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ActionDescriptorCollection _descriptors;
        private readonly WebhookIntake _intake;

        public WebhookPipelineHarness()
        {
            var configuration = new PluginConfiguration
            {
                WebhookSecret = Secret,
                Sonarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://sonarr.test",
                    ApiKey = "sonarr-key",
                },
                Radarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://radarr.test",
                    ApiKey = "radarr-key",
                },
            };

            Configuration = new ConfigurationSnapshotService(configuration);
            _intake = new WebhookIntake(8);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<System.Diagnostics.DiagnosticListener>(new System.Diagnostics.DiagnosticListener("ArrTags.Tests.Mvc"));
            services.AddControllers().AddApplicationPart(typeof(ArrTagsWebhookController).Assembly);
            services.AddSingleton(Configuration);
            services.AddSingleton<IPluginSecretResolver>(Configuration);
            services.AddSingleton<IWebhookIntake>(_intake);
            _provider = services.BuildServiceProvider();

            _descriptors = _provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors;
        }

        public ConfigurationSnapshotService Configuration { get; }

        public WebhookIntake Intake => _intake;

        public TrackingReadStream RequestBody { get; private set; } = new TrackingReadStream(Array.Empty<byte>());

        public async Task<PipelineResponse> PostAsync(string route, string contentType, byte[] body, string? secret)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = _provider;
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.Path = $"/ArrTags/Webhook/{route}";
            httpContext.Request.ContentType = contentType;
            httpContext.Request.ContentLength = body.Length;
            RequestBody = new TrackingReadStream(body);
            httpContext.Request.Body = RequestBody;
            if (secret is not null)
            {
                httpContext.Request.Headers[ArrTagsWebhookController.SecretHeaderName] = secret;
            }

            var responseBody = new MemoryStream();
            httpContext.Response.Body = responseBody;

            var descriptor = _descriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Single(
                    candidate => candidate.ControllerTypeInfo.AsType() == typeof(ArrTagsWebhookController)
                        && candidate.ActionName == $"Receive{route}");

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
            _intake.Dispose();
        }
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
    /// A request body stream that counts how many times the pipeline reads from
    /// it, so a test can prove the body was never read before authentication.
    /// </summary>
    private sealed class TrackingReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public TrackingReadStream(byte[] body)
        {
            _inner = new MemoryStream(body);
        }

        public int ReadCalls { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ReadCalls++;
            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadCalls++;
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
