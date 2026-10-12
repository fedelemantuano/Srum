using SrumData.Readers;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alphaleonis.Win32.Filesystem;
using CsvHelper;
using Exceptionless;
using Microsoft.Isam.Esent.Interop;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ServiceStack;
using SrumData;

#if NET472
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using Path = Alphaleonis.Win32.Filesystem.Path;
#else
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using File = System.IO.File;
#endif


namespace SrumECmd;

public class ApplicationArguments
{
    public string FileDb { get; set; }
    public string FileReg { get; set; }
    public string Directory { get; set; }
    public string CsvDirectory { get; set; }
    public string JsonDirectory { get; set; }
    public string JsonFilename { get; set; }

    public string DateTimeFormat { get; set; }

    public bool Debug { get; set; }
    public bool Trace { get; set; }
}

internal class Program
{
    private static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    private static readonly string Header =
        $"SrumECmd version {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}" +
        "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
        "\r\nhttps://github.com/EricZimmerman/Srum";


    private static readonly string Footer =
        @"Examples: SrumECmd.exe -f ""C:\Temp\SRUDB.dat"" -r ""C:\Temp\SOFTWARE"" --csv ""C:\Temp\"" " + "\r\n\t " +
        @"   SrumECmd.exe -f ""C:\Temp\SRUDB.dat"" --csv ""c:\temp""" + "\r\n\t " +
        @"   SrumECmd.exe -d ""C:\Temp"" --csv ""c:\temp""" + "\r\n\t " +
        @"   SrumECmd.exe -f ""C:\Temp\SRUDB.dat"" --json ""C:\Temp\out"" --jsonf srum.json" + "\r\n\t " +
        "\r\n\t" +
        "    Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes\r\n";

    private static string[] _args;
    private static RootCommand _rootCommand;

