# Jellyfin 12 Extension-Point Findings

**Status:** Research, Jellyfin `v12.0`

**Source revision:** `6c073e19ddf604b2369c638716164fdab4c952dc`

**Purpose:** Establish which Jellyfin 12 plugin and framework extension points
can provide, replace, or augment item artwork. This document records source
findings and does not select the ArrTags architecture.

## 1. Evidence categories

- **Confirmed supported API:** A public Jellyfin plugin contract used by the
  server through an intended extension path.
- **Public but potentially unstable API:** A public interface or framework API
  that is callable by a plugin but is not a dedicated, documented contract for
  the required behavior.
- **Internal implementation detail:** Core implementation behavior that a
  plugin should not depend on as an extension contract.
- **Unsupported workaround:** A technically possible interception or service
  replacement that changes behavior outside a supported plugin contract.

## 2. Jellyfin plugin and HTTP registration

### 2.1 Plugin service registration

`MediaBrowser.Controller.Plugins.IPluginServiceRegistrator` is a confirmed
supported plugin API. Jellyfin discovers parameterless implementations before
the service provider is built and calls:

```csharp
void RegisterServices(IServiceCollection services, IServerApplicationHost host)
```

This permits a plugin to register its own services and standard ASP.NET Core
services. It does not itself define an image-response extension point.

### 2.2 Plugin controllers

Jellyfin collects exported `ControllerBase` types from plugin assemblies through
`ApplicationHost.GetApiPluginAssemblies`. `AddJellyfinApi` adds those assemblies
as MVC application parts and registers controllers as services. A plugin can
therefore add a supported, independently routed API controller.

This supports adding routes. It does not support replacing or overriding
Jellyfin's existing `ImageController` routes. A custom image route would require
clients to request that route explicitly.

### 2.3 Middleware and MVC filters

`IStartupFilter`, `UseMiddleware`, `IAsyncActionFilter`, and
`MvcOptions.Filters.AddService<T>` are public ASP.NET Core APIs available to
plugin service registration. They are not Jellyfin image-provider contracts.

They can observe or alter the HTTP/MVC pipeline, but their behavior depends on
Jellyfin's pipeline order, route definitions, action signatures, result types,
authorization behavior, and response-header behavior. Those details are not
promised as a stable image-overlay API.

### 2.4 HTTP client factory

Jellyfin 12 registers the standard `System.Net.Http.IHttpClientFactory` through
dependency injection. This is not a Jellyfin-specific interface in
`MediaBrowser.Common.Net`; Jellyfin exposes the standard .NET/ASP.NET client
factory through its plugin service-registration path.

`Jellyfin.Server.Startup` registers Jellyfin's named clients with
`AddHttpClient`, and `MediaBrowser.Common.Net.NamedClient` exposes the current
names:

- `NamedClient.Default`;
- `NamedClient.MusicBrainz`;
- `NamedClient.Dlna`;
- `NamedClient.DirectIp`.

`IPluginServiceRegistrator` implementations can resolve
`IHttpClientFactory`, use an existing Jellyfin named client, or register their
own named or typed clients with the standard `AddHttpClient` APIs. Dedicated
clients are appropriate when a plugin needs independent base URLs, headers,
timeouts, handlers, or certificate policy.

The supported boundary is the factory and DI registration contract. Plugins
must not depend on the internal handler implementation, default retry behavior,
Happy Eyeballs configuration, or default headers of a Jellyfin-provided named
client. Those details can change with the host. A plugin should configure the
behavior it requires on its own named or typed client and should not create raw
`HttpClient` instances for individual requests.

## 3. Jellyfin 12 image request pipeline

### 3.1 Image controller

`Jellyfin.Api.Controllers.ImageController` exposes item image routes including:

- `GET` and `HEAD` `/Items/{itemId}/Images/{imageType}`;
- indexed item-image routes;
- a route containing tag, format, size, percent-played, and unplayed-count
  parameters;
- analogous routes for several item-by-name image types.

The item image request passes through Jellyfin's HTTP authentication pipeline,
and the action resolves the item with the current user:

```csharp
_libraryManager.GetItemById<BaseItem>(itemId, User.GetUserId())
```

