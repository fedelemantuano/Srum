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
