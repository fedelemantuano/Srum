# Unified SRUM JSON Lines export

Status: confirmed by the user and implemented.

## Output contract

- Export all records from the seven supported categories into one UTF-8 file without a byte order mark, with one complete JSON object per line.
- Use flat objects and retain the public instance property names of each record model.
- Add a string `RecordType` field with one of: `EnergyUsage`, `TimelineProvider`, `Vfuprov`, `AppResourceUseInfo`, `NetworkConnection`, `NetworkUsage`, `PushNotification`.
- Preserve numeric and Boolean values as JSON numbers and Booleans. Serialize enum values as names and `TimeSpan` durations as invariant strings; durations are not timestamps.
- Include nullable properties belonging to the model with JSON `null` when absent. Do not add properties from unrelated record categories.
- Export every date in UTC using the invariant format `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`, for example `2026-10-09T12:34:56.1234567Z`. Seven fractional digits preserve the model's available precision and are accepted by Elasticsearch's standard date parser.
- Apply the date rule to `Timestamp`, `ExeTimestamp`, `EventTimestamp`, `ConnectStartTime`, `StartTime`, and `EndTime`, wherever present.
- Format JSON independently of `--dt`; existing CSV behavior remains unchanged.

## Command-line contract

- `--json <directory>` enables the unified JSON Lines export and creates the output directory when necessary.
- `--jsonf <filename>` overrides the default filename exactly; it requires `--json` and accepts a filename rather than an output path.
- The default filename is `<UTC yyyyMMddHHmmss>_SrumECmd_Output.json`.
- Require at least one of `--csv` and `--json`; accept either individually or both in the same invocation.
- Preserve the existing `-f`, `-d`, and optional `-r` input behavior.
- A repeated export to the same filename replaces the previous contents rather than appending or leaving trailing bytes.
- Exporting no records produces an empty JSON Lines file.
- Report JSON export failures clearly and return a nonzero exit status.

Example:

```powershell
SrumECmd.exe -f "C:\Evidence\SRUDB.dat" -r "C:\Evidence\SOFTWARE" --json "C:\Output" --jsonf srum.json
```

## EnergyUsage timestamp interpretation

The other six models explicitly interpret the ESE `TimeStamp` column as UTC. `EnergyUsage` instead accepts a `DateTimeOffset`, while its parser passes a `DateTime` through an implicit conversion which can attribute the execution host's local offset.

For JSON only, interpret the `EnergyUsage.Timestamp` wall-clock components as UTC, matching the convention already applied to the other record categories. Simply calling `ToUniversalTime()` on this value would preserve the initially attributed host offset and could shift the recorded clock time. Do not mutate the shared record or change its CSV representation.

This follows the parser's existing interpretation; it does not independently establish the original timezone semantics of SRUM evidence. All other offset-bearing dates are converted to UTC preserving their instant.

## Elasticsearch contract

`RecordType` should be mapped as `keyword` for exact filtering. Timestamp properties should be mapped explicitly as `date`; the standard date format is `strict_date_optional_time||epoch_millis`. Exporting JSON Lines supplies documents for an ingestion pipeline; it is not the action/document format required by Elasticsearch's Bulk API.

The official Elasticsearch 8.19 `testFractionalSeconds` test parses `.1234567Z` using `strict_date_optional_time`. A `date` field indexes with millisecond resolution even when the source string retains seven fractional digits; `date_nanos` is not required to accept this export format.

An index creation body for the discriminator and all timestamp properties is:

```json
{
  "mappings": {
    "properties": {
      "RecordType": { "type": "keyword" },
      "Timestamp": { "type": "date" },
      "ExeTimestamp": { "type": "date" },
      "EventTimestamp": { "type": "date" },
      "ConnectStartTime": { "type": "date" },
      "StartTime": { "type": "date" },
      "EndTime": { "type": "date" }
    }
  }
}
```

The record's `Id` belongs to its source table and is not a global Elasticsearch document identifier. Do not use it alone as `_id` across the combined categories.

## Validation

Use synthetic records to verify one-line parsing, all seven discriminator values, field names and types, nullable values, all temporal properties, UTC conversion, fractional precision, and the JSON-only EnergyUsage interpretation. Verify UTF-8, custom/default filenames, replacement of longer existing output, empty exports, and CLI option combinations.

Check Elasticsearch compatibility against its documented standard date parser and provide an explicit mapping example. A real database parse and end-to-end ingestion require representative SRUM evidence and an Elasticsearch instance; do not describe source checks or synthetic tests as live acceptance.

The host has no `dotnet` executable and the repository includes no SRUM fixtures. Builds run in Docker, while real SRUM evidence remains external to the repository. See the [validation report](../validation/json-export.md) for the executed JSON checks and their limits.

## Sources

- [MFTECmd JSON export](https://github.com/EricZimmerman/MFTECmd/blob/master/MFTECmd/Program.cs): JSON Lines, ISO 8601 configuration, `--json` and `--jsonf`.
- [Elasticsearch date fields](https://www.elastic.co/docs/reference/elasticsearch/mapping-reference/date): default date parser and millisecond indexing resolution.
- [Elasticsearch 8.19 fractional-second parser tests](https://github.com/elastic/elasticsearch/blob/8.19/server/src/test/java/org/elasticsearch/common/time/DateFormattersTests.java#L671): seven fractional ISO 8601 digits accepted by `strict_date_optional_time`.
