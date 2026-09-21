# Jellyfin 12 Extension-Point Findings

**Status:** Research, Jellyfin `v12.0`

**Source revision:** `6c073e19ddf604b2369c638716164fdab4c952dc`

**Purpose:** Establish which Jellyfin 12 plugin and framework extension points
can provide, replace, or augment item artwork. This document records source
findings as **evidence and reference material** and does not select the ArrTags
architecture or define an implementation phase sequence. The authoritative V1
architecture is [`../architecture.md`](../architecture.md); accepted and rejected
choices are recorded in [`../decisions.md`](../decisions.md).

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

The exact pinned `12.0.0` route templates and action names (task 5.1) are:

| Action | Method(s) | Route template |
| --- | --- | --- |
| `GetItemImage` | `GET`, `HEAD` (`HeadItemImage`) | `Items/{itemId}/Images/{imageType}` |
| `GetItemImageByIndex` | `GET`, `HEAD` (`HeadItemImageByIndex`) | `Items/{itemId}/Images/{imageType}/{imageIndex}` |
| `GetItemImage2` | `GET`, `HEAD` (`HeadItemImage2`) | `Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}` |
| `GetItemImageInfos` | `GET` | `Items/{itemId}/Images` |
| `SetItemImage` | `POST` | `Items/{itemId}/Images/{imageType}` |
| `SetItemImageByIndex` | `POST` | `Items/{itemId}/Images/{imageType}/{imageIndex}` |
| `DeleteItemImage` | `DELETE` | `Items/{itemId}/Images/{imageType}` |
| `DeleteItemImageByIndex` | `DELETE` | `Items/{itemId}/Images/{imageType}/{imageIndex}` |
| `UpdateItemImageIndex` | `POST` | `Items/{itemId}/Images/{imageType}/{imageIndex}/Index` |

`ImageController` directly declares `[Route("")]`. Its base type
`BaseJellyfinApiController` declares `[Route("[controller]")]`, and
`RouteAttribute.Inherited` is `true`, so the controller's own `[Route("")]`
overrides the inherited controller-prefixed base route and the effective paths
are the root-relative templates listed above. The unindexed and
indexed read actions additionally accept `maxWidth`, `maxHeight`, `width`,
`height`, `quality`, `fillWidth`, `fillHeight`, `tag`, `format`,
`percentPlayed`, `unplayedCount`, `blur`, `backgroundColor`, and
`foregroundLayer` as query parameters. `GetItemImage2` fixes `tag`, `format`,
`maxWidth`, `maxHeight`, `percentPlayed`, and `unplayedCount` in the path. The
route/action names are not treated as a stable plugin contract (see section 6.3);
the standard image **URLs** are what clients and later Phase 5 verification use.

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

The exact pinned `12.0.0` behavior (task 5.1) in
`ImageController.GetImageResult` is:

- `Response.ContentType` is the `IImageProcessor` MIME type (or
  `text/plain`); `Content-Disposition` is `attachment`; `Age`, `Vary: Accept`,
  and the DLNA headers are always set.
- With a `tag` query parameter, `Cache-Control: public, max-age=31536000,
  immutable` (365 days) is set, `Last-Modified` is emitted, and the ETag is the
  quoted tag. A matching `If-None-Match` (quoted or bare) returns `304`.
- Without a tag, `Cache-Control: public` is set and `If-Modified-Since` is
  honored against the processed file's modified time.
- A client `Cache-Control: no-cache` forces
  `no-cache, no-store, must-revalidate` plus `Pragma`.
- Resizing/format: `ImageProcessingOptions` is built from the query
  (`Width`/`Height` fixed, `MaxWidth`/`MaxHeight` bounding, `FillWidth`/
  `FillHeight` box fill, `Quality`, `Blur`, `BackgroundColor`, `ForegroundLayer`
  opacity, `PercentPlayed`, `UnplayedCount`). `ImageHelper.GetNewImageSize`
  applies `DrawingUtils.ScaleDownToFit`, so Jellyfin **never upscales** beyond
  the source dimensions. `Jellyfin.Drawing.ImageProcessor` is the sealed core
  singleton that owns the `resized-images` cache and incorporates all options
  and a version char into its cache key.

