using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Tests;

internal static class SettingsTransferTests
{
    public static void ExportFailurePreservesDestinationAndCanRetry()
    {
        using var folder = new DataFolder();
        string destination = Path.Combine(folder.Directory, "export.zip");
        File.WriteAllText(destination, "previous export");
        File.WriteAllText(Paths.In("settings.json"), "{unfinished");
        bool failed = false;
        try { SettingsTransfer.Export(destination, "test"); }
        catch (JsonException) { failed = true; }
        Check.True(failed, "invalid source settings fail export");
        Check.Eq(File.ReadAllText(destination), "previous export", "a failed export keeps its destination");
        Check.Eq(System.IO.Directory.GetFiles(folder.Directory, "*.tmp").Length, 0, "failed export staging is removed");

        // A staging file left by an older version belongs to that attempt and must not block or be deleted by us.
        File.WriteAllText(destination + ".tmp", "older attempt");
        File.WriteAllText(Paths.In("settings.json"), """{"theme":"light","claudePath":"private"}""");
        Check.Eq(SettingsTransfer.Export(destination, "test"), 1, "retry to the same destination succeeds");
        Check.Eq(File.ReadAllText(destination + ".tmp"), "older attempt", "another attempt's staging is preserved");
        using var zip = ZipFile.OpenRead(destination);
        Check.True(zip.GetEntry("bloom-settings.json") is not null, "successful retry contains a manifest");
        using var reader = new StreamReader(zip.GetEntry("settings.json")!.Open());
        var settings = (JsonObject)JsonNode.Parse(reader.ReadToEnd())!;
        Check.Eq(settings["theme"]!.GetValue<string>(), "light", "portable settings are exported");
        Check.True(!settings.ContainsKey("claudePath"), "local settings stay out of the export");
    }

    public static void ImportRemovesAllLocalCaseVariants()
    {
        using var folder = new DataFolder();
        string archive = Path.Combine(folder.Directory, "incoming.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("bloom-settings.json").Open()))
                writer.Write("""{"app":"Bloom"}""");
            using (var writer = new StreamWriter(zip.CreateEntry("settings.json").Open()))
                writer.Write("""
                    {"codexPath":"imported-a","CODEXPATH":"imported-b",
                     "onboarded":false,"ONBOARDED":false,
                     "aiAccounts":[],"AIACCOUNTS":[{"id":"foreign"}],
                     "theme":"light","customPortable":{"enabled":true}}
                    """);
        }

        SettingsTransfer.Import(archive);
        var fresh = (JsonObject)JsonNode.Parse(File.ReadAllText(Paths.In("settings.json")))!;
        Check.True(!fresh.ContainsKey("codexPath") && !fresh.ContainsKey("CODEXPATH"), "no imported local path variant remains without prior local settings");
        Check.True(!fresh.ContainsKey("onboarded") && !fresh.ContainsKey("ONBOARDED"), "no imported onboarding variant remains");
        Check.True(!fresh.ContainsKey("aiAccounts") && !fresh.ContainsKey("AIACCOUNTS"), "no imported account variant remains");
        Check.True(fresh["customPortable"]!["enabled"]!.GetValue<bool>(), "unknown portable properties survive");

        const string prior = """{"codexPath":"local-path","onboarded":true,"aiAccounts":[{"id":"local"}],"theme":"dark"}""";
        File.WriteAllText(Paths.In("settings.json"), prior);
        SettingsTransfer.Import(archive);
        var restored = (JsonObject)JsonNode.Parse(File.ReadAllText(Paths.In("settings.json")))!;
        Check.Eq(restored["codexPath"]!.GetValue<string>(), "local-path", "the prior local path survives");
        Check.True(restored["onboarded"]!.GetValue<bool>(), "the prior local onboarding state survives");
        Check.Eq(restored["aiAccounts"]![0]!["id"]!.GetValue<string>(), "local", "the prior local accounts survive");
        Check.True(!restored.ContainsKey("CODEXPATH") && !restored.ContainsKey("ONBOARDED") && !restored.ContainsKey("AIACCOUNTS"), "foreign case variants are all removed");
        Check.Eq(restored["theme"]!.GetValue<string>(), "light", "portable settings are imported");
    }

