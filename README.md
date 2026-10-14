# AudioPlugins SyMenu Plugin

[![Suite](https://img.shields.io/badge/dynamic/regex?url=https%3A%2F%2Fraw.githubusercontent.com%2Fsl2365%2FAudioPluginsSuite%2Fmain%2FSuite%2F_latest.txt&search=%5Cd%7B4%7D%5C.%5Cd%7B2%7D%5C.%5Cd%7B2%7D&label=Suite&style=for-the-badge-square&logo=github&logoColor=white&color=yellow)](https://github.com/sl2365/AudioPluginsSuite/tree/main/Suite)

[![Latest Asset Downloads](https://img.shields.io/github/downloads/sl2365/AudioPluginsSuite/latest/SyMenu.Plugin.AudioPlugins.rar?style=for-the-badge-square&logo=github&logoColor=white&label=downloads-latest&displayAssetName=false&color=blue)](https://github.com/sl2365/AudioPluginsSuite/releases/latest)
[![All Release Downloads](https://img.shields.io/github/downloads/sl2365/AudioPluginsSuite/total?style=for-the-badge-square&logo=github&logoColor=white&label=all-release-downloads&color=blue)](https://github.com/sl2365/AudioPluginsSuite/releases)

A SyMenu plugin that keeps the custom **AudioPlugins** SPS suite definitions up to date from the
[AudioPluginsSuite](https://github.com/sl2365/AudioPluginsSuite) repository.

The plugin does **not** update individual portable applications itself. Its job is to keep the
AudioPlugins suite's SPS definition files current. SyMenu then uses those SPS definitions to
determine which installed applications have updates available and presents those updates through
the normal SPS Manager interface.

The public SPS collection lives in the repository under `Suite/`. Developers who want an SPS added
to the public collection can submit a pull request or open an issue and provide the SPS file.

The AudioPlugins suite is centrally managed. Manual additions to SyMenu's local AudioPlugins
`_Cache` are not supported and may be removed when the published SPS set is replaced during an
update.

Here's what it does:

```text
Startup/manual check
→ read online Suite/_latest.txt
→ compare dates
→ download Suite.zip if needed
→ extract with SyMenu\Lib\7z.exe
→ verify packaged _latest.txt and SPS files
→ back up previous collection
→ replace the SPS set
→ write local _latest.txt last
→ rollback on failure
→ remove temporary extraction workspace
```

## Repository layout

The repository keeps the plugin project and the public SPS suite together, but they are published
separately:

```text
AudioPluginsSuite\
├─ Suite\
│  ├─ _latest.txt
│  ├─ PluginA.sps
│  ├─ PluginB.sps
│  └─ ...
├─ .github\
│  └─ workflows\
│     └─ build-suite.yml
├─ plugin source files
└─ README.md
```

Normal GitHub releases are for the **plugin itself**. The current plugin release package is:

```text
SyMenu.Plugin.AudioPlugins.rar
```

The SPS collection is packaged automatically by GitHub Actions and published separately as
`Suite.zip` on the fixed pre-release tag `suite-current`.

## Repository endpoints

The updater uses two fixed endpoints:

- Suite collection marker:
  `https://raw.githubusercontent.com/sl2365/AudioPluginsSuite/main/Suite/_latest.txt`
- Generated suite package:
  `https://github.com/sl2365/AudioPluginsSuite/releases/download/suite-current/Suite.zip`

`Suite/_latest.txt` contains one collection date in the exact format:

```text
YYYY.MM.DD
```

Example:

```text
2026.10.13
```

`Suite.zip` is generated automatically from the contents of the repository `Suite/` folder whenever
that folder changes.

## SyMenu integration

The plugin implements:

- `ISyPluginMain`
- `ISyPluginNotificationStartup`

`ISyPluginNotificationStartup` allows the plugin to check the AudioPlugins SPS collection whenever
SyMenu starts, without requiring SyMenu's Autoexec feature.

A normal SyMenu startup check is silent when it succeeds. If the suite is already current, no
archive is downloaded and no window is shown. A first installation or SPS collection update is also
performed silently when successful.

If the computer is offline, or the online check times out, the automatic startup check also remains
silent so SyMenu startup is not interrupted merely because no internet connection is available.
Other update/install errors are still reported.

Clicking the **AudioPlugins** plugin menu entry performs a fresh online check and displays the
result. If the manual check cannot reach the online source, the user is shown a connectivity message.

The plugin also includes:

```text
AudioPlugins.Check.exe
```

When SyMenu is already running, this helper sends a local command to the loaded AudioPlugins plugin
and performs the same manual online check as **SyMenu → Plugins → AudioPlugins**. This allows the
helper EXE to be added to SyMenu as a normal portable item if preferred.

`AudioPlugins.Check.exe` does not contain a second updater implementation. SyMenu and the plugin must
be running because the EXE only signals the already-loaded plugin.

The plugin does not hook SyMenu's SPS Manager refresh button. Manual online refresh is provided by
the plugin menu item or `AudioPlugins.Check.exe`.

## Suite location

The plugin derives the SyMenu root from its own DLL location rather than using a drive letter or an
absolute installation path.

Expected plugin location:

```text
SyMenu\Plugin\SyMenu.Plugin.AudioPlugins\SyMenu.Plugin.AudioPlugins.dll
```

The companion launcher can be placed beside it:

```text
SyMenu\Plugin\SyMenu.Plugin.AudioPlugins\AudioPlugins.Check.exe
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
            ├─ AudioPlugins_2026.10.13.zip
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
3. Download/read `Suite/_latest.txt`.
4. Validate the value as an exact `YYYY.MM.DD` date.
5. Read `_Cache\_latest.txt`, if present.
6. Compare the local collection date with the online collection date.
7. If both dates match, stop. `Suite.zip` is not downloaded.
8. If no local marker exists:
   - if `_Cache` contains no SPS files, treat the operation as a first installation;
   - if SPS files already exist, treat them as an unversioned existing suite, back them up, and
     migrate them to the current published collection.
9. If the online collection is newer:
   - download the fixed `Suite.zip` asset from the `suite-current` pre-release;
   - initially save it with a temporary `.download` suffix;
   - after a successful download, retain it in `_Trash` using the collection date, for example
     `AudioPlugins_2026.10.13.zip`;
   - extract it with SyMenu's `Lib\7z.exe` into `_Trash\_ExtractWork`;
   - verify that the package contains `_latest.txt`;
   - verify that the packaged `_latest.txt` matches the online collection date;
   - verify that at least one SPS file was extracted;
   - copy the previous SPS definitions and previous `_latest.txt` into a backup directory under
     `_Trash`, named with the previous local collection date;
   - if that backup directory name already exists, choose a unique suffix such as `_2`, `_3`, and so on;
   - replace the active SPS set in `_Cache`;
   - write the new collection date to `_Cache\_latest.txt` only after the SPS files have been
     installed successfully;
   - remove `_ExtractWork`.
10. SyMenu reads the updated SPS definitions and performs its normal per-application version
    comparison and update presentation.

If the local collection date is newer than the published collection date, the plugin reports
`LOCAL NEWER` during a manual check and makes no changes.

Only one suite-definition transaction can run at a time. Startup, the plugin menu, and
`AudioPlugins.Check.exe` all share the same concurrency guard.

## First installation

For a clean first installation, the plugin:

1. Creates the AudioPlugins suite directory structure if needed.
2. Downloads and validates the current `Suite.zip`.
3. Extracts and verifies the collection marker and SPS files.
4. Installs the SPS files into `_Cache`.
5. Writes `_Cache\_latest.txt` last.

The local marker is therefore not created unless the SPS installation has completed successfully.

## Existing unversioned suite migration

If `_Cache` already contains SPS files but `_latest.txt` is missing, the plugin does not overwrite
those files as a normal first installation. It treats them as an existing unversioned suite.

The existing SPS files are backed up under `_Trash` using a name such as:

```text
Unversioned_2026.10.13
```

If that name already exists, the plugin selects a unique suffix such as `_2`, `_3`, and so on. The
current published SPS collection is then installed and `_Cache\_latest.txt` is written last. If the
migration fails after the backup is complete, the previous unversioned SPS files are restored.

## Failure handling and rollback

The local `_latest.txt` marker is never advanced until the new SPS collection has been installed
successfully.

If the online marker contains an invalid date, the archive download fails, the archive cannot be
extracted, the packaged marker does not match the published collection date, or no SPS files are
found, the active SPS collection is not advanced.

A downloaded dated ZIP that fails extraction/validation is removed so the next check can download a
fresh copy instead of repeatedly retrying the same bad archive.

Loss of internet connectivity or an online-check timeout is silent during automatic SyMenu startup.
The same condition is reported when the user explicitly runs a manual AudioPlugins check.

For an existing installation, the previous SPS files and `_latest.txt` are backed up before the live
SPS set is replaced. If replacement fails after that backup is complete, the plugin attempts to
restore the previous SPS files and marker automatically.

If both the update and the automatic rollback fail, the plugin reports both errors and leaves the
dated backup directory available for manual recovery.

## Critical error logging

The plugin keeps routine successful checks silent and does not create a general activity log.

Unexpected or potentially persistent failures are appended to:

```text
SyMenu\Plugin\SyMenu.Plugin.AudioPlugins\AudioPlugins.log
```

Each entry includes the timestamp, plugin version, whether the failure occurred during an automatic
startup check or a manual check, the plugin DLL path, the exception type, and the full exception
details/stack trace.

Ordinary loss of internet connectivity or an online-check timeout is not logged as a critical error.
Those conditions remain silent during automatic SyMenu startup and produce the normal connectivity
message only when the user explicitly runs a manual AudioPlugins check.

Logging is best-effort. A failure to write the diagnostic log never replaces or hides the original
plugin error.

## Suite package handling

The published SPS collection is distributed as:

```text
Suite.zip
```

The ZIP is not built manually. GitHub Actions creates it automatically from the repository `Suite/`
folder and uploads/replaces it on the fixed `suite-current` pre-release.

The plugin uses the `7z.exe` already distributed with SyMenu:

```text
SyMenu\Lib\7z.exe
```

No separate archive utility is required on the user's machine.

## Publishing a suite-definition update

The maintainer's normal publishing process is:

1. Add, remove, or update the required `.sps` files under `Suite/`.
2. Update `Suite/_latest.txt` to the new collection date in exact `YYYY.MM.DD` format.
3. Commit/push the changes to `main`.
4. GitHub Actions automatically validates the Suite folder, builds `Suite.zip`, and replaces the
   package on the `suite-current` pre-release.

No manual ZIP creation or release-asset replacement is required for normal SPS collection updates.

On the next SyMenu startup or manual plugin check, the plugin compares the new collection date with
the locally installed collection and updates the SPS definitions when required.

## Contributing SPS files

The `Suite/` folder is the authoritative public SPS collection.

A developer who wants an SPS definition included for all AudioPlugins users can:

1. Fork the repository.
2. Add or update the `.sps` file under `Suite/`.
3. Submit a pull request.

Alternatively, a developer can open an issue and provide the SPS file for review.

After an accepted change is merged and `Suite/_latest.txt` is advanced, GitHub Actions automatically
publishes the refreshed `Suite.zip`.

## Plugin releases

Normal GitHub releases are reserved for the plugin itself.

The current plugin release is version:

```text
1.1.0
```

and the release asset is:

```text
SyMenu.Plugin.AudioPlugins.rar
```

The plugin release package contains the built plugin and companion manual-check launcher.

The automatically generated `suite-current` pre-release is separate from the normal plugin release
and exists only to provide the current `Suite.zip` to installed plugins.

## Build

The plugin targets:

```text
net10.0-windows
```

The assembly is explicitly marked as Windows-only with
`[assembly: SupportedOSPlatform("windows")]`, matching SyMenu's platform requirement and preventing
spurious platform-analyzer warnings for Windows Forms and `System.Drawing` APIs.

Development uses a portable .NET 10 SDK rather than requiring a system-wide SDK installation.

Current project SDK layout:

```text
_Projects\
├─ _Tools\
│  └─ dotNet\
│     └─ _10.0.401\
│        └─ dotnet.exe
└─ AudioPlugins\
   ├─ CheckLauncher\
   │  ├─ AudioPlugins.Check.csproj
   │  └─ Program.cs
   ├─ Version.props
   └─ - Build.bat
```

`Version.props` is shared by the plugin and launcher so both outputs use the same release version.

`- Build.bat` uses the SDK by relative path, writes its build output to `Results.log`, and produces:

```text
Build\SyMenu.Plugin.AudioPlugins.dll
Build\AudioPlugins.Check.exe
```

The SyMenu interface assembly is referenced while building but is not distributed beside the plugin
DLL because SyMenu supplies `SyMenu.Plugin.Resources.dll` itself.

## Current status

Confirmed working:

- portable .NET 10 build environment;
- shared plugin/launcher versioning;
- plugin compilation and SyMenu discovery/loading;
- normal plugin menu callback;
- `ISyPluginNotificationStartup`;
- companion `AudioPlugins.Check.exe` manual launcher;
- named-pipe communication from the launcher to the loaded plugin;
- single-check concurrency protection;
- silent successful startup checks;
- manual plugin checks with visible status;
- automatic derivation of the SyMenu root from the plugin DLL location;
- automatic creation of the AudioPlugins suite structure;
- online `Suite/_latest.txt` retrieval and strict `YYYY.MM.DD` validation;
- `FIRST INSTALL`, `UPDATE AVAILABLE`, `UP TO DATE`, and `LOCAL NEWER` detection;
- automatic GitHub Actions packaging of the repository `Suite/` folder;
- fixed `suite-current` pre-release containing `Suite.zip`;
- dated `Suite.zip` download with temporary `.download` protection;
- extraction through SyMenu's bundled `Lib\7z.exe`;
- packaged marker/date validation;
- SPS archive-content verification;
- first installation;
- dated backup of the previous SPS collection;
- replacement of the active SPS set;
- local marker update only after successful SPS installation;
- automatic rollback after update failure;
- automatic cleanup of the extraction workspace after success;
- protected migration of pre-existing SPS files when `_latest.txt` is missing;
- unique backup-folder naming when a previous backup already exists;
- automatic removal of a failed/corrupt dated ZIP so a later check can re-download it;
- silent offline/timeout handling during automatic startup checks;
- visible connectivity errors for manual checks;
- error-only critical diagnostic logging beside the plugin DLL.

The core plugin and automated SPS suite publishing workflow are complete.
