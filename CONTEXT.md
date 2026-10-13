# SRUM

SRUM records Windows resource usage and activity. This tool extracts those records from a database, optionally enriching them with information from a SOFTWARE registry hive.

## Language

**SRUM database**:
The SRUDB.dat evidence file containing resource usage and activity records.

**SRUM record**:
One entry in a SRUM activity or resource usage table, associated with its recorded time and available application and user information.

**SRUM record type**:
The activity or resource usage category of a SRUM record. The supported categories are EnergyUsage, TimelineProvider, Vfuprov, AppResourceUseInfo, NetworkConnection, NetworkUsage, and PushNotification.

**SOFTWARE hive**:
The Windows registry evidence file used to enrich SRUM records with application, user, and network profile information.

**ESE transaction log**:
A journal of database operations and transaction boundaries associated with the acquired SRUM database.

**Transactionally consistent state**:
A database state that reflects supported committed transactions through the selected recovery endpoint and excludes supported changes from transactions left uncommitted at that endpoint.

**Supported ESE recovery**:
Reconstruction of a transactionally consistent state from the database and all required, valid, matching transaction logs for operations implemented by the included C# reader. This does not mean recovering deleted records or supporting every operation in Windows ESE.

**Recovery limitation**:
A condition such as missing logs, invalid evidence, or an unsupported operation that prevents the reader from establishing a supported transactionally consistent state. SrumECmd reports the condition and stops before export.
