# SrumECmd

A SRUM parser!

## Command Line Interface

    SrumECmd version 2026.5.0

    Author: Eric Zimmerman (saericzimmerman@gmail.com)
    https://github.com/EricZimmerman/Srum

            d               Directory to recursively process, looking for SRUDB.dat and SOFTWARE hive. This mode is primarily used with KAPE so both SRUDB.dat and SOFTWARE hive can be located
            f               SRUDB.dat file to process. Either this or -d is required
            r               SOFTWARE hive to process. This is optional, but recommended

            csv             Directory to save CSV formatted results to. This or --json is required
            json            Directory to save JSON Lines results to. This or --csv is required
            jsonf           File name to save JSON Lines results to. Overrides the default name; requires --json

            dt              The custom date/time format for CSV time stamps. Default is: yyyy-MM-dd HH:mm:ss. JSON dates always use UTC ISO 8601

            debug           Show debug information during processing
            trace           Show trace information during processing


    Examples: SrumECmd.exe -f "C:\Temp\SRUDB.dat" -r "C:\Temp\SOFTWARE" --csv "C:\Temp\"
              SrumECmd.exe -f "C:\Temp\SRUDB.dat" --csv "c:\temp"
              SrumECmd.exe -d "C:\Temp" --csv "c:\temp"
              SrumECmd.exe -f "C:\Temp\SRUDB.dat" --json "C:\Output" --jsonf srum.json
              SrumECmd.exe -f "C:\Temp\SRUDB.dat" --csv "C:\Output\csv" --json "C:\Output\json"

              Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes

    Either -f or -d is required. Exiting

## Documentation

### JSON Lines export

`--json` exports all seven supported SRUM record categories into a single UTF-8 file, without a byte order mark, containing one JSON object per line. The default name is `<UTC yyyyMMddHHmmss>_SrumECmd_Output.json`; `--jsonf` overrides that filename exactly. It accepts a filename, not a path. An existing output file with the same name is replaced. An empty database export creates an empty file.

Each object retains the record's field names and adds `RecordType`, with one of these values: `EnergyUsage`, `TimelineProvider`, `Vfuprov`, `AppResourceUseInfo`, `NetworkConnection`, `NetworkUsage`, or `PushNotification`. Nullable fields belonging to the record are emitted as `null`; numeric and Boolean values retain their JSON types. Enum values are names, and `Duration` is an invariant duration string rather than a timestamp.

All JSON timestamps use UTC ISO 8601 with seven fractional digits and a trailing `Z`, for example `2026-10-09T12:34:56.1234567Z`. This applies to all temporal fields, including `ExeTimestamp` and optional event dates. JSON formatting is independent of `--dt`; CSV output formatting remains unchanged. The JSON export interprets the EnergyUsage timestamp's recorded clock components as UTC, matching the convention of the other SRUM categories without modifying the CSV record.

### Elasticsearch

Map `RecordType` as `keyword` to filter exact category names, and timestamp fields as `date`. The standard `strict_date_optional_time||epoch_millis` date parser accepts the exported strings. Elasticsearch indexes `date` values at millisecond resolution, while the JSON source retains its available fractional precision. See the [export contract and mapping example](docs/design/json-export.md).

For example, filter network usage records with:

```json
{"query":{"term":{"RecordType":"NetworkUsage"}}}
```

The output supplies documents for an ingestion pipeline. For Elasticsearch's Bulk API, add an action line before each document. A record's `Id` is local to its source table; it is not a globally unique Elasticsearch document ID.

### Validation

The synthetic JSON export and CLI tests can run with the .NET 9 SDK on Linux, including Docker:

```sh
dotnet test SrumTest/SrumTest.csproj --configuration Release
```

Parsing a real SRUDB.dat still requires Windows ESENT. The existing `BuildingAutomation` test is explicit because it requires Windows and evidence files at its configured local paths.

See the [Docker validation report](docs/validation/json-export.md) for reproduction commands, build results, and live Elasticsearch ingestion results.

## Repairing the SRUDB.dat

When you run SrumECmd, you will likely encounter an error message that states the file is dirty. 

    Command line: -d M:\Forensics\SrumECmdTest\tout -k --csv M:\Forensics\SrumECmdTest\mout\SystemActivity
    Found SRUM database file 'M:\Forensics\SrumECmdTest\tout\C\Windows\System32\SRU\SRUDB.dat'!
    Found SOFTWARE hive 'M:\Forensics\SrumECmdTest\tout\C\Windows\System32\config\software'!
    Processing 'M:\Forensics\SrumECmdTest\tout\C\Windows\System32\SRU\SRUDB.dat'...
    Error processing file! Message: Object reference not set to an instance of an object..
    This almost always means the database is dirty and must be repaired. This can be verified by running 'esentutl.exe /mh SRUDB.dat' and examining the 'State' property
    If the database is dirty, **make a copy of your files**, ensure all files in the directory are not Read-only, open a PowerShell session as an admin, and repair by using the following commands (change directories to the location of SRUDB.dat first):
    'esentutl.exe /r sru /i'
    'esentutl.exe /p SRUDB.dat'
    Executed 1 processor in 0.9046 seconds

    Total execution time: 6.5490 seconds
    
Follow these steps to repair the `SRUDB.dat` so you can run SrumECmd.exe again. First, follow the steps SrumECmd provides:
1. Make a copy of the files within the `.\SRU` directory

2. Ensure the `.\SRU` directory itself is not Read Only. This can be done by right clicking on the directory itself, Properties, and unchecking Read Only if it is checked:

![SRUFolderReadOnlyExample](Pictures/SRUFolderReadOnlyExample.gif)

3. Open a PowerShell session as an Administrator in the directory where your copied files reside

4. Execute this command within the PowerShell Admin session: 

    `esentutl.exe /r sru /i`

![SRUDBFirstRepairCommand](Pictures/SRUDBFirstRepairCommand.gif)

5. Execute this command within the PowerShell Admin session: 

    `esentutl.exe /p SRUDB.dat`
    
![SRUDBSecondRepairCommand](Pictures/SRUDBSecondRepairCommand.gif)

6. Try running SrumECmd again against the location where these repaired files reside:

![SrumECmdParsingSuccessExample](Pictures/SrumECmdParsingSuccessExample.png)

7. Examine output in Timeline Explorer!

![SrumECmdCSVOutput](Pictures/SrumECmdCSVOutput.jpg)

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). 

# Special Thanks

Open Source Development funding and support provided by the following contributors: 
- [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
- [Tines](https://www.tines.com/?utm_source=oss&utm_medium=sponsorship&utm_campaign=ericzimmerman)
