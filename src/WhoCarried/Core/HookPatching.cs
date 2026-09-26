using System.Reflection;

namespace WhoCarried.Core;

/// <summary>
/// Which of content's hooks can be watched with a Harmony patch without changing what they do. A hook declared in a
/// generic class (a mod's shared base, like one rune class per Form card built on <c>FormRune&lt;TCard&gt;</c>) runs as
/// one piece of machine code for every version of that class. Harmony builds its replacement for the one version it's
/// handed and points the shared code at it, so every version then runs as that one: each Form rune looks for the same
/// Form card, and all but one quietly do nothing. Those hooks are left unwatched; their effects just go uncredited.
/// </summary>
public static class HookPatching
{
    /// <param name="method">The hook where it's declared, as it would be handed to Harmony.</param>
    public static bool CanWatch(MethodInfo method) =>
        !method.IsGenericMethod && method.DeclaringType is { IsGenericType: false };
}
