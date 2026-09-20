using System.Collections;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JavOrganizer;

/// <summary>
/// Bridges the Jellyfin API differences that appeared after 10.9, so a
/// single build of the plugin runs on every later server.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin is not binary-compatible across versions, but the differences
/// that matter here are narrow and were each resolved the same way: read or
/// write the member through reflection and accept whichever shape the
/// running server exposes. Doing that at runtime instead of at compile time
/// is what removes the need for a build per version.
/// </para>
/// <para>
/// The known differences covered here:
/// </para>
/// <list type="bullet">
///   <item><see cref="TaskTriggerInfo.Type"/> — string on 10.9 and 10.10,
///   the <c>TaskTriggerInfoType</c> enum from 10.11.</item>
///   <item><c>IUserManager.Users</c> — a property through 10.10, replaced by
///   <c>GetUsers()</c> in 10.11.</item>
///   <item><c>ILibraryManager.GetItemList</c> and <c>GetPeople</c> — returned
///   <c>List&lt;T&gt;</c> through 10.11 and <c>IReadOnlyList&lt;T&gt;</c> from
///   12.0. A return-type change is invisible at compile time but breaks the
///   call with <c>MissingMethodException</c> at runtime.</item>
///   <item><see cref="PersonInfo.Type"/> — a string on 10.8, the
///   <c>PersonKind</c> enum from 10.9, and that enum moved namespaces again
///   in 12.1, so it is never named directly.</item>
/// </list>
/// </remarks>
internal static class JavApiCompat
{
    /// <summary>
    /// Creates a trigger that fires once a day at the given local time.
    /// </summary>
    /// <param name="timeOfDay">Time of day in the server's local time.</param>
    /// <returns>A trigger configured for a daily run.</returns>
    internal static TaskTriggerInfo DailyTrigger(TimeSpan timeOfDay)
    {
        var trigger = new TaskTriggerInfo();
        SetTriggerType(trigger, "DailyTrigger");
        trigger.TimeOfDayTicks = timeOfDay.Ticks;
        return trigger;
    }

    /// <summary>
    /// Creates a trigger that fires repeatedly, every <paramref name="interval"/>.
    /// </summary>
    /// <param name="interval">Time between runs.</param>
    /// <returns>A trigger configured for a repeating run.</returns>
    internal static TaskTriggerInfo IntervalTrigger(TimeSpan interval)
    {
        var trigger = new TaskTriggerInfo();
        SetTriggerType(trigger, "IntervalTrigger");
        trigger.IntervalTicks = interval.Ticks;
        return trigger;
    }

    /// <summary>
    /// Reads the items a library query matches, without binding to the
    /// collection type the server returns.
    /// </summary>
    /// <param name="libraryManager">The server's library manager.</param>
    /// <param name="query">The query to run.</param>
    /// <returns>The matching items, in the order the server returned them.</returns>
    internal static IEnumerable<BaseItem> GetItemList(object libraryManager, InternalItemsQuery query)
    {
        var result = Invoke(libraryManager, "GetItemList", new[] { typeof(InternalItemsQuery) }, query);
        return result as IEnumerable<BaseItem> ?? Enumerable.Empty<BaseItem>();
    }

    /// <summary>
    /// Reads the people credited on an item, without binding to the
    /// collection type the server returns.
    /// </summary>
    /// <param name="libraryManager">The server's library manager.</param>
    /// <param name="item">The item to read credits from.</param>
    /// <returns>The credited people.</returns>
    internal static IEnumerable<PersonInfo> GetPeople(object libraryManager, BaseItem item)
    {
        var result = Invoke(libraryManager, "GetPeople", new[] { typeof(BaseItem) }, item);
        return result as IEnumerable<PersonInfo> ?? Enumerable.Empty<PersonInfo>();
    }

    /// <summary>
    /// Reads the people already recorded on a metadata result, without
    /// binding to the collection type the server returns.
    /// </summary>
    /// <param name="result">The metadata result being built.</param>
    /// <returns>The people recorded so far.</returns>
    internal static IEnumerable<PersonInfo> GetPeople(object result)
    {
        var raw = result.GetType().GetProperty("People")?.GetValue(result) as IEnumerable;
        return raw is null ? Enumerable.Empty<PersonInfo>() : raw.Cast<PersonInfo>();
    }

