using System;
using System.Linq;
using System.Reflection;

namespace Bloom.Tests;

/// Runs every public static method of every *Tests class; a check that fails throws. Exit code = failures.
/// `dotnet run --project tests\Bloom.Tests [-- <filter>]` (a filter keeps the tests whose name contains it).
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;
        var tests = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Tests", StringComparison.Ordinal) && t.IsClass)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(void)))
            .Where(m => filter is null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int failed = 0;
        foreach (var m in tests)
        {
            string name = $"{m.DeclaringType!.Name}.{m.Name}";
            Core.Lang.Force(false); // each test starts in Korean; English tests switch themselves
            try
            {
                m.Invoke(null, null);
                Console.WriteLine($"  ok    {name}");
            }
            catch (TargetInvocationException e) when (e.InnerException is { } inner)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}: {inner.Message}");
            }
        }
        Console.WriteLine(failed == 0 ? $"all {tests.Count} passed" : $"{failed} of {tests.Count} failed");
        return failed;
    }
}

/// The checks. Each throws with what was expected and what came.
internal static class Check
{
    public static void Eq<T>(T actual, T expected, string what)
    {
        if (!Equals(actual, expected)) throw new InvalidOperationException($"{what}: expected «{expected}», got «{actual}»");
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }

    public static void Near(double actual, double expected, double tolerance, string what)
    {
        if (Math.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"{what}: expected {expected} ± {tolerance}, got {actual}");
    }

    public static T NotNull<T>(T? value, string what) where T : class => value ?? throw new InvalidOperationException(what + ": got null");
}
