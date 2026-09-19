using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Common.Api;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.1 confirmation of the pinned Jellyfin 12.0.0
/// <c>Jellyfin.Api.Controllers.ImageController</c> route variants and their
/// authorization attributes. <c>Jellyfin.Api.dll</c> is a host application
/// assembly and is not a NuGet package, so the exact pinned host install
/// directory is supplied through <c>ARRTAGS_JELLYFIN_HOST_DIR</c>. When it is
/// unset the facts are reported as skipped; when it is set the pinned assembly
/// must be present and loadable, otherwise the tests fail rather than silently
/// passing. The route templates are also recorded in
/// <c>docs/research/jellyfin-12-architecture.md</c>.
/// </summary>
public class JellyfinImageRouteTests
{
    private const string ControllerTypeName = "Jellyfin.Api.Controllers.ImageController";

    [JellyfinHostFact]
    public void ItemImageReadRoutesAndVerbsArePinned()
    {
        var controller = LoadImageController();
        var routes = ReadRoutes(controller);

        Assert.Contains(("GetItemImage", "Items/{itemId}/Images/{imageType}", "HttpGet"), routes);
        Assert.Contains(("GetItemImage", "Items/{itemId}/Images/{imageType}", "HttpHead"), routes);
        Assert.Contains(("GetItemImageByIndex", "Items/{itemId}/Images/{imageType}/{imageIndex}", "HttpGet"), routes);
        Assert.Contains(("GetItemImageByIndex", "Items/{itemId}/Images/{imageType}/{imageIndex}", "HttpHead"), routes);
        Assert.Contains(("GetItemImageInfos", "Items/{itemId}/Images", "HttpGet"), routes);
        Assert.Contains(
            (
                "GetItemImage2",
                "Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}",
                "HttpGet"),
            routes);
        Assert.Contains(
            (
                "GetItemImage2",
                "Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}",
                "HttpHead"),
            routes);
    }

    [JellyfinHostFact]
    public void ItemImageWriteRoutesAndVerbsArePinned()
    {
        var controller = LoadImageController();
        var routes = ReadRoutes(controller);

        Assert.Contains(("SetItemImage", "Items/{itemId}/Images/{imageType}", "HttpPost"), routes);
        Assert.Contains(("SetItemImageByIndex", "Items/{itemId}/Images/{imageType}/{imageIndex}", "HttpPost"), routes);
        Assert.Contains(("DeleteItemImage", "Items/{itemId}/Images/{imageType}", "HttpDelete"), routes);
        Assert.Contains(("DeleteItemImageByIndex", "Items/{itemId}/Images/{imageType}/{imageIndex}", "HttpDelete"), routes);
        Assert.Contains(("UpdateItemImageIndex", "Items/{itemId}/Images/{imageType}/{imageIndex}/Index", "HttpPost"), routes);
    }

    [JellyfinHostFact]
    public void ItemImageReadActionsCarryNoMethodLevelAuthorizeAttribute()
    {
        var controller = LoadImageController();

        // Confirmed from the pinned 12.0.0 artifact and the live host: the
        // unindexed/indexed GET/HEAD item-image actions are not decorated with
        // [Authorize]. They still resolve the item user-scoped through
        // ILibraryManager.GetItemById(itemId, User.GetUserId()), so anonymous
        // requests are rejected by item visibility rather than by an
        // authorization policy. Later Phase 5 tasks must not assume a 401.
        foreach (var name in new[] { "GetItemImage", "GetItemImageByIndex", "GetItemImage2" })
        {
            var method = FindMethod(controller, name);
            Assert.NotNull(method);
            Assert.DoesNotContain(
                method!.GetCustomAttributesData(),
                a => a.AttributeType.FullName == "Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
        }
    }

    [JellyfinHostFact]
    public void ItemImageInfoAndWriteActionsCarryAuthorizeAttributes()
    {
        var controller = LoadImageController();

        var infos = FindMethod(controller, "GetItemImageInfos");
        Assert.NotNull(infos);
        Assert.Contains(
            infos!.GetCustomAttributesData(),
            a => a.AttributeType.FullName == "Microsoft.AspNetCore.Authorization.AuthorizeAttribute");

        foreach (var name in new[] { "SetItemImage", "SetItemImageByIndex", "DeleteItemImage", "DeleteItemImageByIndex" })
        {
            var method = FindMethod(controller, name);
            Assert.NotNull(method);

            var authorize = method!.GetCustomAttributesData()
                .SingleOrDefault(a => a.AttributeType.FullName == "Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
            Assert.NotNull(authorize);

            var policy = authorize!.NamedArguments
                .SingleOrDefault(a => a.MemberName == "Policy")
                .TypedValue.Value as string;
            Assert.Equal(Policies.RequiresElevation, policy);
        }
    }

    [JellyfinHostFact]
    public void ImageControllerIsThePinnedJellyfinApiRouteController()
    {
        var controller = LoadImageController();

        Assert.Equal("Jellyfin.Api.Controllers", controller.Namespace);
        var route = controller.GetCustomAttributesData()
            .SingleOrDefault(a => a.AttributeType.FullName == "Microsoft.AspNetCore.Mvc.RouteAttribute");
        Assert.NotNull(route);
        Assert.Equal(string.Empty, route!.ConstructorArguments[0].Value);
    }

    private static Type LoadImageController()
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
        var controller = assembly.GetType(ControllerTypeName, throwOnError: false);
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

    private static List<(string Action, string Template, string Verb)> ReadRoutes(Type controller)
    {
        var routes = new List<(string Action, string Template, string Verb)>();

        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var attribute in method.GetCustomAttributesData())
            {
                var verb = attribute.AttributeType.Name switch
                {
                    "HttpGetAttribute" => "HttpGet",
                    "HttpHeadAttribute" => "HttpHead",
                    "HttpPostAttribute" => "HttpPost",
                    "HttpDeleteAttribute" => "HttpDelete",
                    _ => null,
                };

                if (verb is null)
                {
                    continue;
                }

                foreach (var argument in attribute.ConstructorArguments)
                {
                    if (argument.Value is string template)
                    {
                        routes.Add((method.Name, template, verb));
                    }
                }
            }
        }

        return routes;
    }

    private static MethodInfo? FindMethod(Type type, string name)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == name);
    }
}
