using System.Runtime.CompilerServices;
using SnapGit.Core.Diagnostics;

namespace SnapGit.Tests;

internal static class TestEnvironment
{
    /// <summary>Runs once before any test: keep logs/settings out of the developer's real %APPDATA%.</summary>
    [ModuleInitializer]
    internal static void Init() =>
        AppPaths.DataRoot = Path.Combine(Path.GetTempPath(), "snapgit-tests-data");
}