Authorization: the unindexed/indexed GET/HEAD read actions carry **no**
`[Authorize]` attribute in the pinned `12.0.0` source. They resolve the item
through `_libraryManager.GetItemById<BaseItem>(itemId, User.GetUserId())`. For
an anonymous request `ClaimsPrincipalExtensions.GetUserId()` returns
`default(Guid)`, `LibraryManager.GetItemById<T>(Guid, Guid)` maps an empty user
id to a **null user**, and `LibraryManager.ItemIsVisible(item, null)` returns
`true` for any non-null item. A known item's image is therefore served
**anonymously** (HTTP 200); only an unknown item id (or an item with no image of
the requested type) yields `404`. The item-image information action
(`GET Items/{itemId}/Images`) and all write actions (`POST`/`DELETE`) do carry
`[Authorize]`/`[Authorize(Policy = RequiresElevation)]`. The route/authorization
status observations are **live-confirmed** on the pinned host (an anonymous
`GET /Items/{id}/Images/Primary` reached the action and returned `404` for the
unknown id, while `GET /Items/{id}/Images` and
`POST /Items/{id}/Images/Primary` returned `401`), and the pinned host OpenAPI
document shows `security=null` for the GET/HEAD item-image operations. Later
Phase 5 tasks must not assume a `401` from the read image route and must not
treat the route as an authorization boundary. Jellyfin still owns the delivery
path's caching and resizing; ArrTags must not add a parallel route or response
validator (ADR-001).

Consequences for a response interceptor:

- an interceptor that short-circuits before the action must preserve Jellyfin's
  item-resolution, item-existence, and image-existence checks (including the
  user-scoped behavior for authenticated callers) itself;
- an action filter may observe a `304` result instead of image bytes;
- a transformed response cannot safely retain an ETag representing only the
  original artwork;
- changing external badge metadata does not change Jellyfin's artwork tag;
- the interceptor must own or explicitly reject transformed conditional, range,
  `HEAD`, and cache behavior.

**Task 5.11 in-process route/response confirmation.** The pinned route, cache,
conditional-request, size/format, and pass-through behavior above was exercised
in-process against the pinned `Jellyfin.Api.dll`
`Jellyfin.Api.Controllers.ImageController` by
`tests/ArrTags.Tests/JellyfinImageResponseTests.cs`: the real `GetItemImage`,
`GetItemImageByIndex`, and `GetItemImage2` actions are invoked with a real
`DefaultHttpContext` and `DispatchProxy` doubles for `ILibraryManager`,
`IProviderManager`, and `IImageProcessor`. The facts assert the unindexed,
indexed, and path-form `Primary` routes deliver the server-rendered
`PhysicalFileResult` (path and content type); the quoted
image-tag `ETag`, `Cache-Control: public, max-age=31536000, immutable`,
`Last-Modified`, `Vary: Accept`, `Content-Disposition: attachment`, and DLNA
headers; `304` for a matching quoted/bare `If-None-Match` and for
`If-Modified-Since`; the `no-cache` revalidation headers; the requested
size/format plumbing; the never-upscale clamp; and the `404` pass-through. The
route templates and the read/write authorization split are separately pinned by
the task 5.1 `JellyfinImageRouteTests` over the same host assembly. A
publish-then-read-back case uses the real `JellyfinArtworkImageWriter` (with a
provider double that mirrors the supported `ImageSaver` write-and-update flow)
and then serves the derived bytes through the real standard route. A **live HTTP
round-trip against a running Jellyfin server was not performed** by task 5.11;
the running-host on-disk representation and a live end-to-end read-back remain
in the "Still needing live-host validation" list below.

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

Jellyfin does not add plugin ownership metadata to this persisted image. The
`ImageInfo.ImageTag` exposed by the item-image information endpoint is a
Jellyfin representation/cache validator; in the v12 implementation it is based
on item path and image modification time, not on the actor or a content hash.
ArrTags must therefore retain its own source artifact and active content
identity. The selected ownership and restoration contract is recorded in
`docs/decisions.md` ADR-002; equality of the Jellyfin image tag alone is not a
safe restoration condition.

It is not a per-request overlay contract. For ArrTags, the selected architecture
accepts the active-artwork change and preserves the original through a separate
backup/restoration design.

### 4.4 Confirmed Jellyfin 12.0.0 item-image publication and read ABI (task 5.1)

**Status:** Confirmed against the pinned Jellyfin `12.0.0` artifacts. Task 5.1
pins the exact publication/read ABI and the standard item-image route variants
that the later ABI-dependent Phase 5 tasks rely on. This section confirms an
ABI; it does not reopen ADR-001, ADR-002, ADR-003, ADR-006, or ADR-009.

Evidence sources for this subsection, all at Jellyfin source revision
`6c073e19ddf604b2369c638716164fdab4c952dc` (tag `v12.0`) unless noted:

