# Desktop compatibility verification

These checks compare the package split with an unmodified desktop package built
before the split. Keep the baseline package outside source control, for example
under `artifacts/android-port/baseline-feed`. The initial development baseline is
`CodeBrix.Audio.MitLicenseForever.1.0.269.1.nupkg`.

1. Build the coordinated packages with a single `BuildVersion` and copy the desktop,
   Core and ModestSynth packages (the three the script checks) to a local feed.
   See the Android repository's `tools/build-local.sh`.
2. Run `python3 tools/verify-packages/verify_packages.py BASELINE_NUPKG FEED VERSION`.
   It checks Core assembly ownership, all dependency edges, and byte identity of
   every desktop native binary and adjacent license file.
3. Extract `lib/net10.0/CodeBrix.Audio.dll` and `CodeBrix.Audio.Engine.dll` from the
   baseline and Core packages into separate baseline/current directories. Run:

   ```bash
   dotnet msbuild tools/verify-packages/ApiCompatibility.proj -t:Verify \
     -p:BaselineDirectory=/absolute/baseline -p:CurrentDirectory=/absolute/current
   ```

   This uses the .NET SDK API compatibility task, including parameter-name checks.
4. Build `LegacyConsumer/LegacyConsumer.csproj` against the baseline feed with
   `-p:DesktopPackageVersion=BASELINE_VERSION`, then run it. Keep the resulting
   consumer DLL and deps.json unchanged, replace only the two CodeBrix managed DLLs
   with the Core versions, and run again. It exercises WAV writing/decoding and
   lazy shared-output configuration without opening a physical audio device.
5. Separately rebuild the same source with only the existing Desktop package
   reference upgraded to the current version. Run it to verify that NuGet resolves
   Core assemblies and Desktop native assets transitively with no new references.

These are host checks. They do not establish runtime validation on every desktop
OS/RID. The unchanged native assets and API/binary checks reduce compatibility
risk; platform-specific smoke tests remain part of normal release validation.
