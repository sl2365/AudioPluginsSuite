# AudioPlugins SyMenu Plugin

[![Latest](https://img.shields.io/badge/dynamic/regex?url=https%3A%2F%2Fraw.githubusercontent.com%2Fsl2365%2FAudioPluginsSuite%2Fmain%2F_latest.txt&search=%5Cd%7B4%7D%5C.%5Cd%7B2%7D%5C.%5Cd%7B2%7D&label=Latest&style=for-the-badge-square&logo=github&logoColor=white&color=2481A8)](https://github.com/sl2365/AudioPluginsSuite/releases/latest/download/AudioPlugins.rar)

[![Total Downloads](https://img.shields.io/github/downloads/sl2365/AudioPluginsSuite/total?style=for-the-badge-square&logo=github&logoColor=white&label=downloads-total&color=blue)](https://github.com/sl2365/AudioPluginsSuite/releases)

A SyMenu plugin that keeps the custom **AudioPlugins** SPS suite definitions up to date from the
[AudioPluginsSuite](https://github.com/sl2365/AudioPluginsSuite) repository.

The plugin does **not** update individual portable applications itself. Its job is to keep the
AudioPlugins suite's SPS definition files current. SyMenu then uses those SPS definitions to
determine which installed applications have updates available and presents those updates through
the normal SPS Manager interface.

This is a collection of SPS files used with SyMenu to allow auto-install and update of the listed plugins. At present, only my own releases are here, but this will hopefully be expanded to include other devs SPS files if I can get them interested in adding this little extra task to their dev time. Anyone interested, feel free to contact me by creating a PR or issue.

Here's what it does:

```text
Startup/manual check
→ read online _latest.txt
→ compare dates
→ download dated RAR if needed
→ extract with SyMenu\Lib\7z.exe
→ verify SPS files
→ back up previous collection
→ replace the SPS set
→ write _latest.txt last
→ rollback on failure
→ remove temporary extraction workspace
```

## Repository endpoints

The updater uses two fixed endpoints:

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

The release archive keeps the permanent filename `AudioPlugins.rar`, so the plugin's download URL
does not need to change when a new SPS collection is published.

## SyMenu integration

The plugin implements:

- `ISyPluginMain`
- `ISyPluginNotificationStartup`

`ISyPluginNotificationStartup` allows the plugin to check the AudioPlugins SPS collection whenever
SyMenu starts, without requiring SyMenu's Autoexec feature.

A normal SyMenu startup check is silent when it succeeds. If the suite is already current, no
archive is downloaded and no window is shown. A first installation or SPS collection update is also
performed silently when successful. Errors are shown to the user.

Clicking the **AudioPlugins** plugin menu entry performs a fresh online check and displays the
result. This provides a manual suite-definition refresh independently of SyMenu startup.

A separate SyMenu notification for the SPS Manager's suite-specific manual refresh button has been
requested from the SyMenu developer. If such an interface is added, the same updater routine can be
called immediately before SyMenu refreshes the AudioPlugins suite.

## Suite location

The plugin derives the SyMenu root from its own DLL location rather than using a drive letter or an
absolute installation path.

Expected plugin location:

```text
SyMenu\Plugin\SyMenu.Plugin.AudioPlugins\SyMenu.Plugin.AudioPlugins.dll
```

The AudioPlugins suite is located at:

```text
SyMenu\ProgramFiles\SPSSuite\AudioPlugins
```

If the suite does not already exist, the plugin creates the suite directory together with `_Cache`
and `_Trash`. SyMenu automatically exposes a newly created suite directory in SPS Manager.

Current suite structure:

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
            │  ├─ _latest.txt
            │  ├─ old PluginA.sps
            │  └─ old PluginB.sps
            └─ ...
```

During an install/update, `_Trash\_ExtractWork` is used as a temporary extraction workspace. It is
deleted automatically after a successful operation. If an operation fails, it may remain available
for troubleshooting and is cleared before the next extraction attempt.

## Update process

The update transaction is:

1. Determine the SyMenu root from the loaded plugin DLL.
2. Ensure these directories exist:
   - `ProgramFiles\SPSSuite\AudioPlugins`
   - `ProgramFiles\SPSSuite\AudioPlugins\_Cache`
   - `ProgramFiles\SPSSuite\AudioPlugins\_Trash`
3. Download/read the online `_latest.txt`.
4. Validate the value as an exact `YYYY.MM.DD` date.
5. Read `_Cache\_latest.txt`, if present.
6. Compare the local collection date with the online collection date.
7. If both dates match, stop. No RAR is downloaded.
8. If no local marker exists, treat the operation as a first installation.
9. If the online collection is newer:
   - download the fixed `AudioPlugins.rar` release asset;
   - initially save it with a temporary `.download` suffix;
   - after a successful download, retain it in `_Trash` using the collection date, for example
     `AudioPlugins_2026.10.13.rar`;
   - extract it with SyMenu's `Lib\7z.exe` into `_Trash\_ExtractWork`;
   - verify that at least one SPS file was extracted;
   - copy the previous SPS definitions and previous `_latest.txt` into a backup directory under
     `_Trash`, named with the previous local collection date;
   - replace the active SPS set in `_Cache`;
   - write the new collection date to `_Cache\_latest.txt` only after the SPS files have been
     installed successfully;
   - remove `_ExtractWork`.
10. SyMenu reads the updated SPS definitions and performs its normal per-application version
    comparison and update presentation.

If the local collection date is newer than the published collection date, the plugin reports
`LOCAL NEWER` during a manual check and makes no changes.

## First installation

For a clean first installation, the plugin:

1. Creates the AudioPlugins suite directory structure if needed.
2. Downloads and validates the current RAR.
3. Extracts and verifies the SPS files.
4. Installs the SPS files into `_Cache`.
5. Writes `_Cache\_latest.txt` last.

The local marker is therefore not created unless the SPS installation has completed successfully.

## Failure handling and rollback

The local `_latest.txt` marker is never advanced until the new SPS collection has been installed
successfully.

If the online marker cannot be read, contains an invalid date, the archive download fails, the
archive cannot be extracted, or no SPS files are found, the active SPS collection is not advanced.

For an existing installation, the previous SPS files and `_latest.txt` are backed up before the live
SPS set is replaced. If replacement fails after that backup is complete, the plugin attempts to
restore the previous SPS files and marker automatically.

If both the update and the automatic rollback fail, the plugin reports both errors and leaves the
dated backup directory available for manual recovery.

## RAR handling

The published suite collection is intentionally distributed as `AudioPlugins.rar`.

The plugin does not require WinRAR to be installed on the user's machine. It uses the `7z.exe`
already distributed with SyMenu:

```text
SyMenu\Lib\7z.exe
```

The plugin only needs archive extraction. Archive creation remains part of the maintainer's existing
WinRAR publishing workflow.

## Publishing a suite-definition update

The maintainer's publishing process is:

1. Update the required master SPS files.
2. Create a new `AudioPlugins.rar`.
3. Replace the `AudioPlugins.rar` asset on the current/latest GitHub release.
4. Update repository `_latest.txt` to the new collection date in `YYYY.MM.DD` format.
5. Commit/publish `_latest.txt`.

On the next startup or manual plugin check, the plugin compares the new date with the locally
installed collection and updates the SPS definitions when required.

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

## Current status

Confirmed working:

- portable .NET 10 build environment;
- plugin compilation and SyMenu discovery/loading;
- normal plugin menu callback;
- `ISyPluginNotificationStartup`;
- silent successful startup checks;
- manual plugin checks with visible status;
- automatic derivation of the SyMenu root from the plugin DLL location;
- automatic creation of the AudioPlugins suite structure;
- online `_latest.txt` retrieval and strict `YYYY.MM.DD` validation;
- `FIRST INSTALL`, `UPDATE AVAILABLE`, `UP TO DATE`, and `LOCAL NEWER` detection;
- static GitHub release-asset download with temporary `.download` protection;
- extraction through SyMenu's bundled `Lib\7z.exe`;
- SPS archive-content verification;
- first installation;
- dated backup of the previous SPS collection;
- replacement of the active SPS set;
- local marker update only after successful SPS installation;
- automatic rollback after update failure;
- automatic cleanup of the extraction workspace after success.

The remaining work is final user-facing polish, compatibility testing, and integration with any future
SyMenu notification for its suite-specific SPS Manager refresh action.
