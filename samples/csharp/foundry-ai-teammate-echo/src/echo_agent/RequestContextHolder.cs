using System.Diagnostics;

namespace EchoAgent;

/// <summary>
/// Holds the last HTTP request's ActivityContext so that the OnActivity handler
/// (which runs on a background async context without HttpContext) can re-parent
/// its spans under the incoming platform request.
/// </summary>
internal static class RequestContextHolder
{
    public static ActivityContext? LastContext;
}
