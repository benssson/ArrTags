namespace ArrTags.Secrets;

/// <summary>
/// The purpose of a credential slot. Purposes are not interchangeable: an Arr
/// API-key lease cannot satisfy a webhook-authentication comparison and vice
/// versa.
/// </summary>
public enum SecretPurpose
{
    /// <summary>
    /// The API key for one Sonarr or Radarr connection.
    /// </summary>
    ArrApiKey,

    /// <summary>
    /// The shared secret that authenticates inbound Arr webhook requests.
    /// </summary>
    WebhookAuthentication,
}
