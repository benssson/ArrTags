using System;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArrTags.Diagnostics;

/// <summary>
/// The read-only diagnostics status boundary (ADR-025 clause 1). Jellyfin
/// discovers exported <see cref="ControllerBase"/> types in plugin assemblies as
/// independently routed API controllers, so this controller is registered with
/// the host without any ArrTags-specific registration. It is gated at class
/// level by the host's administrator elevation policy, so only an authenticated
/// administrator can read it. It exposes exactly one read-only route and never
/// mutates configuration, work, or artwork: the action resolves the bounded,
/// secret-free <see cref="DiagnosticsSnapshot"/> from the singleton
/// <see cref="DiagnosticsSnapshotProvider"/> and returns it unchanged.
/// </summary>
/// <remarks>
/// The response is the fixed-shape snapshot with a bounded number of fields and
/// no per-item array: counts and bounded enums only, never a path, item name,
/// item identifier list, provider payload, credential, secret value, or
/// unbounded collection. The controller adds no write action, no configuration
/// setting, and no route outside the elevation-gated read route (ADR-025
/// clauses 1, 3, 4, and 5).
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route(RoutePrefix)]
public sealed class ArrTagsStatusController : ControllerBase
{
    /// <summary>
    /// The route prefix for the read-only diagnostics status endpoint.
    /// </summary>
    public const string RoutePrefix = "ArrTags/Status";

    private readonly DiagnosticsSnapshotProvider _snapshotProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsStatusController"/> class.
    /// </summary>
    /// <param name="snapshotProvider">The read-only diagnostics snapshot provider.</param>
    /// <exception cref="ArgumentNullException">The snapshot provider is <see langword="null"/>.</exception>
    public ArrTagsStatusController(DiagnosticsSnapshotProvider snapshotProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    /// <summary>
    /// Returns the current bounded, secret-free diagnostics snapshot. The action
    /// is read-only: it reads the in-memory snapshot and performs no provider,
    /// render, library, I/O, or mutation work.
    /// </summary>
    /// <returns>The fixed-shape diagnostics snapshot.</returns>
    [HttpGet]
    public ActionResult<DiagnosticsSnapshot> GetStatus()
    {
        return _snapshotProvider.GetSnapshot();
    }
}