    private static bool IsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task Main(string[] args)
    {
        ExceptionlessClient.Default.Startup("wPXTiiouhEbK0s19lCgjiDThpfrW0ODU8RskdPEk");

        _args = args;

        try
        {
            var exitCode = await CreateRootCommand().Parse(args).InvokeAsync();
            if (exitCode != 0)
            {
                Environment.ExitCode = exitCode;
            }
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    internal static RootCommand CreateRootCommand()
    {
        var csvOpt = new Option<string>(
            "--csv")
        {
            Description = "Directory to save CSV formatted results to. This or --json is required"
          //  Required = true
        };

        var jsonOpt = new Option<string>("--json")
        {
            Description = "Directory to save JSON Lines results to. This or --csv is required"
        };
        var jsonfOpt = new Option<string>("--jsonf")
        {
            Description = "File name to save JSON Lines results to. Overrides the default name; requires --json"
        };
        
        var readerOpt = new Option<string>("--reader")
        {
            Description = "ESE reader: auto (Windows ESENT, otherwise Dissect), esent, dissect",
            DefaultValueFactory = _ => "auto"
        };

        var fOpt = new Option<string>("-f")
        {
            Description = "SRUDB.dat file to parse"
           // Required = true
        };
        
        var rOpt = new Option<string>("-r")
        {
            Description = "SOFTWARE hive to process. This is optional, but recommended\r\n"
        };
        
        var dOpt = new Option<string>("-d")
        {
            Description = "Directory to recursively process, looking for SRUDB.dat and SOFTWARE hive. This mode is primarily used with KAPE so both SRUDB.dat and SOFTWARE hive can be located"
        };
        
       var dtOpt = new Option<string>(
            "--dt")
        {
            Description = "The custom date/time format for CSV time stamps. JSON dates always use UTC ISO 8601. See https://goo.gl/CNVq0k for options\r\n",
            DefaultValueFactory = _ => "yyyy-MM-dd HH:mm:ss"
        };

        var debugOpt = new Option<bool>("--debug")
        {
            Description = "Show debug information during processing",
            DefaultValueFactory = _ => false
        };
        var traceOpt = new Option<bool>("--trace")
        {
            Description = "Show trace information during processing",
            DefaultValueFactory = _ => false
        };

        _rootCommand = new RootCommand
        {
           fOpt,
           rOpt,
           dOpt,
           csvOpt,
           jsonOpt,
           jsonfOpt,
           dtOpt,
           debugOpt,
           traceOpt,
           readerOpt
        
        };

        _rootCommand.Description = Header + "\r\n\r\n" + Footer;

        _rootCommand.Validators.Add(result =>
        {
            var reader = result.GetValue(readerOpt) ?? "auto";
            if (!new[] { "auto", "esent", "dissect" }.Contains(reader.ToLowerInvariant()))
                result.AddError("--reader must be auto, esent, or dissect.");
            if (string.IsNullOrWhiteSpace(result.GetValue(fOpt)) &&
                string.IsNullOrWhiteSpace(result.GetValue(dOpt)))
            {
                result.AddError("Either -f or -d is required.");
            }

            if (string.IsNullOrWhiteSpace(result.GetValue(csvOpt)) &&
                string.IsNullOrWhiteSpace(result.GetValue(jsonOpt)))
            {
                result.AddError("Either --csv or --json is required.");
            }

            var jsonf = result.GetValue(jsonfOpt);
            if (jsonf != null)
            {
                if (string.IsNullOrWhiteSpace(result.GetValue(jsonOpt)))
                {
                    result.AddError("--jsonf requires --json.");
                }
                if (!JsonExporter.IsValidFilename(jsonf))
                {
                    result.AddError("--jsonf must be a valid filename, not a path.");
                }
            }
        });

        _rootCommand.SetAction(result => DoWork(result.GetValue(fOpt),result.GetValue(rOpt),result.GetValue(dOpt),result.GetValue(csvOpt),result.GetValue(jsonOpt),result.GetValue(jsonfOpt),result.GetValue(dtOpt),result.GetValue(debugOpt),result.GetValue(traceOpt),result.GetValue(readerOpt)));
        return _rootCommand;
    }

    private static int DoWork(string f, string r, string d, string csv, string json, string jsonf, string dt, bool debug, bool trace, string reader)
    {
        var levelSwitch = new LoggingLevelSwitch();

        var template = "{Message:lj}{NewLine}{Exception}";

        if (debug)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Debug;
            template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        }

        if (trace)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
            template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        }

        var conf = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: template)
            .MinimumLevel.ControlledBy(levelSwitch);

        Log.Logger = conf.CreateLogger();

        if (f.IsNullOrEmpty() && d.IsNullOrEmpty())
        {
            var aaa = new CustomHelpAction(new HelpAction());
            aaa.Invoke(_rootCommand.Parse("Either -f or -d is required. Exiting"));

            //Log.Warning("Either -f or -d is required. Exiting");
            return 1;
        }


        if (f.IsNullOrEmpty() == false && !File.Exists(f))
        {
            Log.Warning("File '{File}' not found. Exiting", f);
            return 1;
        }

        if (d.IsNullOrEmpty() == false && !Directory.Exists(d))
        {
            Log.Warning("Directory '{D}' not found. Exiting", d);
            return 1;
        }

        if (csv.IsNullOrEmpty() && json.IsNullOrEmpty())
        {
            var aaa = new CustomHelpAction(new HelpAction());
            aaa.Invoke(_rootCommand.Parse("Either --csv or --json is required. Exiting"));

            //Log.Warning("Either -f or -d is required. Exiting");
            return 1;
        }

        Log.Information("{Header}", Header);
        Console.WriteLine();
        Log.Information("Command line: {Args}\r\n", string.Join(" ", _args ?? Array.Empty<string>()));

        if (IsAdministrator() == false)
        {
            Log.Warning("Warning: Administrator privileges not found!\r\n");
        }

        var sw = new Stopwatch();
        sw.Start();

        var ts = DateTimeOffset.UtcNow;

        Srum sr = null;