    /// <summary>
    /// Reports whether a credited person is a director, whatever type the
    /// role property holds.
    /// </summary>
    /// <param name="person">The credited person.</param>
    /// <returns><c>true</c> when the credit is a director.</returns>
    internal static bool IsDirector(PersonInfo person)
    {
        return string.Equals(PersonRole(person), "Director", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a person credit carrying the role, without naming the type of
    /// the role property.
    /// </summary>
    /// <param name="name">The person's name.</param>
    /// <param name="isActor"><c>true</c> for a performer, <c>false</c> for crew.</param>
    /// <returns>The person credit.</returns>
    internal static PersonInfo PersonWithRole(string name, bool isActor)
    {
        var person = new PersonInfo { Name = name };
        var property = typeof(PersonInfo).GetProperty(nameof(PersonInfo.Type));
        var role = isActor ? "Actor" : "Director";

        if (property is null)
        {
            return person;
        }

        if (property.PropertyType == typeof(string))
        {
            property.SetValue(person, role);
            return person;
        }

        if (property.PropertyType.IsEnum)
        {
            property.SetValue(person, Enum.Parse(property.PropertyType, role));
        }

        return person;
    }

    /// <summary>
    /// Reads the users the server knows about. 10.9 and 10.10 expose them as
    /// an <c>IUserManager.Users</c> property; 10.11 replaced it with
    /// <c>GetUsers()</c>.
    /// </summary>
    /// <remarks>
    /// The elements are deliberately untyped. The user type is not named so
    /// the assembly never has to reference it, which is what keeps a single
    /// build valid across the versions that moved it around.
    /// </remarks>
    /// <param name="userManager">The server's user manager.</param>
    /// <returns>Every configured user.</returns>
    internal static IEnumerable<object> GetUsers(object userManager)
    {
        var type = userManager.GetType();

        var raw = type.GetProperty("Users")?.GetValue(userManager) as IEnumerable
            ?? Invoke(userManager, "GetUsers", Type.EmptyTypes) as IEnumerable;

        return raw is null ? Enumerable.Empty<object>() : raw.Cast<object>();
    }

    /// <summary>
    /// Reads a person's role as text, whatever type the property holds.
    /// </summary>
    /// <param name="person">The credited person.</param>
    /// <returns>The role, or <c>null</c> when it cannot be read.</returns>
    private static string? PersonRole(PersonInfo person)
    {
        var property = typeof(PersonInfo).GetProperty(nameof(PersonInfo.Type));
        return property?.GetValue(person)?.ToString();
    }

    /// <summary>
    /// Calls a method by name, tolerating a changed signature.
    /// </summary>
    /// <param name="target">The instance to call on.</param>
    /// <param name="name">The method name.</param>
    /// <param name="parameterTypes">Parameter types to match, or none.</param>
    /// <param name="arguments">The arguments to pass.</param>
    /// <returns>The method's return value, or <c>null</c> if it is absent.</returns>
    private static object? Invoke(object target, string name, Type[] parameterTypes, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(name, parameterTypes);
        return method?.Invoke(target, arguments);
    }

    /// <summary>
    /// Assigns <paramref name="typeName"/> to a trigger's type property,
    /// using whichever representation the running Jellyfin exposes.
    /// </summary>
    /// <param name="trigger">The trigger to configure.</param>
    /// <param name="typeName">The trigger type, e.g. "DailyTrigger".</param>
    private static void SetTriggerType(TaskTriggerInfo trigger, string typeName)
    {
        var property = typeof(TaskTriggerInfo).GetProperty(nameof(TaskTriggerInfo.Type));
        if (property is null)
        {
            return;
        }

        if (property.PropertyType == typeof(string))
        {
            property.SetValue(trigger, typeName);
            return;
        }

        if (property.PropertyType.IsEnum)
        {
            property.SetValue(trigger, Enum.Parse(property.PropertyType, typeName));
        }
    }
}
