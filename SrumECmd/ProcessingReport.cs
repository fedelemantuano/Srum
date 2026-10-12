using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ServiceStack.Text;
using SrumData;
using SrumData.Readers;

namespace SrumECmd;

internal static class ProcessingReport
{
    internal static void Write(string path, Srum database, string input, string hive, int exitCode)
    {
        foreach (var source in new[] { input, hive })
            if (!string.IsNullOrWhiteSpace(source) && string.Equals(Path.GetFullPath(path),
                Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Diagnostics must not overwrite input evidence.");
        var counts = JsonExporter.GetRecords(database).GroupBy(r => r.GetType().Name)
            .ToDictionary(g => g.Key, g => g.Count());
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(input);
        var report = new Dictionary<string, object>
        {
            ["SchemaVersion"] = 1,
            ["Reader"] = database.ReaderKind.ToString(),
            ["DatabaseState"] = database.DatabaseState,
            ["DatabaseStateName"] = database.DatabaseState == 3 ? "CleanShutdown" :
                database.DatabaseState == 2 ? "DirtyShutdown" : "Other",
            ["LogReplay"] = database.ReaderKind == EseReaderKind.Dissect ? "NotSupported" : "ManagedByEsent",
            ["PotentiallyIncomplete"] = database.HasIncompleteData,
            ["ExportStatus"] = exitCode == 1 ? "Failed" : exitCode == 2 ? "PotentiallyIncomplete" : "Succeeded",
            ["ExitCode"] = exitCode,
            ["InputSha256"] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(),
            ["RecordCounts"] = counts,
            ["RowsRead"] = database.RowsRead,
            ["RowsParsed"] = database.RowsParsed,
            ["MissingOptionalTables"] = database.MissingTables,
            ["ParsingErrorCount"] = database.ParsingErrorCount,
            ["ParsingErrors"] = database.ParsingErrors,
            ["SoftwareHiveProvided"] = !string.IsNullOrWhiteSpace(hive)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (JsConfig.With(new Config { ExcludeTypeInfo = true, IncludeNullValuesInDictionaries = true }))
                File.WriteAllText(temporary, JsonSerializer.SerializeToString(report) + "\n", new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
