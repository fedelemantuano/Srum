# Unified JSON Lines export

The JSON export will collect all seven supported SRUM record categories in one file, with one JSON object per line and an additional field identifying the record type. This makes the records available for ingestion together and filtering by category in Elasticsearch; separate files per category were considered, but the user chose a unified file.

ISO 8601 timestamp formatting applies to JSON only. Existing CSV formatting and its date-format option remain unchanged.

The discriminator is `RecordType`, using the seven record category names. JSON dates use UTC with a trailing `Z` and seven fractional digits, preserving the model's precision while remaining compatible with Elasticsearch's standard date parser. Nullable properties belonging to a record are included as `null` when absent.

The user confirmed the detailed [export contract](../design/json-export.md) and authorized implementation and Docker validation.
