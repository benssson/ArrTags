using System;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Secrets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArrTags.Webhooks;

/// <summary>
/// The pre-model-binding guard for the anonymous inbound Arr webhook boundary.
/// MVC runs authorization filters before it binds action arguments, and binding
/// unconditionally materializes the composite value provider — including the
/// form value providers, which read and parse the request body for form content
/// types. Authenticating inside the action is therefore too late for those
/// content types: the body would already have been read up to the framework's
/// limits and the configured <see cref="OperationalLimits.WebhookMaxPayloadBytes"/>
/// would not be the effective bound (release security finding SEC-1).
/// </summary>
/// <remarks>
/// This filter authenticates the shared secret first and fails closed with the
/// uniform <c>401</c> before any body read, so every content type observes the
/// same authenticate-first contract. It then rejects a body that is not JSON
/// before binding, so the configured payload bound — not the framework form
/// limits or Kestrel's global cap — governs the authenticated webhook path. The
/// action re-authenticates and applies the byte bound and tolerant parse, so the
/// filter is a strict pre-binding gate rather than a replacement for the action
/// boundary. It never reflects the secret, headers, or body.
/// </remarks>
public sealed class WebhookAuthenticationFilter : IAsyncAuthorizationFilter
{
    private readonly ConfigurationSnapshotService _configuration;
    private readonly IPluginSecretResolver _secrets;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookAuthenticationFilter"/> class.
    /// </summary>
    /// <param name="configuration">The current public configuration snapshot.</param>
    /// <param name="secrets">The versioned secret resolver used for constant-time authentication.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public WebhookAuthenticationFilter(
        ConfigurationSnapshotService configuration,
        IPluginSecretResolver secrets)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <inheritdoc />
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.HttpContext.Request;
        var snapshot = _configuration.Current;
        var candidate = request.Headers[ArrTagsWebhookController.SecretHeaderName].ToString();
        if (WebhookAuthentication.Authenticate(_secrets, snapshot, candidate) != WebhookAuthenticationResult.Authenticated)
        {
            // Fail closed with the uniform safe status before any body read, so
            // the response never reveals whether a secret is configured, why the
            // candidate was rejected, or any header/body content.
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        if (!IsJsonContentType(request.ContentType))
        {
            // A form or multipart body would be read and parsed by MVC binding
            // before the action could apply the configured byte bound. Reject it
            // here, before the read, rather than letting the framework consume it
            // up to its own limits. The status stays a bounded client error and
            // no body detail is reflected.
            context.Result = new BadRequestResult();
        }

        return Task.CompletedTask;
    }

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            // No declared content type: the bytes are still bounded and parsed
            // as JSON by the action, so preserve the tolerant historical path.
            return true;
        }

        var mediaType = contentType;
        var separator = mediaType.IndexOf(';', StringComparison.Ordinal);
        if (separator >= 0)
        {
            mediaType = mediaType.Substring(0, separator);
        }

        mediaType = mediaType.Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
