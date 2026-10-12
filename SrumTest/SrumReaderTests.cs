using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SrumData;
using SrumData.Readers;
using SrumECmd;

namespace SrumTest;

[TestFixture]
public class SrumReaderTests
{
    private string directory;
    private string file;
    private static readonly DateTime Clock = new(2024, 6, 7, 8, 9, 10, 123, DateTimeKind.Unspecified);
    private static readonly DateTimeOffset Event = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero).AddTicks(1234567);

    [SetUp]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "srum-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "SRUDB.dat");
        File.WriteAllText(file, "synthetic transport fixture");
    }

    [TearDown] public void Cleanup() => Directory.Delete(directory, true);

    private Srum Parse(FixtureReader reader) => new(file, null, reader, EseReaderKind.Dissect);

    [Test]
    public void MapsAllCategoriesAndDistinguishesOleDatesFromFiletimes()
    {
        var db = Parse(new FixtureReader());
        Assert.Multiple(() =>
        {
            Assert.That(db.ParsingErrorCount, Is.Zero);
            Assert.That(db.HasIncompleteData, Is.False);
            Assert.That(JsonExporter.GetRecords(db).Count(), Is.EqualTo(8));
            Assert.That(db.NetworkUsages[1].Timestamp.UtcDateTime, Is.EqualTo(DateTime.SpecifyKind(Clock, DateTimeKind.Utc)));
            Assert.That(db.NetworkConnections[1].ConnectStartTime, Is.EqualTo(Event));
            Assert.That(db.Vfuprovs[1].StartTime, Is.EqualTo(Event));
            Assert.That(db.TimelineProviders[1].EndTime, Is.EqualTo(Event));
            Assert.That(db.NetworkUsages[1].BytesReceived, Is.EqualTo(9876543210L));
            Assert.That(db.NetworkUsages[1].ExeInfo, Is.EqualTo("C:\\apps\\è 東京.exe"));
            Assert.That(db.NetworkUsages[1].Sid, Is.EqualTo("S-1-5-18"));
            Assert.That(db.PushNotifications[1].PayloadSize, Is.EqualTo(11));
            Assert.That(db.EnergyUsages.Values.Count(e => e.IsLt), Is.EqualTo(1));
            Assert.That(db.EnergyUsages.Values.Single(e => !e.IsLt).EventTimestamp, Is.EqualTo(Event));
        });
    }

    [TestCase(2, true)]
    [TestCase(3, false)]
    [TestCase(99, true)]
    public void DirtyAndUnknownStatesAreReported(int state, bool incomplete)
    {
        Assert.That(Parse(new FixtureReader { State = state }).HasIncompleteData, Is.EqualTo(incomplete));
    }

    [Test]
    public void MissingOptionalTableIsDifferentFromBrokenRows()
    {
        var db = Parse(new FixtureReader { MissingPush = true });
        Assert.That(db.HasIncompleteData, Is.False);
        Assert.That(db.MissingTables, Does.Contain(PushNotification.TableName));
        Assert.That(db.PushNotifications, Is.Empty);
    }

    [Test]
    public void MalformedRowDoesNotDiscardLaterValidRowsAndIsReported()
    {
        var db = Parse(new FixtureReader { BadNetworkRow = true });
        Assert.That(db.NetworkUsages.Count, Is.EqualTo(1));
        Assert.That(db.RowsRead[NetworkUsage.TableName], Is.EqualTo(2));
        Assert.That(db.RowsParsed[NetworkUsage.TableName], Is.EqualTo(1));
        Assert.That(db.ParsingErrorCount, Is.EqualTo(1));
        Assert.That(db.HasIncompleteData, Is.True);
        ProcessingReport.Write(Path.Combine(directory, "report.json"), db, file, null, 2);
        Assert.That(File.ReadAllText(Path.Combine(directory, "report.json")), Does.Contain("PotentiallyIncomplete"));
    }

    [Test]
    public void MissingRequiredMappingTableFails()
    {
        Assert.That(() => Parse(new FixtureReader { MissingMap = true }), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void TimestampColumnMustBeOleDateStorage()
    {
        Assert.That(() => Parse(new FixtureReader { BadType = true }), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void EmptyTableWithUnsupportedSchemaFails()
    {
        Assert.That(() => Parse(new FixtureReader { BadSchema = true }), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("-f data --json out --reader dissect", true)]
    [TestCase("-f data --json out --reader AUTO", true)]
    [TestCase("-f data --json out --reader unknown", false)]
    public void ReaderCliValidation(string args, bool valid) =>
        Assert.That(Program.CreateRootCommand().Parse(args).Errors.Count == 0, Is.EqualTo(valid));

    [Test]
    public void FailedSerializationPreservesExistingExport()
    {
        var output = Path.Combine(directory, "previous.json");
        File.WriteAllText(output, "previous result");
        Assert.Throws<ArgumentException>(() => JsonExporter.Write(output, new object[] { new object() }));
        Assert.That(File.ReadAllText(output), Is.EqualTo("previous result"));
        Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void MissingInputReturnsFailure()
    {
        Assert.That(Program.CreateRootCommand().Parse(new[] { "-f", file + ".missing", "--json", directory }).Invoke(), Is.EqualTo(1));
    }

    [Test]
    public void ParsesBothPrivateSamplesWhenConfigured()
    {
        var root = Environment.GetEnvironmentVariable("SRUM_TEST_SAMPLE_ROOT");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("Set SRUM_TEST_SAMPLE_ROOT to private Clean/Dirty sample parent.");
        foreach (var name in new[] { "Clean", "Dirty" })
        {
            var db = new Srum(Path.Combine(root, name, "SRUDB.dat"), null, EseReaderKind.Dissect);
            Assert.That(JsonExporter.GetRecords(db).Count(), Is.EqualTo(6811), name);
            Assert.That(db.ParsingErrorCount, Is.Zero, name);
            Assert.That(db.HasIncompleteData, Is.EqualTo(name == "Dirty"), name);
            Assert.That(db.RowsRead, Is.EquivalentTo(db.RowsParsed), name);
        }
    }

    private sealed class FixtureReader : IEseReader
    {
        internal int State = 3;
        internal bool MissingPush, MissingMap, BadNetworkRow, BadSchema, BadType;

        public IEnumerable<EseRecord> ReadDatabase(string path, string[] tables)
        {
            yield return new EseRecord { Kind = "database", State = State, Version = 1 };
            foreach (var table in tables)
            {
                if ((MissingMap && table == "SruDbIdMapTable") || (MissingPush && table == PushNotification.TableName))
                {
                    yield return new EseRecord { Kind = "missing", Table = table };
                    continue;
                }
                var rows = Records(table).ToList();
                yield return new EseRecord { Kind = "table", Table = table,
                    Columns = BadSchema ? new() : rows[0].Values.Keys.ToDictionary(k => k, k =>
                        k == "TimeStamp" ? (BadType ? "LongLong" : "DateTime") :
                        k == "IdBlob" ? "LongBinary" : k == "IdType" ? "UnsignedByte" :
                        Convert.FromBase64String(rows[0].Values[k]).Length == 8 ? "LongLong" : "Long") };
                if (BadSchema) continue;
                if (BadNetworkRow && table == NetworkUsage.TableName)
                {
                    var broken = new EseRecord { Kind = "row", Table = table, Values = new(rows[0].Values) };
                    broken.Values["TimeStamp"] = Convert.ToBase64String(new byte[] { 1 });
                    yield return broken;
                }
                foreach (var row in rows) yield return row;
            }
            yield return new EseRecord { Kind = "end" };
        }

        private static IEnumerable<EseRecord> Records(string table)
        {
            if (table == "SruDbIdMapTable")
            {
                yield return Row(table, new() { ["IdType"] = (byte)0, ["IdIndex"] = 1, ["IdBlob"] = Encoding.Unicode.GetBytes("C:\\apps\\è 東京.exe\0") });
                yield return Row(table, new() { ["IdType"] = (byte)3, ["IdIndex"] = 2, ["IdBlob"] = new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 18, 0, 0, 0 } });
                yield break;
            }
            var fields = new Dictionary<string, object>
            {
                ["AutoIncId"] = 1, ["AppId"] = 1, ["UserId"] = 2, ["TimeStamp"] = Clock.ToOADate()
            };
            string ints = "", longs = "";
            if (table == NetworkUsage.TableName || table == NetworkConnection.TableName)
            {
                ints = "L2ProfileFlags L2ProfileId";
                longs = "InterfaceLuid";
                if (table == NetworkUsage.TableName) longs += " BytesRecvd BytesSent";
                else { ints += " ConnectedTime"; fields["ConnectStartTime"] = Event.ToFileTime(); }
            }
            else if (table == PushNotification.TableName) ints = "NetworkType NotificationType PayloadSize";
            else if (table == TimelineProvider.TableName) { ints = "DurationMS"; fields["EndTime"] = Event.ToFileTime(); }
            else if (table == Vfuprov.TableName)
            {
                ints = "Flags"; fields["EndTime"] = Event.ToFileTime(); fields["StartTime"] = Event.ToFileTime();
            }
            else if (table == AppResourceUseInfo.TableName)
            {
                ints = "BackgroundContextSwitches BackgroundNumberOfFlushes BackgroundNumReadOperations BackgroundNumWriteOperations ForegroundContextSwitches ForegroundNumberOfFlushes ForegroundNumReadOperations ForegroundNumWriteOperations";
                longs = "BackgroundBytesRead BackgroundBytesWritten BackgroundCycleTime ForegroundBytesRead ForegroundBytesWritten ForegroundCycleTime FaceTime";
            }
            else
            {
                ints = "DesignedCapacity FullChargedCapacity CycleCount";
                longs = "ConfigurationHash";
                if (table.EndsWith("LT")) ints += " ActiveAcTime ActiveDcTime ActiveDischargeTime ActiveEnergy CsAcTime CsDcTime CsDischargeTime CsEnergy";
                else { ints += " StateTransition ChargeLevel"; fields["EventTimestamp"] = Event.ToFileTime(); }
            }
            foreach (var field in ints.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) fields[field] = 11;
            foreach (var field in longs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) fields[field] = 9876543210L;
            yield return Row(table, fields);
        }

        private static EseRecord Row(string table, Dictionary<string, object> fields) => new()
        {
            Kind = "row", Table = table, Values = fields.ToDictionary(f => f.Key, f => Convert.ToBase64String(f.Value switch
            {
                byte b => new[] { b },
                int i => BitConverter.GetBytes(i),
                long l => BitConverter.GetBytes(l),
                double d => BitConverter.GetBytes(d),
                byte[] bytes => bytes,
                _ => throw new ArgumentException()
            }))
        };
    }
}