        if (d.IsNullOrEmpty() == false)
        {
            IEnumerable<string> files2;

#if NET6_0_OR_GREATER
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
                AttributesToSkip = 0
            };

            files2 =
                Directory.EnumerateFileSystemEntries(d, "SRUDB.DAT", enumerationOptions);

            var candidates = files2.Take(2).ToArray();
            if (candidates.Length > 1)
            {
                Log.Error("Multiple SRUDB.dat files found. Process each evidence directory separately or use -f.");
                return 1;
            }
            f = candidates.FirstOrDefault();

            if (f.IsNullOrEmpty())
            {
                Log.Warning("Did not locate any files named 'SRUDB.dat'! Exiting");
                return 1;
            }

            files2 =
                Directory.EnumerateFileSystemEntries(d, "SOFTWARE", enumerationOptions);

            r = string.IsNullOrWhiteSpace(r) ? files2.FirstOrDefault() : r;

            if (r.IsNullOrEmpty())
            {
                Log.Warning("Did not locate any files named 'SOFTWARE'! Registry data will not be extracted");
            }
            else
            {
                Log.Information("Found SOFTWARE hive '{R}'!", r);
            }

#elif NET472
            //kape mode, so find the files
            var ilter = new DirectoryEnumerationFilters();
            ilter.InclusionFilter = fsei =>
            {
                if (fsei.FileSize == 0)
                {
                    return false;
                }

                if (fsei.FileName.ToUpperInvariant() == "SRUDB.DAT")
                {
                    return true;
                }

                return false;
            };

            ilter.RecursionFilter = entryInfo => !entryInfo.IsMountPoint && !entryInfo.IsSymbolicLink;

            ilter.ErrorFilter = (errorCode, errorMessage, pathProcessed) => true;

            const DirectoryEnumerationOptions dirEnumOptions =
                DirectoryEnumerationOptions.Files | DirectoryEnumerationOptions.Recursive |
                DirectoryEnumerationOptions.SkipReparsePoints | DirectoryEnumerationOptions.ContinueOnException |
                DirectoryEnumerationOptions.BasicSearch;

             files2 =
                Directory.EnumerateFileSystemEntries(d, dirEnumOptions, ilter);

            var candidates = files2.Take(2).ToArray();
            if (candidates.Length > 1)
            {
                Log.Error("Multiple SRUDB.dat files found. Process each evidence directory separately or use -f.");
                return 1;
            }
            f = candidates.FirstOrDefault();

            if (f.IsNullOrEmpty())
            {
                Log.Warning("Did not locate any files named 'SRUDB.dat'! Exiting");
                return 1;
            }

            Log.Information("Found SRUM database file '{F}'!", f);

            ilter = new DirectoryEnumerationFilters();
            ilter.InclusionFilter = fsei =>
            {
                if (fsei.FileSize == 0)
                {
                    return false;
                }

                if (fsei.FileName.ToUpperInvariant() == "SOFTWARE")
                {
                    return true;
                }

                return false;
            };

            ilter.RecursionFilter = entryInfo => !entryInfo.IsMountPoint && !entryInfo.IsSymbolicLink;

            ilter.ErrorFilter = (errorCode, errorMessage, pathProcessed) => true;

            files2 =
                Directory.EnumerateFileSystemEntries(d, dirEnumOptions, ilter);
            
            
            r = string.IsNullOrWhiteSpace(r) ? files2.FirstOrDefault() : r;

            if (r.IsNullOrEmpty())
            {
                Log.Warning("Did not locate any files named 'SOFTWARE'! Registry data will not be extracted");
            }
            else
            {
                Log.Information("Found SOFTWARE hive '{R}'!", r);
            }
#endif