The action then calls an internal image path which obtains the image metadata and
calls `IImageProcessor.ProcessImage(ImageProcessingOptions)`.

### 3.2 Core image processing

`MediaBrowser.Controller.Drawing.IImageProcessor` is public and includes:

```csharp
Task<(string Path, string? MimeType, DateTime DateModified)>
    ProcessImage(ImageProcessingOptions options)
```

`ImageProcessingOptions` includes the source image, dimensions, output format
selection, quality, blur, background/foreground options, and Jellyfin's built-in
unplayed/percent-played overlays. It has no extension collection or arbitrary
badge/drawing callback.

Jellyfin registers `Jellyfin.Drawing.ImageProcessor` as one core singleton
implementation of `IImageProcessor`. That implementation is sealed. It returns
the original image or a path in Jellyfin's `resized-images` cache and incorporates
the built-in processing options into its own cache key.

The public interface is therefore a service API, but service replacement or
decoration is not a documented plugin extension contract. It would affect the
global image hot path and depend on core implementation behavior.

### 3.3 Response headers and conditional requests

`ImageController` sets response headers after `ProcessImage` returns. When a
request supplies an image tag, Jellyfin emits a tag-based ETag and may return
`304 Not Modified` when `If-None-Match` matches. It also emits `Last-Modified`
and cache-control behavior based on the image tag and processing result. The
action returns a `PhysicalFileResult` for an image body when no conditional
response short-circuits it.

Consequences for a response interceptor:

- an interceptor that short-circuits before the action must preserve Jellyfin's
  user-scoped authorization and image existence checks itself;
- an action filter may observe a `304` result instead of image bytes;
- a transformed response cannot safely retain an ETag representing only the
  original artwork;
- changing external badge metadata does not change Jellyfin's artwork tag;
- the interceptor must own or explicitly reject transformed conditional, range,
  `HEAD`, and cache behavior.

## 4. Supported artwork provider APIs

### 4.1 `IImageProvider`

`MediaBrowser.Controller.Providers.IImageProvider` is the base supported provider
contract. It contains only `Name` and `Supports(BaseItem)`.

The specialized contracts are:

- `IRemoteImageProvider`: discovers remote image candidates and downloads image
  responses during refresh;
- `ILocalImageProvider`: discovers local image files during refresh;
- `IDynamicImageProvider`: generates an image response during refresh.

These are image acquisition/provider contracts, not per-request response
decorators. They participate in Jellyfin's provider manager and image refresh
pipeline.

### 4.2 `IDynamicImageProvider`

`IDynamicImageProvider` provides:

```csharp
IEnumerable<ImageType> GetSupportedImages(BaseItem item)
Task<DynamicImageResponse> GetImage(
    BaseItem item,
    ImageType type,
    CancellationToken cancellationToken)
```

`DynamicImageResponse` can describe a path, stream, protocol, format, and image
presence. During `ItemImageProvider.RefreshImages`, Jellyfin calls the provider
only when the image is absent or replacement has been requested. A response with
a stream or local path is passed to `IProviderManager.SaveImage`; an HTTP path can
be attached as an image reference.

This is a confirmed supported refresh-time mechanism. It does not run for every
image request and does not receive the already selected poster as an input. It
cannot provide a non-persistent overlay over an existing poster.

### 4.3 Persisted image APIs

`IProviderManager.SaveImage`, `BaseItem.SetImage`/`SetImagePath`, and the item
image update flow are confirmed supported mechanisms for changing an item's
artwork. They update the artwork that Jellyfin serves normally.

This approach:

- works for clients using Jellyfin's normal image routes;
- keeps Jellyfin authentication and authorization on those requests;
- lets Jellyfin generate normal image tags and conditional responses after the
  artwork changes;
- requires a generated image to be stored as item artwork or referenced as an
  image;
- changes the effective primary artwork and therefore does not preserve the
  original without a separate backup/restoration design.

It is not a per-request overlay contract. For ArrTags, the selected architecture
accepts the active-artwork change and preserves the original through a separate
backup/restoration design.

## 5. Approach comparison