    public static void ImportKeepsThisPcsPermissionsAndMcpSelections()
    {
        using var folder = new DataFolder();
        string archive = Path.Combine(folder.Directory, "incoming.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("bloom-settings.json").Open()))
                writer.Write("""{"app":"Bloom"}""");
            using (var writer = new StreamWriter(zip.CreateEntry("settings.json").Open()))
                writer.Write("""{"theme":"light","permissions":{"command":"allow","file:write":"allow","mcp":"allow"},"mcpOn":["test|evil"],"mcpFiles":["C:\\evil\\.mcp.json"],"apiFileTools":true}""");
        }

        SettingsTransfer.Import(archive);
        var fresh = (JsonObject)JsonNode.Parse(File.ReadAllText(Paths.In("settings.json")))!;
        Check.Eq(fresh["theme"]!.GetValue<string>(), "light", "portable settings are imported");
        Check.True(!fresh.ContainsKey("permissions") && !fresh.ContainsKey("mcpOn") && !fresh.ContainsKey("mcpFiles") && !fresh.ContainsKey("apiFileTools"),
            "a file from elsewhere switches no approval off and no MCP server on");

        File.WriteAllText(Paths.In("settings.json"), """{"theme":"dark","permissions":{"command":"off"},"mcpOn":["claude|mine"],"mcpFiles":[]}""");
        SettingsTransfer.Import(archive);
        var kept = (JsonObject)JsonNode.Parse(File.ReadAllText(Paths.In("settings.json")))!;
        Check.Eq(kept["permissions"]!["command"]!.GetValue<string>(), "off", "this PC's permissions stay");
        Check.True(kept["permissions"]!.AsObject().Count == 1 && kept["mcpOn"]![0]!.GetValue<string>() == "claude|mine" && kept["mcpFiles"]!.AsArray().Count == 0 && !kept.ContainsKey("apiFileTools"), "and its MCP selections");

        string export = Path.Combine(folder.Directory, "out.zip");
        SettingsTransfer.Export(export, "test");
        using var zipOut = ZipFile.OpenRead(export);
        using var reader = new StreamReader(zipOut.GetEntry("settings.json")!.Open());
        var exported = (JsonObject)JsonNode.Parse(reader.ReadToEnd())!;
        Check.True(!exported.ContainsKey("permissions") && !exported.ContainsKey("mcpOn"), "an export leaves them out");
    }

    public static void AtomicWriteKeepsDestinationWhenWriterOrMoveFails()
    {
        using var folder = new DataFolder();
        string destination = Path.Combine(folder.Directory, "destination.json");
        File.WriteAllText(destination, "original");
        bool failed = false;
        try
        {
            AtomicFile.Write(destination, stream =>
            {
                stream.WriteByte(123);
                throw new IOException("simulated interrupted writer");
            });
        }
        catch (IOException) { failed = true; }
        Check.True(failed, "writer failures propagate to callers");
        Check.Eq(File.ReadAllText(destination), "original", "a partial staged write never replaces the destination");
        Check.Eq(System.IO.Directory.GetFiles(folder.Directory, "*.tmp").Length, 0, "writer failure staging is cleaned");

        // A destination that is a directory forces the final replacement to fail after writing succeeds.
        string blocked = Path.Combine(folder.Directory, "blocked.json");
        System.IO.Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "marker"), "keep");
        failed = false;
        try { AtomicFile.Write(blocked, stream => stream.WriteByte(123)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed = true; }
        Check.True(failed, "replacement failures propagate to callers");
        Check.Eq(File.ReadAllText(Path.Combine(blocked, "marker")), "keep", "failed replacement preserves the destination");
        Check.Eq(System.IO.Directory.GetFiles(folder.Directory, "*.tmp").Length, 0, "replacement failure staging is cleaned");
    }

    public static void SameSecondImportsPreserveBothBackupGenerations()
    {
        using var folder = new DataFolder();
        string archive = Path.Combine(folder.Directory, "incoming.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("bloom-settings.json").Open()))
                writer.Write("""{"app":"Bloom"}""");
            using (var writer = new StreamWriter(zip.CreateEntry("settings.json").Open()))
                writer.Write("""{"theme":"imported"}""");
        }

        var sameSecond = new DateTime(2026, 10, 12, 12, 34, 56);
        string settings = Paths.In("settings.json");
        const string first = """{"theme":"first-generation"}""";
        const string second = """{"theme":"second-generation"}""";
        File.WriteAllText(settings, first);
        SettingsTransfer.Import(archive, sameSecond);
        File.WriteAllText(settings, second);
        SettingsTransfer.Import(archive, sameSecond);

        string[] backups = System.IO.Directory.GetDirectories(Paths.In("backups"), "20261012-123456-*");
        Check.Eq(backups.Length, 2, "imports in the same second create separate recovery directories");
        var contents = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (string backup in backups) contents.Add(File.ReadAllText(Path.Combine(backup, "settings.json")));
        Check.True(contents.Contains(first) && contents.Contains(second), "both prior generations remain recoverable");
        var current = (JsonObject)JsonNode.Parse(File.ReadAllText(settings))!;
        Check.Eq(current["theme"]!.GetValue<string>(), "imported", "the second import still applies its settings");
    }

    /// Restore test-global paths directly: Init(null) would create real user-data directories.
    internal sealed class DataFolder : IDisposable
    {
        private readonly string priorData = Paths.Data, priorLocal = Paths.Local;
        private readonly bool priorDev = Paths.IsDev;
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "bloom-transfer-test-" + Guid.NewGuid().ToString("N"));

        public DataFolder() => Paths.Init(Directory);

        public void Dispose()
        {
            Restore(nameof(Paths.Data), priorData);
            Restore(nameof(Paths.Local), priorLocal);
            Restore(nameof(Paths.IsDev), priorDev);
            System.IO.Directory.Delete(Directory, true);
        }

        private static void Restore(string name, object value) =>
            typeof(Paths).GetProperty(name, BindingFlags.Static | BindingFlags.Public)!.SetValue(null, value);
    }
}