            Console.WriteLine();
        }

        try
        {
            Log.Information("Processing '{F}'...", f);
            sr = new Srum(f, r, (EseReaderKind)Enum.Parse(typeof(EseReaderKind), reader ?? "auto", true));

            Console.WriteLine();
            Log.Information("Processing complete!");
            Console.WriteLine();

            Log.Information("{EnergyUse} {EnergyUsagesCount:N0}", "Energy Usage count:".PadRight(30),
                sr.EnergyUsages.Count);
            Log.Information("{AppTimelineProviders} {AppTimelineProvidersCount:N0}",
                "AppTimelineProvider count:".PadRight(30),
                sr.TimelineProviders.Count);
            Log.Information("{vfuprovs} {vfuprovsCount:N0}", "vfuprov count:".PadRight(30),
                sr.Vfuprovs.Count);
            Log.Information("{AppResourceUseInfos} {AppResourceUseInfosCount:N0}",
                "App Resource Usage count:".PadRight(30), sr.AppResourceUseInfos.Count);
            Log.Information("{NetworkConnections} {NetworkConnectionsCount:N0}",
                "Network Connection count:".PadRight(30), sr.NetworkConnections.Count);
            Log.Information("{NetworkUsages} {NetworkUsagesCount:N0}", "Network Usage count:".PadRight(30),
                sr.NetworkUsages.Count);
            Log.Information("{PushNotifications} {PushNotificationsCount:N0}", "Push Notification count:".PadRight(30),
                sr.PushNotifications.Count);
            Console.WriteLine();
        }
        catch (Exception e)
        {
            Log.Error(e, "Cannot parse database: {Message}. No export was written.", e.Message);
            return 1;
        }

        var exitCode = sr.HasIncompleteData ? 2 : 0;

        if (!string.IsNullOrWhiteSpace(json))
        {
            var jsonExitCode = ExportJson(json, jsonf, ts, JsonExporter.GetRecords(sr), f, r);
            if (jsonExitCode != 0)
            {
                exitCode = jsonExitCode;
            }
        }

        if (csv.IsNullOrEmpty() == false)
        {
            if (Directory.Exists(csv) == false)
            {
                Log.Information(
                    "Path to '{Csv}' doesn't exist. Creating...", csv);

                try
                {
                    Directory.CreateDirectory(csv);
                }
                catch (Exception)
                {
                    Log.Fatal(
                        "Unable to create directory '{Csv}'. Does a file with the same name exist? Exiting", csv);
                    return 1;
                }
            }


            string outName;

            string outFile;

            Log.Information("CSV output will be saved to '{Csv}'\r\n", csv);

            try
            {
                Log.Debug("Dumping Energy Usage tables '{TableName}'", EnergyUsage.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_EnergyUsage_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<EnergyUsage>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");
                foo.Map(t => t.EventTimestamp).Convert(t =>
                    $"{t.Value.EventTimestamp?.ToString(dt)}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<EnergyUsage>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.EnergyUsages.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'EnergyUsage' data! Error: {Message}", e.Message);
            }


            try
            {
                Log.Debug("Dumping AppTimelineProvider table '{TableName}'", TimelineProvider.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_AppTimelineProvider_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<TimelineProvider>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");
                foo.Map(t => t.EndTime).Convert(t =>
                    $"{t.Value.EndTime.ToString(dt)}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<TimelineProvider>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.TimelineProviders.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'AppTimelineProvider' data! Error: {Message}", e.Message);
            }

            try
            {
                Log.Debug("Dumping vfuprov table '{TableName}'", Vfuprov.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_vfuprov_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<Vfuprov>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");
                foo.Map(t => t.EndTime).Convert(t =>
                    $"{t.Value.EndTime.ToString(dt)}");
                foo.Map(t => t.StartTime).Convert(t =>
                    $"{t.Value.StartTime.ToString(dt)}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<Vfuprov>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.Vfuprovs.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'vfuprov' data! Error: {Message}", e.Message);
            }

            try
            {
                Log.Debug("Dumping App Resource Use Info table '{TableName}'", AppResourceUseInfo.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_AppResourceUseInfo_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<AppResourceUseInfo>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<AppResourceUseInfo>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.AppResourceUseInfos.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'AppResourceUseInfo' data! Error: {Message}", e.Message);
            }

            try
            {
                Log.Debug("Dumping Network Connection table '{TableName}'", NetworkConnection.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_NetworkConnections_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<NetworkConnection>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");
                foo.Map(t => t.ConnectStartTime).Convert(t =>
                    $"{t.Value.ConnectStartTime.ToString(dt)}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<NetworkConnection>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.NetworkConnections.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'NetworkConnection' data! Error: {Message}", e.Message);
            }

            try
            {
                Log.Debug("Dumping Network Usage table '{TableName}'", NetworkUsage.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_NetworkUsages_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<NetworkUsage>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<NetworkUsage>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.NetworkUsages.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'NetworkUsage' data! Error: {Message}", e.Message);
            }

            try
            {
                Log.Debug("Dumping Push Notification table '{TableName}'", PushNotification.TableName);

                outName = $"{ts:yyyyMMddHHmmss}_SrumECmd_PushNotifications_Output.csv";

                outFile = Path.Combine(csv, outName);

                using var swCsv = new StreamWriter(outFile, false, Encoding.UTF8);

                using var csvWriter = new CsvWriter(swCsv, CultureInfo.InvariantCulture);

                var foo = csvWriter.Context.AutoMap<PushNotification>();
                foo.Map(t => t.Timestamp).Convert(t =>
                    $"{t.Value.Timestamp:yyyy-MM-dd HH:mm:ss}");

                csvWriter.Context.RegisterClassMap(foo);
                csvWriter.WriteHeader<PushNotification>();
                csvWriter.NextRecord();

                csvWriter.WriteRecords(sr.PushNotifications.Values);

                csvWriter.Flush();
                swCsv.Flush();
            }
            catch (Exception e)
            {
                exitCode = 1;
                Log.Error(e, "Error exporting 'PushNotification' data! Error: {Message}", e.Message);
            }

        }

        try
        {
            var reportPath = !string.IsNullOrWhiteSpace(json)
                ? JsonExporter.GetOutputPath(json, jsonf, ts) + ".diagnostics.json"
                : Path.Combine(csv, $"{ts:yyyyMMddHHmmss}_SrumECmd.diagnostics.json");
            ProcessingReport.Write(reportPath, sr, f, r, exitCode);
            Log.Information("Diagnostics saved to '{ReportPath}'", reportPath);
        }
        catch (Exception e)
        {
            Log.Error(e, "Cannot write processing diagnostics.");
            exitCode = 1;
        }
        sw.Stop();
        Log.Information("Processing completed in {TotalSeconds:N4} seconds\r\n", sw.Elapsed.TotalSeconds);
        return exitCode;
    }

    internal static int ExportJson(string directory, string filename, DateTimeOffset exportTime,
        IEnumerable<object> records, string databasePath, string hivePath)
    {
        try
        {
            var outputPath = JsonExporter.GetOutputPath(directory, filename, exportTime);
            var fullOutputPath = System.IO.Path.GetFullPath(outputPath);
            if (string.Equals(fullOutputPath, System.IO.Path.GetFullPath(databasePath), StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(hivePath) && string.Equals(fullOutputPath,
                    System.IO.Path.GetFullPath(hivePath), StringComparison.OrdinalIgnoreCase)))
            {
                throw new IOException("JSON output must not overwrite the SRUM database or SOFTWARE hive.");
            }

            JsonExporter.Write(outputPath, records);
            Log.Information("JSON Lines output saved to '{JsonFile}'\r\n", outputPath);
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error exporting JSON data! Error: {Message}", e.Message);
            return 1;
        }
    }
    
    private class CustomHelpAction : SynchronousCommandLineAction
    {
        private readonly HelpAction _defaultHelp;

        public CustomHelpAction(HelpAction action)
        {
            _defaultHelp = action;
        }

        public override int Invoke(ParseResult parseResult)
        {
            var result = _defaultHelp.Invoke(parseResult);

            Log.Warning("{Msg}", string.Join(" ",parseResult.Tokens));

            return result;
        }
    }

    
}