| Approach | API status | Existing poster preserved | Client coverage | Auth remains with Jellyfin | ETags/304 | Enhanced coexistence | Generated disk write | Stability/dependency |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `IRemoteImageProvider`, `ILocalImageProvider` | Confirmed supported API | No overlay; provider selects/adds artwork | Normal Jellyfin image clients after refresh | Yes for later image requests | Core handles the resulting artwork | Independent, but changed artwork can still receive Enhanced overlays | Usually refresh-time storage or image reference | Public provider contract; not an overlay hook |
| `IDynamicImageProvider` | Confirmed supported API | No; it supplies refresh-time artwork | Normal Jellyfin image clients after refresh | Yes for later image requests | Core handles the resulting artwork | Independent, but Web may add client overlays | Yes for stream/local responses through `SaveImage` | Public provider contract; refresh semantics are the limitation |
| `IImageProcessor` decoration/replacement | Public interface; unsupported workaround for overlays | Potentially | Potentially all image clients | Core action can handle auth if the request reaches it | Cannot safely add external metadata to the controller's ETag contract | Global interaction with Enhanced and all image processing | Core may write resized-image cache; plugin behavior varies | Depends on sealed core implementation and global DI registration |
| `IImageEncoder` replacement | Public interface; unsupported workaround for overlays | Potentially | Potentially all image clients | Core action can handle auth if the request reaches it | Core owns validator behavior | Global interaction with every encoded image | Core resized-image cache | Singular core encoder; affects every image and is not badge-specific |
| ASP.NET Core middleware | Public framework API; unsupported Jellyfin image hook | Yes if only the response is changed | All clients reaching the intercepted route | Only if it calls the pipeline; a short-circuit must implement authorization | Must reimplement or override Jellyfin validators, ranges, and conditional behavior | Ordering with Enhanced filters is not a supported contract | Optional plugin cache; not required | Depends on route and pipeline internals |
| ASP.NET Core MVC action filter | Public framework API; unsupported Jellyfin image hook | Yes if only the response is changed | All clients reaching the intercepted action | Authorization filters run, but item authorization inside the action requires the action to execute | The action can already return `304`; transformed validators need separate handling | Filter order with Enhanced is not a supported contract | Optional plugin cache; not required | Depends on controller actions, result types, and MVC configuration |
| New plugin image controller/route | Confirmed supported plugin route mechanism | Yes | Only clients explicitly using the new route | Yes if the controller uses Jellyfin authorization | Fully controllable for the new route | Does not alter Enhanced's existing image requests | Optional plugin cache; not required | Route is supported, but client adoption is required |
| Persisted artwork via `SaveImage`/item image APIs | Confirmed supported API | Not without backup/restoration | All normal image clients | Yes on normal image requests | Jellyfin handles updated artwork tags | Enhanced may overlay on the changed image; no private dependency | Yes or an attached image reference | Public artwork contract; semantics are intentionally persistent |
| Jellyfin Enhanced-style client overlay | Enhanced-specific/private client mechanism | Yes | Web and embedded-web clients; not native TV/Kodi/other clients | Data endpoints and image requests can use auth, but DOM rendering is client-side | Original image validators remain unchanged | Reusing Enhanced internals is unsupported; independent overlays can duplicate | No poster write required; caches may be persisted by the client/plugin | Depends on jellyfin-web DOM, JavaScript, and Enhanced internals |

## 6. Classification of the candidate mechanisms

### 6.1 Confirmed supported API

The following are confirmed supported mechanisms in Jellyfin 12:

- `IPluginServiceRegistrator` for plugin service registration;
- plugin `ControllerBase` types as additional API routes;
- `IImageProvider` and its remote/local provider variants for artwork
  discovery/acquisition;
- `IDynamicImageProvider` for refresh-time generated artwork;
- `IProviderManager.SaveImage` and item image update APIs for persisted artwork;
- the normal Jellyfin image routes and image processing when a client requests
  stored artwork, with Jellyfin's existing request authentication and
  user-scoped item resolution remaining in that path.

None of these provides a supported, non-persistent, per-request badge overlay
over an existing poster.

### 6.2 Public but potentially unstable API

The following are public APIs but do not establish a supported ArrTags overlay
contract:

- `IImageProcessor`;
- `IImageEncoder`;
- ASP.NET Core `IStartupFilter`;
- ASP.NET Core `IAsyncActionFilter` and `MvcOptions` configuration;
- ASP.NET Core response/result types used to replace image results.

