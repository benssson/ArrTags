using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Rendering;

/// <summary>
/// The provider-neutral renderer boundary from ADR-010. The renderer has no
/// external side effects: it never opens a path, queries Jellyfin, reads a
/// provider service, or mutates the source bytes. Required arguments are guarded
/// in the value types; invalid or unsupported content is reported as a bounded
/// <see cref="RenderResult"/> rather than thrown.
/// </summary>
public interface IRenderer
{
    /// <summary>
    /// Renders one derived poster image from the supplied request.
    /// </summary>
    /// <param name="request">The validated provider-neutral render request.</param>
    /// <param name="cancellationToken">The token observed before decode, after decode, between layout stages, and before output finalization.</param>
    /// <returns>A bounded rendered, pass-through, or failed result.</returns>
    /// <exception cref="System.ArgumentNullException">The request is <see langword="null"/>.</exception>
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken);
}
