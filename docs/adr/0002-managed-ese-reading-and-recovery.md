# Include one C# ESE reader in Srum

Srum includes the C# source from the local `EseRecovery` implementation and references that project directly. Every supported platform uses this single reader for clean databases and for automatic, bounded transaction recovery when matching adjacent logs are required. Python, Dissect, ManagedEsent, `esent.dll`, public backend selection, and external build contexts are removed.

The application keeps the CLI and export contract introduced by commit `78574ae`, including CSV and JSON Lines. Recovery limitations stop processing before a new export is written; unrecovered stored-page output is not exposed. The included source must be synchronized deliberately with the copy used by Sum.

This removes runtime and package-feed dependencies and makes the Docker build self-contained. It also means Srum is limited to the ESE formats and recovery operations implemented by the included library; it does not claim complete Windows ESE equivalence.