The framework APIs are public and usable by plugins, but the Jellyfin-specific
assumptions needed to identify image actions, preserve authorization, access
processed image output, and replace validators are not promised contracts.

### 6.3 Internal implementation detail

The following are implementation details rather than plugin extension points:

- `Jellyfin.Drawing.ImageProcessor` being the current `IImageProcessor`
  implementation;
- its sealed implementation, singleton registration, cache-file naming, and
  `resized-images` layout;
- `ImageController` action names, route variants, private helper flow, result
  types, and header order;
- the ordering between Jellyfin filters and filters registered by other plugins;
- the internal Skia encoder drawing implementation.

### 6.4 Unsupported workaround

These are technically possible but should be described as workarounds, not
supported Jellyfin image APIs:

- replacing or decorating the global `IImageProcessor` or `IImageEncoder`;
- short-circuiting image requests from middleware before Jellyfin's action;
- replacing `ImageController` results with an action filter and assuming its
  route/result behavior is stable;
- depending on Jellyfin Enhanced's DOM, JavaScript globals, configuration, or
  private endpoints;
- claiming a custom plugin route replaces the native Jellyfin image route.

## 7. Viable architectural choices

No single confirmed supported Jellyfin 12 extension point satisfies all of the
following simultaneously: preserve the original artwork, render at request
time, work for every image client, and use the normal Jellyfin image endpoints.

The technically viable choices are:

1. **Supported persisted artwork:** Use the provider/save-image APIs. This is
   the strongest Jellyfin API choice and has normal client, authorization, and
   validator behavior, but it changes stored artwork and needs a reliable
   original-artwork backup/restoration policy.
2. **Client-side overlay:** Use a web/client overlay pattern. This preserves
   artwork and avoids server image interception, but it is limited to Web and
   embedded-web clients and cannot satisfy native-client parity.
3. **Framework response interception:** Use middleware or MVC filters. This can
   preserve stored artwork and reach all clients, but it is an unsupported
   Jellyfin-specific workaround requiring version-pinned compatibility testing,
   authorization preservation, and ownership of transformed HTTP caching.
4. **Separate supported plugin route:** Add a custom authenticated image route.
   This is a supported route mechanism and can implement correct validators, but
   clients must be taught to request it; it does not transparently augment
   Jellyfin's native image URLs.

The source evidence does not justify selecting one of these choices here.

## 8. Source references

Jellyfin `v12.0` source:

- [`IPluginServiceRegistrator`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs)
- [`ApplicationHost`](https://github.com/jellyfin/jellyfin/blob/v12.0/Emby.Server.Implementations/ApplicationHost.cs)
- [`PluginManager`](https://github.com/jellyfin/jellyfin/blob/v12.0/Emby.Server.Implementations/Plugins/PluginManager.cs)
- [`Startup`](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Server/Startup.cs)
- [`NamedClient`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Common/Net/NamedClient.cs)
- [`ApiServiceCollectionExtensions`](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs)
- [`ImageController`](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Api/Controllers/ImageController.cs)
- [`IImageProcessor`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Drawing/IImageProcessor.cs)
- [`ImageProcessingOptions`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Drawing/ImageProcessingOptions.cs)
- [`ImageProcessor`](https://github.com/jellyfin/jellyfin/blob/v12.0/src/Jellyfin.Drawing/ImageProcessor.cs)
- [`IImageEncoder`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Drawing/IImageEncoder.cs)
- [`CoreAppHost`](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Server/CoreAppHost.cs)
- [`IImageProvider`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/IImageProvider.cs)
- [`IRemoteImageProvider`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/IRemoteImageProvider.cs)
- [`ILocalImageProvider`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/ILocalImageProvider.cs)
- [`IDynamicImageProvider`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/IDynamicImageProvider.cs)
- [`DynamicImageResponse`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/DynamicImageResponse.cs)
- [`ItemImageProvider`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Providers/Manager/ItemImageProvider.cs)
- [`IProviderManager`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Providers/IProviderManager.cs)

Project research:

- [`poster-rendering-strategies.md`](poster-rendering-strategies.md)
- [`architecture.md`](architecture.md)