- The pinned NuGet assemblies `MediaBrowser.Controller.dll` and
  `MediaBrowser.Model.dll` `12.0.0` (`~/.nuget/packages/jellyfin.controller/`
  and `jellyfin.model/`), which are the assemblies the plugin compiles against.
- The pinned host install at `/tmp/opencode/jf/jellyfin`
  (`Jellyfin.Api.dll` `12.0.0.0`, `MediaBrowser.Controller.xml`,
  `MediaBrowser.Model.xml`).
- The pinned host source extract for `Jellyfin.Api/Controllers/ImageController.cs`,
  `MediaBrowser.Providers/Manager/ImageSaver.cs`,
  `MediaBrowser.Providers/Manager/ProviderManager.cs`, and
  `Emby.Server.Implementations/Library/LibraryManager.cs`.
- A live pinned-host probe on Jellyfin `12.0.0` (the Phase 1 portable host) for
  the route/authorization observations marked **live-confirmed**.

The automated confirmation is
`tests/ArrTags.Tests/JellyfinImageAbiTests.cs` (unguarded; reflects the pinned
NuGet assemblies) and `tests/ArrTags.Tests/JellyfinImageRouteTests.cs`
(guarded by `ARRTAGS_JELLYFIN_HOST_DIR`; reflects the pinned host
`Jellyfin.Api.dll`).

#### Publication API surface (confirmed from the pinned 12.0.0 assembly)

- Interface: `MediaBrowser.Controller.Providers.IProviderManager`
  (`MediaBrowser.Controller.dll` `12.0.0`).
- The V1 stream overload, confirmed signature:

  ```csharp
  Task SaveImage(
      BaseItem item,
      Stream source,
      string mimeType,
      ImageType type,
      int? imageIndex,
      CancellationToken cancellationToken);
  ```

- Also present and confirmed:

  ```csharp
  Task SaveImage(BaseItem item, string url, ImageType type, int? imageIndex, CancellationToken cancellationToken);
  Task SaveImage(BaseItem item, string source, string mimeType, ImageType type, int? imageIndex, bool? saveLocallyWithMedia, CancellationToken cancellationToken);
  Task SaveImage(Stream source, string mimeType, string path);
  ```

- Parameter meaning (from the pinned XML docs and
  `MediaBrowser.Providers/Manager/ProviderManager.cs` /
  `ImageSaver.cs`): `item` is the target `BaseItem`; `source` is the image byte
  stream; `mimeType` selects the file extension (an empty MIME type throws);
  `type` is the `ImageType` surface; `imageIndex` is `int?` and defaults to `0`
  for single-image surfaces (`null` is only auto-incremented for
  `AllowsMultipleImages` types: `Backdrop` and `Chapter`). `ImageSaver` writes
  the bytes and then calls `item.SetImagePath(...)`; the caller must still run
  the item update flow (below).
