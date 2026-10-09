using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using ServiceStack.Text;
using SrumData;
using SrumECmd;

namespace SrumTest;

[TestFixture]
public class JsonExporterTests
{
    private static readonly DateTime Clock = new DateTime(2026, 10, 9, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);
    private static readonly DateTimeOffset Instant = new(Clock);
    private string _directory;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "srum-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void ExportsAllCategoriesAsFlatIndependentJsonLinesWithTypedFields()
    {
        var records = CreateRecords();
        var path = Export(records);
        var bytes = File.ReadAllBytes(path);
        Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }), "No UTF-8 BOM");
        var lines = File.ReadAllLines(path);
        Assert.That(lines, Has.Length.EqualTo(7));
        var types = new List<string>();
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            types.Add(root.GetProperty("RecordType").GetString());
            Assert.That(root.GetProperty("Id").GetInt32(), Is.EqualTo(1), "Same ID may exist in different categories");
            Assert.That(root.GetProperty("SidType").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(root.TryGetProperty("TableName", out _), Is.False, "Static metadata is not a record field");
        }

        Assert.That(types, Is.EquivalentTo(new[]
        {
            "EnergyUsage", "TimelineProvider", "Vfuprov", "AppResourceUseInfo", "NetworkConnection",
            "NetworkUsage", "PushNotification"
        }));
        using var energy = JsonDocument.Parse(lines[0]);
        Assert.That(energy.RootElement.GetProperty("IsLt").GetBoolean(), Is.False);
        using var network = JsonDocument.Parse(lines[5]);
        Assert.That(network.RootElement.GetProperty("BytesReceived").GetInt64(), Is.EqualTo(9007199254740993L));
        Assert.That(network.RootElement.GetProperty("ProfileName").GetString(), Is.EqualTo("caffè\n\"WiFi\""));
        Assert.That(network.RootElement.TryGetProperty("EventTimestamp", out _), Is.False);
        SaveArtifact(path, "srum-synthetic.jsonl");
    }

    [Test]
    public void FormatsEveryTemporalFieldInUtcPreservingSevenDigitsAndInstant()
    {
        var records = CreateRecords();
        foreach (var record in records)
        {
            record.GetType().GetProperty("ExeTimestamp").SetValue(record, Instant.ToOffset(TimeSpan.FromHours(2)));
        }

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            var path = Export(records);
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                using var document = JsonDocument.Parse(lines[i]);
                var root = document.RootElement;
                foreach (var name in new[] { "Timestamp", "ExeTimestamp", "EventTimestamp", "ConnectStartTime", "StartTime", "EndTime" })
                {
                    if (!root.TryGetProperty(name, out var field))
                    {
                        continue;
                    }

                    var text = field.GetString();
                    Assert.That(text, Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$"));
                    var parsed = DateTimeOffset.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                    var expected = (DateTimeOffset)records[i].GetType().GetProperty(name).GetValue(records[i]);
                    if (records[i] is EnergyUsage && name == "Timestamp")
                    {
                        expected = Instant;
                    }
                    Assert.That(parsed, Is.EqualTo(expected), $"{records[i].GetType().Name}.{name}");
                }
            }
            SaveArtifact(path, "srum-dates-synthetic.jsonl");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void EnergyUtcInterpretationPreservesEvidenceClockAndDoesNotMutateRecord()
    {
        var record = CreateEnergy(-1);
        var originalTimestamp = record.Timestamp;
        using var document = JsonDocument.Parse(File.ReadAllText(Export(new object[] { record })));
        Assert.That(document.RootElement.GetProperty("Timestamp").GetString(), Is.EqualTo("2026-10-09T12:34:56.1234567Z"));
        Assert.That(document.RootElement.GetProperty("EventTimestamp").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(record.Timestamp, Is.EqualTo(originalTimestamp));
        Assert.That(record.Timestamp.Offset, Is.EqualTo(TimeSpan.FromHours(-7)));
    }

    [Test]
    public void PreservesNullsAndDurationWithoutChangingGlobalSerializationConfiguration()
    {
        using var outerScope = JsConfig.With(new Config { IncludeNullValues = false, TextCase = TextCase.CamelCase });
        var records = CreateRecords();
        var lines = File.ReadAllLines(Export(records));
        using var energy = JsonDocument.Parse(lines[0]);
        Assert.That(energy.RootElement.GetProperty("ExeTimestamp").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(energy.RootElement.GetProperty("ExeInfo").ValueKind, Is.EqualTo(JsonValueKind.Null));
        using var vfu = JsonDocument.Parse(lines[2]);
        Assert.That(vfu.RootElement.GetProperty("Duration").GetString(), Is.EqualTo("00:00:03.2500000"));
        Assert.That(JsConfig.TextCase, Is.EqualTo(TextCase.CamelCase));
        Assert.That(JsConfig.IncludeNullValues, Is.False);
    }

    [Test]
    public void CreatesDirectoryAndReplacesLongerExistingFileThenSupportsEmptyExport()
    {
        var path = Path.Combine(_directory, "nested", "srum.json");
        JsonExporter.Write(path, CreateRecords());
        JsonExporter.Write(path, new object[] { CreateEnergy(-1) });
        Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(1));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.That(document.RootElement.GetProperty("RecordType").GetString(), Is.EqualTo("EnergyUsage"));
        JsonExporter.Write(path, Array.Empty<object>());
        Assert.That(new FileInfo(path).Length, Is.Zero);
    }

    [Test]
    public void UsesExactCustomFilenameAndUtcDefaultFilename()
    {
        var exportTime = new DateTimeOffset(2026, 10, 9, 14, 34, 56, TimeSpan.FromHours(2));
        Assert.That(JsonExporter.GetOutputPath(_directory, "custom.json", exportTime),
            Is.EqualTo(Path.Combine(_directory, "custom.json")));
        Assert.That(JsonExporter.GetOutputPath(_directory, null, exportTime),
            Is.EqualTo(Path.Combine(_directory, "20261009123456_SrumECmd_Output.json")));
    }

    [Test]
    public void ReportsOutputIoFailures()
    {
        var directoryAsFile = Path.Combine(_directory, "occupied");
        File.WriteAllText(directoryAsFile, "existing file");
        Assert.Throws<IOException>(() => JsonExporter.Write(Path.Combine(directoryAsFile, "srum.json"), CreateRecords()));
        Assert.That(Program.ExportJson(directoryAsFile, "srum.json", Instant, CreateRecords(),
            Path.Combine(_directory, "SRUDB.dat"), null), Is.EqualTo(1));
    }

    [TestCase("SRUDB.dat")]
    [TestCase("SOFTWARE")]
    public void RefusesToOverwriteEitherEvidenceInput(string filename)
    {
        var evidencePath = Path.Combine(_directory, filename);
        File.WriteAllText(evidencePath, "evidence must survive");
        Assert.That(Program.ExportJson(_directory, filename, Instant, CreateRecords(),
            Path.Combine(_directory, "SRUDB.dat"), Path.Combine(_directory, "SOFTWARE")), Is.EqualTo(1));
        Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("evidence must survive"));
    }

    private string Export(IEnumerable<object> records)
    {
        var path = Path.Combine(_directory, "srum.json");
        JsonExporter.Write(path, records);
        return path;
    }

    private static void SaveArtifact(string path, string filename)
    {
        var destination = Path.Combine(TestContext.CurrentContext.WorkDirectory, filename);
        File.Copy(path, destination, true);
        TestContext.AddTestAttachment(destination, "Synthetic SRUM JSON Lines for ingestion validation");
    }

    internal static object[] CreateRecords()
    {
        return new object[]
        {
            CreateEnergy(Instant.ToFileTime()),
            new TimelineProvider(1, Clock, 2, 3, 3250, Instant.ToFileTime()),
            new Vfuprov(1, Clock, 2, 3, 0, Instant.AddMilliseconds(-3250).ToFileTime(), Instant.ToFileTime()),
            new AppResourceUseInfo(1, Clock, 2, 3, 10, 20, 30, 40, 50, 60, 70, 0, 0, 0, 0, 0, 0, 0, 0),
            new NetworkConnection(1, Clock, 60, Instant.ToOffset(TimeSpan.FromHours(2)), 0, 0, 0, 2, 3, "WiFi"),
            new NetworkUsage(1, Clock, 2, 3, 9007199254740993L, 123, 0, 0, 0, "caffè\n\"WiFi\""),
            new PushNotification(1, Clock, 0, 0, 25, 2, 3)
        };
    }

    private static EnergyUsage CreateEnergy(long eventTimestamp)
    {
        return new EnergyUsage(1, false, new DateTimeOffset(DateTime.SpecifyKind(Clock, DateTimeKind.Unspecified),
                TimeSpan.FromHours(-7)), 2, 3, 0, eventTimestamp, 0, 50, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
