# Linux Docker validation — 2026-10-12

The image was built and run on Docker Desktop on an Apple M4 Mac, targeting Linux ARM64. No Windows machine, VM, native ESE recovery, or transaction-log replay was used.

## Real evidence

Both directories supplied by the user under `BelkasoftCTF_InsiderThreat` were mounted read-only and processed separately. Container runs used a non-root user, a read-only root filesystem, dropped capabilities, a temporary `/tmp` mount, and `--network none`. Database contents and exported evidence are not committed to this repository.

| Sample | ESE state | Exported activity records | Row decoding errors | Exit |
| --- | --- | ---: | ---: | ---: |
| Clean | CleanShutdown (3) | 6,811 | 0 | 0 |
| Dirty | DirtyShutdown (2) | 6,811 | 0 | 2 |

Each export contains 4,107 TimelineProvider, 2,087 AppResourceUseInfo, 465 NetworkUsage, 101 Vfuprov, 50 NetworkConnection and 1 EnergyUsage records. The PushNotification table is absent in both databases. All 476 ID mapping rows were also decoded. No SOFTWARE hive was present in either directory, so external registry enrichment was not tested.

The JSON Lines outputs are byte-identical. The diagnostic reports differ in database state, input hash, completeness flag, and exit status. Identical exported pages do not prove that replaying logs would leave the data unchanged.

Input SHA-256 values, verified against the originals after parsing:

```text
Clean/SRUDB.dat b2c06003c6763b1f15272381f5d3f077264168975ee0aa8d08bac92e1c99e796
Dirty/SRUDB.dat f96ebb82bb40c3581ff02e2ec4713c1156891d7bf3ec1f7b9a967eb630443661
```

Direct comparison with ESE column bytes checked 69,653 numeric/timestamp values per sample across all 6,811 records. This validates the C# conversion against the raw values returned by Dissect, including SRUM OLE Automation timestamps and FILETIME fields; it is not an independent Windows-engine comparison.

## Automated checks

```sh
docker build --target test -t srumecmd-test:local .
docker run --rm --entrypoint dotnet \
  -e SRUM_TEST_SAMPLE_ROOT=/samples \
  -v /absolute/path/BelkasoftCTF_InsiderThreat:/samples:ro \
  srumecmd-test:local test SrumTest/SrumTest.csproj -c Release --no-build --no-restore
```

The test stage runs the Python binary-transport tests, .NET tests for mapping all supported categories, mixed timestamp encodings, malformed rows, optional/required tables, CLI validation, subprocess failure/timeout/truncation, and export protection. It builds both .NET 9 and .NET Framework 4.7.2. Without private samples, the sample integration test is skipped; the original hard-coded Windows test remains explicit. No real Windows ESENT regression test or live Kubernetes deployment is claimed.

The Dockerfile supports Linux ARM64 and AMD64 build targets; the initial real-evidence runtime validation was performed on ARM64.
