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

## Included C# ESE reader and Docker build

The ESE reader source is included in `EseRecovery/` and built through a project reference. SrumECmd uses this single C# path on every platform: clean databases open directly, while dirty databases are recovered automatically when the required matching transaction logs are adjacent to `SRUDB.dat`. Missing logs, invalid evidence, or unsupported recovery operations stop processing before export. The public CLI has no backend or recovery-mode selector.

Python, Dissect, Windows `esent.dll`, external NuGet feeds, and external Docker build contexts are not required. The included reader implements the ESE subset required by the supported SRUM datasets; it is not a complete replacement for every Windows ESE operation. See [the included library scope](EseRecovery/README.md).

Build the runnable Linux x64 image from the repository root:

```sh
docker build --platform linux/amd64 --build-arg DOTNET_RID=linux-x64 -t srumecmd:local .
```

Export the complete self-contained Linux x64 directory:

```sh
docker build --platform linux/amd64 --target export --build-arg DOTNET_RID=linux-x64 --output type=local,dest=artifacts/bin/linux-x64 .
```

Export the macOS ARM64 directory from the same Dockerfile and sign the app host after extraction:

```sh
docker build --platform linux/amd64 --target export --build-arg DOTNET_RID=osx-arm64 --output type=local,dest=artifacts/bin/osx-arm64 .
chmod +x artifacts/bin/osx-arm64/SrumECmd
codesign --force --sign - artifacts/bin/osx-arm64/SrumECmd
```

The export target copies the complete publish directory because the executable requires its adjacent self-contained runtime files.


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

Real SRUDB.dat files are parsed by the included C# reader on Linux and macOS. The existing `BuildingAutomation` test remains explicit because it uses evidence paths configured on the developer machine.

See the [Docker validation report](docs/validation/json-export.md) for reproduction commands, build results, and live Elasticsearch ingestion results.

## Transaction recovery

Keep `SRUDB.dat`, `SRU.log`, the numbered `SRU*.log` files, and the checkpoint from the same acquisition together. The reader validates matching log identities and generations, replays the supported committed operations, and removes supported uncommitted changes in memory; it never modifies the evidence files.

If recovery cannot establish a supported consistent state, SrumECmd exits with code `1` and does not create a new JSON or CSV export. Copying unrelated logs beside a dirty database does not make that database recoverable.

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). 

# Special Thanks

Open Source Development funding and support provided by the following contributors: 
- [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
- [Tines](https://www.tines.com/?utm_source=oss&utm_medium=sponsorship&utm_campaign=ericzimmerman)
