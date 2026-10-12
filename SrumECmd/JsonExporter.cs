using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ServiceStack.Text;
using SrumData;

namespace SrumECmd;

internal static class JsonExporter
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly Type[] RecordTypes =
    {
        typeof(EnergyUsage), typeof(TimelineProvider), typeof(Vfuprov), typeof(AppResourceUseInfo),
        typeof(NetworkConnection), typeof(NetworkUsage), typeof(PushNotification)
    };
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    internal static string GetOutputPath(string directory, string filename, DateTimeOffset exportTime)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A JSON output directory is required.", nameof(directory));
        }

        if (filename != null && !IsValidFilename(filename))
        {
            throw new ArgumentException("--jsonf must be a filename, not a path.", nameof(filename));
        }

        var outputName = filename ??
            $"{exportTime.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}_SrumECmd_Output.json";
        return Path.Combine(directory, outputName);
    }

    internal static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename == "." || filename == ".." ||
            filename.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) ||
            filename.EndsWith(".", StringComparison.Ordinal) || filename.EndsWith(" ", StringComparison.Ordinal))
        {
            return false;
        }

        // Windows device names are reserved even with a file extension.
        var stem = filename.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem != "CON" && stem != "PRN" && stem != "AUX" && stem != "NUL" &&
               !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                   stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]));
    }

    internal static IEnumerable<object> GetRecords(Srum database)
    {
        return database.EnergyUsages.Values.Cast<object>()
            .Concat(database.TimelineProviders.Values)
            .Concat(database.Vfuprovs.Values)
            .Concat(database.AppResourceUseInfos.Values)
            .Concat(database.NetworkConnections.Values)
            .Concat(database.NetworkUsages.Values)
            .Concat(database.PushNotifications.Values);
    }

    internal static void Write(string outputPath, IEnumerable<object> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
        var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            using (JsConfig.With(new Config
            {
                IncludeNullValues = true,
                IncludeNullValuesInDictionaries = true,
                ExcludeTypeInfo = true,
                TextCase = TextCase.Default
            }))
            {
                writer.NewLine = "\n";
                foreach (var record in records) writer.WriteLine(Serialize(record));
            }
            if (File.Exists(outputPath)) File.Replace(temporary, outputPath, null);
            else File.Move(temporary, outputPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Serialize(object record)
    {
        if (record == null || !RecordTypes.Contains(record.GetType()))
        {
            throw new ArgumentException("Only supported SRUM records can be exported.", nameof(record));
        }

        var type = record.GetType();
        var fields = new Dictionary<string, object> { ["RecordType"] = type.Name };
        var properties = Properties.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        foreach (var property in properties)
        {
            var value = property.GetValue(record);
            if (value is DateTimeOffset timestamp)
            {
                // EnergyUsage's parser implicitly attributes the host's offset to the ESE clock value.
                // Match the other SRUM models' UTC interpretation for JSON without mutating CSV data.
                var utc = record is EnergyUsage && property.Name == nameof(EnergyUsage.Timestamp)
                    ? new DateTimeOffset(DateTime.SpecifyKind(timestamp.DateTime, DateTimeKind.Utc))
                    : timestamp.ToUniversalTime();
                value = utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
            }
            else if (value is Enum)
            {
                value = value.ToString();
            }
            else if (value is TimeSpan duration)
            {
                value = duration.ToString("c", CultureInfo.InvariantCulture);
            }

            fields.Add(property.Name, value);
        }

        return JsonSerializer.SerializeToString(fields);
    }
}
