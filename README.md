# SrumECmd

A SRUM parser for Windows, Linux, and macOS. The Docker image runs entirely on Linux using Dissect; it does not require a Windows host or VM.

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

            reader          ESE reader: auto (default), esent, or dissect

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

Real SRUDB.dat parsing is available on Linux/macOS with Dissect. The original `BuildingAutomation` test remains explicit because it uses hard-coded Windows evidence paths. Set `SRUM_TEST_SAMPLE_ROOT` to a private directory containing the Clean and Dirty validation fixtures to run the optional integration test.

See the [Docker validation report](docs/validation/json-export.md) for reproduction commands, build results, and live Elasticsearch ingestion results.

## Docker on macOS and Linux

Build the image locally (automatically targets the build's Linux ARM64 or AMD64 architecture):

```sh
docker build -t srumecmd:local .
mkdir -p output/Clean output/Dirty
for sample in Clean Dirty; do
  docker run --rm --network none --read-only \
    --user "$(id -u):$(id -g)" --cap-drop ALL --security-opt no-new-privileges \
    --tmpfs /tmp:rw,noexec,nosuid,size=128m \
    -v "/absolute/path/BelkasoftCTF_InsiderThreat/$sample:/evidence:ro" \
    -v "$PWD/output/$sample:/out" \
    srumecmd:local -d /evidence --json /out --jsonf srum.json --csv /out
  status=$?
  echo "$sample exit status: $status"
done
```

Run this loop in a shell without `set -e`, or handle exit 2 explicitly in your calling application. Replace the absolute evidence path. Each invocation processes one database; `-d` rejects ambiguous directories containing multiple SRUDB.dat files. Use `-r /path/to/SOFTWARE` for optional registry enrichment. Without a hive, application/SID mappings stored in SRUDB.dat are still used.

The image includes .NET and pinned Python dependencies and runs as UID/GID 65532 by default. The output volume must be writable by the selected user. Input can remain read-only, and no runtime network access is required. For execution outside Docker, install Python >=3.10 and `SrumData/python/requirements.txt`; `SRUM_PYTHON` selects the interpreter.

### Processing status

| Exit code | Meaning |
| --- | --- |
| 0 | Supported tables were parsed and outputs written without incompleteness detected. |
| 1 | Input, helper, schema, or output failure. Do not treat existing outputs from a previous run as new results. |
| 2 | Output written, but the ESE state is not clean or some rows failed to decode. Inspect diagnostics before ingestion. |

JSON export also writes `<json-filename>.diagnostics.json` in the output directory. CSV-only mode writes `<timestamp>_SrumECmd.diagnostics.json`. This report includes reader, ESE state, input SHA-256, log replay capability, record/table counts, missing optional tables, and bounded row-error details. Optional tables may be absent legitimately. A missing required ID mapping table or incompatible schema is fatal. Individual malformed rows are counted and reported while other readable rows continue.

**Dissect reads database pages; it does not replay transaction logs.** A dirty SRUDB.dat can still yield useful records, but these may be incomplete or inconsistent. Supplying SRU.log, numbered logs, or SRU.chk does not change this. Clean and dirty files can produce identical page-level exports; that does not demonstrate transaction-level completeness. This implementation parses the seven supported activity categories; it is not deleted-record carving or a generic dump of every ESE system table.

The Windows `esent` reader remains available for existing users. Linux containers never invoke `esent.dll`, a Windows VM, or lossy `esentutl /p` repair. JSON timestamp and CSV field conventions are unchanged.

### Kubernetes

Use [deploy/kubernetes-job.yaml](deploy/kubernetes-job.yaml) as a Job template: choose your image reference, existing input/output PVCs, evidence directory and a unique output directory for each database. It uses Linux nodes, a non-root process, read-only evidence/root filesystem, no service-account token, and a writable temporary volume. Size memory limits for your evidence: decoded SRUM records are retained in memory before export.

Exit 2 is deliberately nonzero. Kubernetes marks such a Job as failed even though results were produced; the orchestration layer should distinguish `2` (results requiring review) from `1` (failure), retain the output PVC, and read the diagnostic report. `backoffLimit: 0` prevents automatic retry loops on dirty evidence. The template does not provide a cluster-specific NetworkPolicy; add an egress policy if your deployment requires enforced network isolation. See the [Kubernetes Job documentation](https://kubernetes.io/docs/concepts/workloads/controllers/job/).

To build for both Linux architectures for a registry, use `docker buildx build --platform linux/amd64,linux/arm64 -t YOUR_REGISTRY/srumecmd:VERSION --push .`. Publishing and deployment are separate from local validation.

See [real-sample Docker validation](docs/validation/linux-docker.md) for the tested files and results.

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). 

# Special Thanks

Open Source Development funding and support provided by the following contributors: 
- [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
- [Tines](https://www.tines.com/?utm_source=oss&utm_medium=sponsorship&utm_campaign=ericzimmerman)
