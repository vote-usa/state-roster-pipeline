using System.Reflection;

namespace StateBallot.Core;

/// <summary>
/// Discovers IStateCollector implementations via [StateCode("XX")] and a
/// constructor invokable as (HttpFetcher, int, string[, string? inputDataRoot, ...]).
/// Loads any StateBallot.States.*.dll beside the entry assembly so collectors are found
/// even before their types are otherwise referenced.
/// </summary>
public static class CollectorDiscovery
{
    public static Dictionary<string, Func<HttpFetcher, int, string, string?, IStateCollector>> Discover(
        IEnumerable<Assembly>? assemblies = null)
    {
        EnsureStateAssembliesLoaded();

        var map = new Dictionary<string, Func<HttpFetcher, int, string, string?, IStateCollector>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || !typeof(IStateCollector).IsAssignableFrom(type))
                    continue;

                var attr = type.GetCustomAttribute<StateCodeAttribute>();
                if (attr is null)
                    continue;

                var ctor = FindCollectorConstructor(type)
                    ?? throw new InvalidOperationException(
                        $"{type.FullName} is marked [StateCode(\"{attr.Code}\")] but lacks a public " +
                        "constructor callable as (HttpFetcher, int year, string stateDataDir, ...).");

                if (map.ContainsKey(attr.Code))
                    throw new InvalidOperationException(
                        $"Duplicate [StateCode(\"{attr.Code}\")] on {type.FullName} and another collector.");

                map[attr.Code] = (fetcher, year, dir, inputRoot) =>
                    (IStateCollector)ctor.Invoke(BuildCtorArgs(ctor, fetcher, year, dir, inputRoot))!;
            }
        }

        return map;
    }

    /// <summary>
    /// Loads StateBallot.States.*.dll from the entry / base directory so that
    /// project-referenced state assemblies participate in discovery without
    /// requiring a live typeof() touch at the call site.
    /// </summary>
    private static void EnsureStateAssembliesLoaded()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
            dirs.Add(AppContext.BaseDirectory);
        try
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entry))
                dirs.Add(Path.GetDirectoryName(entry)!);
        }
        catch
        {
            // ignore
        }

        var loaded = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => n is not null)
                .Cast<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var path in Directory.EnumerateFiles(dir, "StateBallot.States.*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (loaded.Contains(name))
                    continue;
                try
                {
                    Assembly.LoadFrom(path);
                    loaded.Add(name);
                }
                catch
                {
                    // Skip unloadable satellites.
                }
            }
        }
    }

    private static object?[] BuildCtorArgs(
        ConstructorInfo ctor, HttpFetcher fetcher, int year, string dir, string? inputRoot)
    {
        var parms = ctor.GetParameters();
        var args = new object?[parms.Length];
        args[0] = fetcher;
        args[1] = year;
        args[2] = dir;

        var assignedInput = false;
        for (var i = 3; i < parms.Length; i++)
        {
            if (!assignedInput && parms[i].ParameterType == typeof(string))
            {
                args[i] = inputRoot ?? (parms[i].HasDefaultValue ? parms[i].DefaultValue : null);
                assignedInput = true;
            }
            else
            {
                args[i] = parms[i].HasDefaultValue ? parms[i].DefaultValue : null;
            }
        }

        return args;
    }

    private static ConstructorInfo? FindCollectorConstructor(Type type)
    {
        foreach (var ctor in type.GetConstructors())
        {
            var parms = ctor.GetParameters();
            if (parms.Length < 3)
                continue;
            if (parms[0].ParameterType != typeof(HttpFetcher))
                continue;
            if (parms[1].ParameterType != typeof(int))
                continue;
            if (parms[2].ParameterType != typeof(string))
                continue;
            if (parms.Skip(3).All(p => p.HasDefaultValue))
                return ctor;
        }

        return null;
    }
}
