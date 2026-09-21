using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Secrets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ArrTags.Webhooks;

/// <summary>
/// The inbound Arr webhook boundary. Jellyfin discovers exported
/// <see cref="ControllerBase"/> types in plugin assemblies as independently
/// routed API controllers, so this controller is registered with the host
/// without any ArrTags-specific registration. It is anonymous because the caller
/// is a Sonarr/Radarr instance, not a Jellyfin user; the request is authenticated
/// by the configured shared secret header. The action authenticates first, then
/// enforces the bounded payload limit, then tolerantly parses the payload, then
/// performs a non-blocking bounded submit and returns. It never logs or returns
/// the secret, headers, or body, never publishes metadata or artwork, and never
/// calls an Arr endpoint (ADR-012).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(ArrTagsWebhookController.RoutePrefix)]
public sealed class ArrTagsWebhookController : ControllerBase
{
    /// <summary>
    /// The route prefix for the inbound webhook endpoints.
    /// </summary>
    public const string RoutePrefix = "ArrTags/Webhook";

    /// <summary>
    /// The request header that carries the configured webhook shared secret.
    /// </summary>
    public const string SecretHeaderName = "X-ArrTags-Webhook-Secret";

    /// <summary>
    /// The Sonarr webhook route segment.
    /// </summary>
    public const string SonarrRoute = "Sonarr";

    /// <summary>
    /// The Radarr webhook route segment.
    /// </summary>
    public const string RadarrRoute = "Radarr";

    private readonly ConfigurationSnapshotService _configuration;
    private readonly IPluginSecretResolver _secrets;
    private readonly IWebhookIntake _intake;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsWebhookController"/> class.
    /// </summary>
    /// <param name="configuration">The current public configuration snapshot.</param>
    /// <param name="secrets">The versioned secret resolver used for constant-time authentication.</param>
    /// <param name="intake">The bounded, non-blocking webhook intake.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public ArrTagsWebhookController(
        ConfigurationSnapshotService configuration,
        IPluginSecretResolver secrets,
        IWebhookIntake intake)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
    }

    /// <summary>
    /// Receives an authenticated Sonarr webhook delivery.
    /// </summary>
    /// <param name="cancellationToken">The request cancellation signal.</param>
    /// <returns>A bounded safe status code.</returns>
    [HttpPost(SonarrRoute)]
    public Task<IActionResult> ReceiveSonarrAsync(CancellationToken cancellationToken)
    {
        return ReceiveAsync(ArrProviderKind.Sonarr, cancellationToken);
    }

    /// <summary>
    /// Receives an authenticated Radarr webhook delivery.
    /// </summary>
    /// <param name="cancellationToken">The request cancellation signal.</param>
    /// <returns>A bounded safe status code.</returns>
    [HttpPost(RadarrRoute)]
    public Task<IActionResult> ReceiveRadarrAsync(CancellationToken cancellationToken)
    {
        return ReceiveAsync(ArrProviderKind.Radarr, cancellationToken);
    }

    /// <summary>
    /// Handles one inbound webhook delivery: authenticate, bound, parse, submit.
    /// Public so the boundary can be verified without a live host; it is not an
    /// MVC action.
    /// </summary>
    /// <param name="providerKind">The provider family the route identified.</param>
    /// <param name="cancellationToken">The request cancellation signal.</param>
    /// <returns>A bounded safe status code.</returns>
    [NonAction]
    public async Task<IActionResult> ReceiveAsync(ArrProviderKind providerKind, CancellationToken cancellationToken)
    {
        var snapshot = _configuration.Current;
        var candidate = Request.Headers[SecretHeaderName].ToString();
        var authentication = WebhookAuthentication.Authenticate(_secrets, snapshot, candidate);
        if (authentication != WebhookAuthenticationResult.Authenticated)
        {
            // Fail closed with a uniform safe status. The response never reveals
            // whether a secret is configured, why the candidate was rejected, or
            // any header/body content.
            return Unauthorized();
        }

        var body = await WebhookBodyReader
            .ReadAsync(Request, snapshot.Limits.WebhookMaxPayloadBytes, cancellationToken)
            .ConfigureAwait(false);
        if (body.TooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!WebhookEventParser.TryParse(body.Bytes, providerKind, out var webhookEvent, out _)
            || webhookEvent is null)
        {
            return BadRequest();
        }

        // The submit is non-blocking and bounded. A coalesced, overflowed, or
        // unsupported event is acknowledged and repaired by the authoritative
        // periodic reconciliation.
        _intake.TrySubmit(webhookEvent);
        return Accepted();
    }
}
