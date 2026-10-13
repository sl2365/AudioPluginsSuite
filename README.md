# AudioPlugins SyMenu Plugin

[![Release](https://img.shields.io/badge/dynamic/regex?url=https%3A%2F%2Fraw.githubusercontent.com%2Fsl2365%2FAudioPluginsSuite%2Fmain%2F_latest.txt&search=%5Cd%7B4%7D%5C.%5Cd%7B2%7D%5C.%5Cd%7B2%7D&label=release&style=for-the-badge&logo=github&logoColor=white&color=purple)](https://github.com/sl2365/AudioPluginsSuite/releases/latest/download/AudioPlugins.rar)

[![Total Downloads](https://img.shields.io/github/downloads/sl2365/AudioPluginsSuite/total?style=for-the-badge-square&logo=github&logoColor=white&label=downloads-total&color=blue)](https://github.com/sl2365/AudioPluginsSuite/releases)

A SyMenu plugin that keeps the custom **AudioPlugins** SPS suite definitions up to date from the
[AudioPluginsSuite](https://github.com/sl2365/AudioPluginsSuite) repository.

The plugin does **not** update individual portable applications itself. Its job is to keep the
AudioPlugins suite's SPS definition files current. SyMenu then uses those SPS definitions to
determine which installed applications have updates available and presents those updates through
the normal SPS Manager interface.

This is a collection of SPS files used with SyMenu to allow auto-install and update of the listed plugins. At present, only my own releases are here, but this will hopefully be expanded to include other devs SPS files if I can get them interested in adding this little extra task to their dev time. Anyone interested, feel free to contact me by creating a PR or issue.

## Repository endpoints

The update mechanism uses two fixed endpoints:

- Suite collection marker:
  `https://raw.githubusercontent.com/sl2365/AudioPluginsSuite/main/_latest.txt`
- Current suite archive:
  `https://github.com/sl2365/AudioPluginsSuite/releases/latest/download/AudioPlugins.rar`

`_latest.txt` contains one collection date in the exact format:

```text
YYYY.MM.DD
```

Example:

```text
2026.10.13
```

The release archive keeps the permanent filename `AudioPlugins.rar`. The release asset can therefore
be replaced without changing the URL used by the plugin.

## SyMenu integration

The plugin implements SyMenu's normal plugin interface and the startup notification interface:

- `ISyPluginMain`
- `ISyPluginNotificationStartup`

`ISyPluginNotificationStartup` allows the plugin to perform its suite-definition check whenever
SyMenu starts, without requiring SyMenu's Autoexec feature.

The plugin also has its normal SyMenu plugin menu entry. This provides a manual way to run/check the
AudioPlugins updater independently.

A separate SyMenu notification for the SPS Manager's suite-specific manual refresh button has been
requested from the SyMenu developer. If such an interface is added, the plugin can use the same
update routine immediately before SyMenu performs its normal refresh of the AudioPlugins suite.

## Suite location

The plugin derives the SyMenu root from its own DLL location rather than using a drive letter or
absolute path.

Expected plugin location:

```text
SyMenu\Plugin\SyMenu.Plugin.AudioPlugins\SyMenu.Plugin.AudioPlugins.dll
```

The AudioPlugins suite is located at:

```text
SyMenu\ProgramFiles\SPSSuite\AudioPlugins
```

The plugin creates the suite directory and required subdirectories when they do not already exist.
SyMenu automatically exposes a newly created suite directory in SPS Manager.

Planned suite structure:

```text
SyMenu\
└─ ProgramFiles\
   └─ SPSSuite\
      └─ AudioPlugins\
         ├─ _Cache\
         │  ├─ _latest.txt
         │  ├─ PluginA.sps
         │  ├─ PluginB.sps
         │  └─ ...
         │
         └─ _Trash\
            ├─ AudioPlugins_2026.10.13.rar
            ├─ 2026.09.28\
            │  ├─ old PluginA.sps
            │  └─ old PluginB.sps
            └─ ...
```

## Update process

The intended update transaction is:

1. Determine the SyMenu root from the loaded plugin DLL.
2. Ensure these directories exist:
   - `ProgramFiles\SPSSuite\AudioPlugins`
   - `ProgramFiles\SPSSuite\AudioPlugins\_Cache`
   - `ProgramFiles\SPSSuite\AudioPlugins\_Trash`
3. Download/read the online `_latest.txt`.
4. Validate the value as an exact `YYYY.MM.DD` date.
5. Read `_Cache\_latest.txt`, if present.
6. Compare the local collection date with the online collection date.
7. If the dates match, stop. No archive is downloaded.
8. If no local marker exists, treat the operation as a first installation.
9. If the online collection is newer:
   - download the fixed `AudioPlugins.rar` release asset;
   - store it in `_Trash` using the collection date, for example
     `AudioPlugins_2026.10.13.rar`;
   - verify that the archive can be successfully extracted before replacing the active SPS set;
   - move the previous SPS definitions into a dated backup directory under `_Trash`, using the
     previous local collection date;
   - extract/install the new SPS definitions into `_Cache`;
   - write the new collection date to `_Cache\_latest.txt` only after the update completes
     successfully.
10. SyMenu reads the updated SPS definitions and performs its normal per-application version
    comparison and update presentation.

## Failure handling

The local `_latest.txt` marker must never be advanced until the new SPS collection has been installed
successfully.

If the online marker cannot be read, contains an invalid date, the archive download fails, or
extraction fails, the current `_Cache` SPS definitions remain the active definitions and the next
check can retry the update.

The existing SPS files are preserved under `_Trash` before replacement so a previous collection is
available for recovery.

## RAR handling

The published suite collection is intentionally distributed as `AudioPlugins.rar`.

The plugin does not require WinRAR to be installed on the user's machine. The planned implementation
uses the `7z.exe` already distributed with SyMenu to extract the RAR archive.

The plugin only needs archive extraction; archive creation remains part of the maintainer's existing
WinRAR publishing workflow.

## Publishing a suite-definition update

The maintainer's publishing process is intended to be:

1. Update the required master SPS files.
2. Create a new `AudioPlugins.rar`.
3. Replace the `AudioPlugins.rar` asset on the current/latest GitHub release.
4. Update repository `_latest.txt` to the new collection date in `YYYY.MM.DD` format.
5. Commit/publish `_latest.txt`.

The plugin then detects the newer collection automatically on the next supported check.

## Build

The plugin targets:

```text
net10.0-windows
```

Development uses a portable .NET 10 SDK rather than requiring a system-wide SDK installation.

Current project SDK layout:

```text
_Projects\
├─ _Tools\
│  └─ dotNet\
│     └─ _10.0.401\
│        └─ dotnet.exe
└─ AudioPlugins\
   └─ - Build.bat
```

`- Build.bat` uses the SDK by relative path and writes its build output to `Results.log`.

The SyMenu interface assembly is referenced while building but is not distributed beside the plugin
DLL because SyMenu supplies `SyMenu.Plugin.Resources.dll` itself.

## Current development status

Confirmed working:

- portable .NET 10 build environment;
- plugin compilation;
- SyMenu plugin discovery/loading;
- normal plugin menu callback;
- `ISyPluginNotificationStartup`;
- automatic derivation of the SyMenu root from the plugin DLL location;
- discovery of the AudioPlugins `_Cache` path.

Current implementation stage being tested:

- automatic creation of the AudioPlugins suite directory structure;
- read-only retrieval and validation of the online `_latest.txt`;
- read-only comparison with `_Cache\_latest.txt`;
- reporting `FIRST INSTALL`, `UPDATE AVAILABLE`, `UP TO DATE`, or `LOCAL NEWER`.

This stage does not download the RAR, replace SPS files, or write the local marker. Archive
download/extraction and SPS replacement are intentionally being added only after this stage is
verified.
