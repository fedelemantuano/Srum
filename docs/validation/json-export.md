# JSON export validation

Executed on 2026-10-09 using Docker Desktop on an ARM64 host. The repository was mounted read-only and copied into a temporary build directory; no .NET SDK installation on the host was needed.

## Build and tests

Image: `mcr.microsoft.com/dotnet/sdk:9.0`, digest `sha256:1330d3c4144aec2695036a125d9a3b49a1fc555c9024c981248fa83d40fa8b23`.

- Release build of SrumECmd and SrumData for both `net9.0` and `net472`: passed, zero warnings and zero errors.
- NUnit suite: 20 automated tests passed, zero failures.
- The legacy `BuildingAutomation` test is explicitly excluded from the ordinary suite because it requires Windows and evidence at hardcoded local paths. It was not executed.
- Compiled executable `--help`: passed; the new `--json`, `--jsonf`, and JSON-independent `--dt` behavior are documented in help.

The tests cover all seven record categories, independent JSON lines, field types, strings with Unicode and embedded newlines, large integers, nulls, every timestamp property, seven fractional digits, offset-to-UTC conversion, the JSON-only EnergyUsage interpretation, preservation of record/configuration state, durations, filenames, directory creation, truncation/replacement, empty output, failed writes, input-file protection, and CLI validation/exit status.

From the repository root, reproduce the build and tests with:

```sh
docker run --rm \
  -v "$PWD:/src:ro" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  mcr.microsoft.com/dotnet/sdk:9.0 sh -c '
    set -eu
    cp -a /src /tmp/srum
    cd /tmp/srum
    dotnet test SrumTest/SrumTest.csproj --configuration Release
    dotnet build SrumECmd/SrumECmd.csproj --configuration Release --no-restore
  '
```

## Live Elasticsearch ingestion

Image: `docker.elastic.co/elasticsearch/elasticsearch:8.19.0`, digest `sha256:e1e66bfabae0fd03a0a36651a9bb198e7f061e0c99f457a6203b116e053e9cdb`.

The disposable single-node instance ran with security disabled for the isolated test, without publishing ports to the host. The index used the [documented mapping](../design/json-export.md): `RecordType` as `keyword` and all six timestamp property names as standard `date` fields, without custom formats or `date_nanos`.

The test suite generated two synthetic JSON Lines files, each containing one record per category. Bulk action lines were added to these exported documents for the ingestion request.

- All 14 documents were created with HTTP item status 201 and `errors: false`.
- The index contained all seven categories, each with two documents.
- An exact term filter for `RecordType = NetworkUsage` returned two documents.
- Every source `Timestamp` remained `2026-10-09T12:34:56.1234567Z`.
- Every indexed `Timestamp` doc value was `2026-10-09T12:34:56.123Z`, as expected for the millisecond-resolution `date` type.
- Both missing temporal values and populated `ExeTimestamp` values were accepted. In the second dataset, executable timestamps supplied with a nonzero offset were exported in UTC.

Machine-readable [ingestion results](json-export-elasticsearch.json) are retained beside this report. The test container was stopped after validation.

## Limits

This validates the implemented serializer, CLI contract, compilation of both targets, and real Elasticsearch acceptance of synthetic exported records. It does not validate parsing a real SRUDB.dat, Windows execution of either target, or the complete CLI-to-ESENT-to-Elasticsearch path. Those checks require Windows ESENT and representative SRUM evidence fixtures, which are not supplied in this repository.

The EnergyUsage rule follows the UTC interpretation of the other six models. It does not independently establish the original timezone semantics of SRUM evidence.