- **Save-locally semantics (confirmed, relevant to the V1 "do not write
  media-folder artwork" rule).** The stream overload has no
  `saveLocallyWithMedia` parameter. `ImageSaver` computes `saveLocally` from
  `item.SupportsLocalMetadata && item.IsSaveLocalMetadataEnabled()` (the
  library's `SaveLocalMetadata` option) and then writes to the media folder
  (`poster.png` next to the movie/episode) when it is enabled; it otherwise
  writes under the item's internal metadata path. Only the filesystem-path
  overload exposes `bool? saveLocallyWithMedia`, and passing `false` forces the
  internal metadata path. That overload also deletes the source file after
  saving (the pinned XML doc: "This method will remove the image on the source
  path after saving it to the destination"), so it requires a plugin-owned
  temporary file. Later Phase 5 tasks must choose the overload and the
  `saveLocallyWithMedia` value that keeps V1 from writing media-folder artwork;
  the exact host representation and read-back remain implementation validation.
  **V1 choice (task 5.5):** ArrTags uses the stream overload with the durable
  derived PNG bytes and never the filesystem-path overload, because the path
  overload deletes its plugin-owned source file. ArrTags therefore does not
  select or write the destination path; Jellyfin's `ImageSaver` owns it, and on a
  library with `SaveLocalMetadata` enabled Jellyfin itself stores the published
  bytes in the media folder through its supported API. This is Jellyfin's
  supported behavior, not a direct ArrTags media-folder or cache write.
- `ProviderManager.SaveImage(...)` is registered as the singleton
  `IProviderManager` in `ApplicationHost` (`AddSingleton<IProviderManager,
  ProviderManager>()`), so the interface is a confirmed supported plugin
  resolution target. The implementation type is not a plugin contract; ArrTags
  consumes the interface only.

#### Item-image read/information surface (confirmed)

- `BaseItem.GetImageInfo(ImageType imageType, int imageIndex)` returns
  `MediaBrowser.Controller.Entities.ItemImageInfo?` and is the supported
  in-memory read of the current image identity.
- `BaseItem.ImageInfos` (`ItemImageInfo[]`) is the current image set;
  `BaseItem.GetImagePath`, `HasImage`, `GetImageIndex`, and `GetImages` are the
  related read helpers.
- `ItemImageInfo` (`MediaBrowser.Controller.Entities`) carries `Path`,
  `Type` (`ImageType`), `DateModified`, `Width`, `Height`, `BlurHash`, and the
  `[JsonIgnore] IsLocalFile` flag (`Path` not starting with `http`).
- `ImageInfo` (`MediaBrowser.Model.Dto`) is the API DTO returned by the
  item-image information endpoint and carries `ImageType`, `ImageIndex`,
  `ImageTag`, `Path`, `BlurHash`, `Width`, `Height`, and `Size`.
- `IImageProcessor.GetImageCacheTag(BaseItem, ItemImageInfo)` produces the
  `ImageTag`; in the pinned 12.0.0 implementation it is the MD5 of
  `(item.Path + image.DateModified.Ticks)`. It is a representation/cache
  validator, not a content hash or ownership token (ADR-002).
- Dimensions are observable from `ItemImageInfo.Width`/`Height` (0 until
  resolved) or through `IImageProcessor.GetImageDimensions(...)`. Bytes are
  observable by reading the image through the normal item-image route or the
  resolved path; the supported postcondition identity ArrTags persists is its
  own content hash (ADR-002), not the Jellyfin tag.
- `ILibraryManager.UpdateImagesAsync(BaseItem, bool forceUpdate = false)`
  refreshes dimensions/hashes; `ILibraryManager.ConvertImageToLocal(BaseItem,
  ItemImageInfo, int, bool removeOnFailure = true)` is the supported conversion
  for a non-local image.
- `ILibraryManager.GetItemById<T>(Guid id, Guid userId)` is the user-scoped
  resolution used by the image controller. An empty `userId` (the anonymous
  case) maps to a null user and therefore applies no visibility filter; a real
  user id resolves the item under that user's visibility.

#### Item repository update (confirmed)

- `BaseItem.UpdateToRepositoryAsync(ItemUpdateType, CancellationToken)` is the
  normal item update flow. `ItemUpdateType` (`MediaBrowser.Controller.Library`)
  is a flags enum; `ImageUpdate = 4`. The controller's write actions call
  `SaveImage` and then `UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ...)`.
  Later Phase 5 tasks must persist the item update after `SaveImage`; the
  publication protocol is ADR-003.

#### Enum/type locations (confirmed)

| Type | Namespace | Assembly | Notes |
| --- | --- | --- | --- |
| `ImageType` | `MediaBrowser.Model.Entities` | `MediaBrowser.Model` | `Primary = 0`, `Art = 1`, `Backdrop = 2`, `Banner = 3`, `Logo = 4`, `Thumb = 5`, `Disc = 6`, `Box = 7`, `Screenshot = 8`, `Menu = 9`, `Chapter = 10`, `BoxRear = 11`, `Profile = 12`. |
| `ImageFormat` | `MediaBrowser.Model.Drawing` | `MediaBrowser.Model` | `Bmp`, `Gif`, `Jpg`, `Png`, `Webp`, `Svg`. |
| `ItemImageInfo` | `MediaBrowser.Controller.Entities` | `MediaBrowser.Controller` | Read surface; `Path` is `required`. |
| `ImageInfo` | `MediaBrowser.Model.Dto` | `MediaBrowser.Model` | API DTO. |
| `ItemUpdateType` | `MediaBrowser.Controller.Library` | `MediaBrowser.Controller` | `ImageUpdate = 4`. |

#### Selected V1 surfaces

Per ADR-006/ADR-009, V1 publishes only the **unindexed `Primary` poster** for
**Movie** and **Episode** items. `ImageType.Primary` with `imageIndex: null`
(or `0`) is the only image surface V1 renders or publishes. Indexed or alternate
poster surfaces (`Primary` with a non-zero index, `Thumb`, `Backdrop`, `Logo`,
`Banner`, `Art`, etc.) are explicitly **out of V1** and are not publication or
source-capture targets. `Series` and `Season` remain structural and are not
badge surfaces.

#### Source capture: container confinement and oriented dimensions (task 5.3)

The host source adapter added by task 5.3 reads the selected surface with the
confirmed read surface above: `BaseItem.GetImageInfo(ImageType.Primary, 0)`,
`ILibraryManager.ConvertImageToLocal(item, info, 0, removeOnFailure: false)` when
`ItemImageInfo.IsLocalFile` is false, and a bounded read of the resolved local
path. The evidence sources are the pinned 12.0.0 assemblies and the pinned
`Jellyfin.Api/Controllers/ImageController.cs` `GetImageInternal` read path, which
uses the same `ConvertImageToLocal` call.

Two host behaviors that the adapter must handle are pinned from the pinned
source:

- **Dimensions are pre-orientation.** `IImageProcessor.GetImageDimensions(item,
  info)` falls back to the file path and returns `IImageEncoder.GetImageSize`;
  the pinned `src/Jellyfin.Drawing.Skia/SkiaEncoder.cs` `GetImageSize`
  implementation returns `SKCodec.Info.Width`/`Height`, which are the encoded
  dimensions before EXIF orientation, while `SKCodec.EncodedOrigin` is reported
  separately. The renderer resolves orientation before layout and validates the
  supplied `SourceImageInput.OrientedWidth`/`OrientedHeight` against
  `SKCodec.EncodedOrigin` (`src/ArrTags/Rendering/SkiaBadgeRenderer.cs`). The
  adapter therefore derives the post-orientation display dimensions from the exact
  bytes with `SourceImageDescriptor` (the pinned `SkiaSharp 3.119.4` codec) and
  `SourceOrientationExtensions.OrientedDimensions`, rather than trusting a raw or
  cached Jellyfin dimension. This is fail-closed: bytes whose header cannot be
  understood are not handed to the renderer, so a wrong oriented dimension is
  never supplied. Item-type eligibility remains the caller's concern; this
  adapter enforces only the unindexed `Primary` surface.
- **Only PNG and JPEG containers are inspected for color profiles.** The
  renderer's `SourceColorProfile.Inspect` recognizes PNG `iCCP` and JPEG `APP2`
  profiles and returns `None` (treated as sRGB) for any other container. V1
  source capture therefore accepts only the `image/png` and `image/jpeg`
  signatures and fails closed for GIF, WebP, BMP, AVIF, and unrecognized bytes.
  This closes the carried-forward Phase 4 MEDIUM finding so a malformed profile
  in an uninspected container can never be silently treated as sRGB. The
  renderer's `SourceColorProfile` is unchanged.

The automated confirmation is `tests/ArrTags.Tests/ArtworkSourceReaderTests.cs`,
`tests/ArrTags.Tests/ArtworkSourceContentTypeTests.cs`,
`tests/ArrTags.Tests/JellyfinArtworkImageAccessTests.cs`, and
`tests/ArrTags.Tests/SourceImageDescriptorTests.cs`. The valid decode cases need
the pinned native runtime and are guarded like the other Skia tests.

#### Still needing live-host validation

The ABI names/signatures and route templates are confirmed from the pinned
artifacts. The following remain host-behavior validation for later Phase 5 tasks,
not ABI uncertainty:

- The exact on-disk representation `SaveImage` produces for the selected host
  configuration (local-metadata vs internal-metadata path) and the read-back
  content hash equality after publication (ADR-002/ADR-003). V1 uses the stream
  overload, which has no `saveLocallyWithMedia` parameter: Jellyfin's `ImageSaver`
  itself chooses the destination from the library's `SaveLocalMetadata` option
  (the media folder when enabled, otherwise the item's internal metadata path).
  ArrTags neither selects nor writes that path and does not use the
  filesystem-path overload, so the stream-overload storage representation and the
  post-publication read-back equality remain host behavior to validate.
- Multi-RID packaging and packaged-asset resolution (task 5.4).
- End-to-end standard-route delivery of a published derived image on a running
  host (task 5.11 exercises the pinned controller route/response pipeline and a
  real-writer publish-then-read-back in-process, but no live HTTP round-trip was
  performed).

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

ADR-001 selects supported persisted artwork for ArrTags. ADR-002 supplies the
separate source-artifact, active-identity, and guarded-restoration contract
required by that choice; this research section does not grant Jellyfin any
additional ownership guarantee.

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
- [`ImageInfo`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Model/Dto/ImageInfo.cs)
- [`ItemImageInfo`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/ItemImageInfo.cs)
- [`ImageSaver`](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Providers/Manager/ImageSaver.cs)
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
- [`architecture.md`](../architecture.md)
- [`decisions.md`](../decisions.md)
