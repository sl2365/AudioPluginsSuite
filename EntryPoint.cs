//--------------------------------------------------------------------------------------------------------------
// Created by		: 
// Company			: 
// Copyright		: 
//--------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyMenu.Plugin.Resources.V01.Interfaces;

// SyMenu itself is Windows-only. Marking the assembly as Windows-only also tells the .NET
// platform analyzer that Windows Forms and System.Drawing calls are intentional here.
[assembly: SupportedOSPlatform("windows")]

namespace SyMenu.Plugin.AudioPlugins
{
	/// <summary>
	/// This class implements the ISyPluginMain interface that is the communication channel between SyMenu and its plugins.
	/// ISyPluginMain resides in the assembly SyMenu.Plugin.Resources.dll in the namespace SyMenu.Plugin.Resources.Interface.Vxx. 
	/// This assembly is released with SyMenu from version 2.00.
	/// When you build your plugin, copy the dll file inside the folder:
	/// Plugin\[dll name]\[your plugin.dll] 
	/// located on the root of SyMenu application and run SyMenu but do not include the SyMenu.Plugin.Resources.dll.
	/// </summary>
	public class EntryPoint : 
		ISyPluginMain, // Mandatory interface. Makes SyMenu know that the plugin exists and who it is
		//ISyPluginNotificationExecution, // Optional interface. The plugin is notified whenever a SyItem is launched.
		//ISyPluginNotificationSaveConfiguration // Optional interface. The plugin is notified when the user saves a configuration for SyMenu or for SyItems.
		//ISyPluginNotificationOpenFolder // Optional interface. The plugin is notified when the user opens a folder.
		ISyPluginNotificationStartup // Optional interface. The plugins is notified when SyMenu starts.
	{
		// External manual checks use a local named pipe. The pipe name is derived from
		// the plugin folder so separate portable SyMenu installations do not collide.
		private const string commandPipePrefix =
			"SyMenu.AudioPlugins.Command.";

		// Only one suite-definition transaction may run at a time, regardless of
		// whether it came from startup, the SyMenu plugin menu, or the helper EXE.
		private int suiteCheckInProgress = 0;

		// The listener is started once after SyMenu's normal startup check completes.
		private int commandListenerStarted = 0;

		#region ISyPluginMain Members

		/// <summary>
		/// It's the plugin name.
		/// Must be a string not longer than 35 characters.
		/// </summary>
		public string Name{get;set;}

		/// <summary>
		/// Optional. It is shown in a tooltip.
		/// Must be a string not longer than 10 characters.
		/// </summary>
		public string Version { get; set; }
		
		/// <summary>
		/// Optional. It is shown in a tooltip.
		/// Must be a string not longer than 20 characters.
		/// </summary>
		public string Author { get; set; }
		
		/// <summary>
		/// Optional description. It is shown in a tooltip.
		/// Must be a string not longer than 100 characters.
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// The icon to be shown in the menu entry. If you have no icon leave it to null.
		/// </summary>
		public Icon MenuIcon { get; set; }

		/// <summary>
		/// The event handler called when a user click on your plugin menu entry.
		/// Must point to a method.
		/// </summary>
		public EventHandler PluginEventHandler { get; set; }

		#endregion

		#region ISyPluginNotificationStartup Members

		/// <summary>
		/// This delegate will be called whenever SyMenu starts.
		/// </summary>
		public SyMenuStartupDelegate OnStartup { get; set; }

		#endregion

		/// <summary>
		/// Constructor.
		/// [You can give proper values to the properties here]
		/// </summary>
		public EntryPoint()
		{
			// Metadata shown by SyMenu for this plugin.
			Name = "AudioPlugins";
			// Keep SyMenu's displayed plugin version tied to the DLL assembly version.
			// The assembly version is defined once in Version.props and shared with the launcher.
			Version = Assembly
				.GetExecutingAssembly()
				.GetName()
				.Version?
				.ToString(3) ?? "Unknown";
			Author = "sl23";
			Description = "Keeps the AudioPlugins SPS suite definitions up to date.";

			// Load the icon embedded in this assembly for the SyMenu plugin menu entry.
			Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SyMenu.Plugin.AudioPlugins.AppIcons.pluginIcon.ico");
			MenuIcon = new Icon(iconStream);

			// Register the normal menu click and SyMenu startup callbacks.
			PluginEventHandler = pluginClick;
			
			OnStartup = syMenuStartup;
		}
		
		/// <summary>
		/// This is the method SyMenu calls when the user clicks on the plugin menu item.
		/// </summary>
		public void pluginClick(object sender, System.EventArgs e)
		{
			runSuiteCheck(true);
		}

		/// <summary>
		/// Starts a suite-definition check only when another check is not already running.
		/// Manual requests receive a short message when an existing transaction is busy.
		/// </summary>
		private void runSuiteCheck(bool showResult)
		{
			if (Interlocked.CompareExchange(
				ref suiteCheckInProgress,
				1,
				0) != 0)
			{
				if (showResult)
				{
					MessageBox.Show(
						"An AudioPlugins update check is already in progress.",
						"AudioPlugins"
					);
				}

				return;
			}

			performReservedSuiteCheck(showResult);
		}

		/// <summary>
		/// Runs a check after ownership of suiteCheckInProgress has already been acquired.
		/// The guard is always released, including when the updater reports an error.
		/// </summary>
		private void performReservedSuiteCheck(bool showResult)
		{
			try
			{
				checkSuiteDefinitions(showResult);
			}
			finally
			{
				Interlocked.Exchange(
					ref suiteCheckInProgress,
					0
				);
			}
		}

		/// <summary>
		/// Creates a deterministic named-pipe name for this specific portable plugin folder.
		/// The helper EXE uses the same calculation, so it can only signal its neighbouring plugin.
		/// </summary>
		private static string getCommandPipeName(
			string pluginFolder)
		{
			string normalizedPluginFolder =
				Path.GetFullPath(pluginFolder)
					.TrimEnd(
						Path.DirectorySeparatorChar,
						Path.AltDirectorySeparatorChar
					)
					.ToUpperInvariant();

			byte[] folderHash =
				SHA256.HashData(
					Encoding.UTF8.GetBytes(
						normalizedPluginFolder
					)
				);

			return commandPipePrefix +
				Convert.ToHexString(folderHash).Substring(0, 16);
		}

		/// <summary>
		/// Starts the local command listener once SyMenu has completed the normal startup check.
		/// The listener is background-only and does not keep SyMenu open when the process exits.
		/// </summary>
		private void startCommandListener()
		{
			if (Interlocked.Exchange(
				ref commandListenerStarted,
				1) != 0)
			{
				return;
			}

			try
			{
				string pluginDllPath =
					Assembly.GetExecutingAssembly().Location;

				string pluginFolder =
					Path.GetDirectoryName(pluginDllPath);

				if (string.IsNullOrWhiteSpace(pluginFolder))
				{
					throw new InvalidOperationException(
						"Unable to determine the plugin folder for the external command listener."
					);
				}

				string pipeName =
					getCommandPipeName(pluginFolder);

				_ = Task.Run(
					() => commandListenerLoop(pipeName)
				);
			}
			catch (Exception ex)
			{
				Interlocked.Exchange(
					ref commandListenerStarted,
					0
				);

				writeCriticalLog(
					"Starting external command listener",
					ex
				);
			}
		}

		/// <summary>
		/// Waits for CHECK commands from AudioPlugins.Check.exe.
		/// A request reserves the same single-check guard used by the normal plugin callbacks.
		/// </summary>
		private async Task commandListenerLoop(
			string pipeName)
		{
			while (true)
			{
				try
				{
					using (NamedPipeServerStream pipe =
						new NamedPipeServerStream(
							pipeName,
							PipeDirection.InOut,
							1,
							PipeTransmissionMode.Byte,
							PipeOptions.Asynchronous
						))
					{
						await pipe.WaitForConnectionAsync();

						using (StreamReader reader =
							new StreamReader(
								pipe,
								Encoding.UTF8,
								false,
								1024,
								true
							))
						using (StreamWriter writer =
							new StreamWriter(
								pipe,
								new UTF8Encoding(false),
								1024,
								true
							))
						{
							writer.AutoFlush = true;

							string command =
								await reader.ReadLineAsync();

							if (!string.Equals(
								command,
								"CHECK",
								StringComparison.Ordinal))
							{
								await writer.WriteLineAsync("UNKNOWN");
								continue;
							}

							if (Interlocked.CompareExchange(
								ref suiteCheckInProgress,
								1,
								0) != 0)
							{
								await writer.WriteLineAsync("BUSY");
								continue;
							}

							try
							{
								await writer.WriteLineAsync("ACCEPTED");

								queueExternalManualCheck();
							}
							catch
							{
								// The check was reserved above. If the response or dispatch fails
								// before the updater takes over, release the guard for the next request.
								Interlocked.Exchange(
									ref suiteCheckInProgress,
									0
								);

								throw;
							}
						}
					}
				}
				catch (IOException)
				{
					// A launcher can disappear after connecting. That is not a plugin fault;
					// simply return to listening for the next request.
				}
				catch (Exception ex)
				{
					writeCriticalLog(
						"External command listener",
						ex
					);

					await Task.Delay(1000);
				}
			}
		}

		/// <summary>
		/// Runs an accepted external request on its own STA background thread.
		/// MessageBox requires a Windows STA thread, while the suite updater itself
		/// does not depend on SyMenu's UI thread.
		/// </summary>
		private void queueExternalManualCheck()
		{
			Thread checkThread =
				new Thread(
					() => performReservedSuiteCheck(true)
				);

			checkThread.IsBackground = true;
			checkThread.Name = "AudioPlugins Manual Check";
			checkThread.SetApartmentState(
				ApartmentState.STA
			);

			checkThread.Start();
		}

		/// <summary>
		/// Returns an unused backup directory name beneath _Trash.
		/// Existing backups are never overwritten; numeric suffixes are added when required.
		/// </summary>
		private static string getUniqueBackupFolder(
			string trashFolder,
			string baseName)
		{
			string backupFolder =
				Path.Combine(
					trashFolder,
					baseName
				);

			int suffix = 2;

			while (Directory.Exists(backupFolder))
			{
				backupFolder =
					Path.Combine(
						trashFolder,
						baseName + "_" + suffix
					);

				suffix++;
			}

			return backupFolder;
		}

		/// <summary>
		/// Removes a cached suite archive after validation fails so the next check downloads a fresh copy.
		/// Failure to delete the archive is deliberately ignored because it must not hide the original error.
		/// </summary>
		private static void deleteArchiveForRetry(
			string archiveFile)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(archiveFile) &&
					File.Exists(archiveFile))
				{
					File.Delete(archiveFile);
				}
			}
			catch
			{
				// Best effort only. The original failure is more important.
			}
		}

		/// <summary>
		/// Writes an error-only diagnostic log beside the plugin DLL.
		/// Logging is best-effort and must never replace the exception that caused the failure.
		/// Routine offline/time-out conditions are handled separately and are not logged.
		/// </summary>
		private void writeCriticalLog(
			string context,
			Exception exception)
		{
			try
			{
				string pluginDllPath =
					Assembly.GetExecutingAssembly().Location;

				string pluginFolder =
					Path.GetDirectoryName(pluginDllPath);

				// If even the plugin directory cannot be resolved there is nowhere reliable
				// to place the requested plugin-local log, so leave the original error untouched.
				if (string.IsNullOrWhiteSpace(pluginFolder))
				{
					return;
				}

				string logFile =
					Path.Combine(
						pluginFolder,
						"AudioPlugins.log"
					);

				string logEntry =
					"============================================================" +
					Environment.NewLine +
					DateTime.Now.ToString(
						"yyyy-MM-dd HH:mm:ss",
						CultureInfo.InvariantCulture
					) +
					Environment.NewLine +
					"Plugin version: " +
					Version +
					Environment.NewLine +
					"Context: " +
					context +
					Environment.NewLine +
					"Plugin DLL: " +
					pluginDllPath +
					Environment.NewLine +
					"Exception type: " +
					exception.GetType().FullName +
					Environment.NewLine +
					Environment.NewLine +
					exception +
					Environment.NewLine +
					Environment.NewLine;

				File.AppendAllText(
					logFile,
					logEntry
				);
			}
			catch
			{
				// Logging is diagnostic only. Never let a logging failure obscure
				// the real plugin error that is already being handled.
			}
		}

		/// <summary>
		/// Performs the complete AudioPlugins suite-definition check/update transaction.
		/// showResult is true for an explicit menu click and false for automatic startup.
		/// </summary>
		private void checkSuiteDefinitions(bool showResult)
		{
			// The published collection marker lives beside the SPS files in the repository Suite folder.
			// The suite ZIP is downloaded only when the published date is newer than the local marker.
			const string latestUrl =
				"https://raw.githubusercontent.com/sl2365/AudioPluginsSuite/main/Suite/_latest.txt";

			try
			{
				// Resolve the plugin's own location first, then walk up from
				// SyMenu\Plugin\<plugin folder> to the SyMenu installation root.
				string pluginDllPath = Assembly.GetExecutingAssembly().Location;
				string pluginFolder = Path.GetDirectoryName(pluginDllPath);

				if (string.IsNullOrWhiteSpace(pluginFolder))
				{
					throw new InvalidOperationException(
						"Unable to determine the plugin folder."
					);
				}

				DirectoryInfo pluginDirectory = new DirectoryInfo(pluginFolder);
				DirectoryInfo syMenuRootDirectory = pluginDirectory.Parent?.Parent;

				if (syMenuRootDirectory == null)
				{
					throw new InvalidOperationException(
						"Unable to determine the SyMenu root folder."
					);
				}

				string syMenuRoot = syMenuRootDirectory.FullName;

				// Build all paths relative to the resolved SyMenu root so the plugin
				// remains portable and does not depend on a drive letter.
				string suiteFolder = Path.Combine(
					syMenuRoot,
					"ProgramFiles",
					"SPSSuite",
					"AudioPlugins"
				);

				string cacheFolder = Path.Combine(
					suiteFolder,
					"_Cache"
				);

				string trashFolder = Path.Combine(
					suiteFolder,
					"_Trash"
				);

				string localLatestFile = Path.Combine(
					cacheFolder,
					"_latest.txt"
				);

				// Directory.CreateDirectory is safe when the directory already exists,
				// so this also handles first-run creation of the custom suite structure.
				Directory.CreateDirectory(suiteFolder);
				Directory.CreateDirectory(cacheFolder);
				Directory.CreateDirectory(trashFolder);

				// Read the published collection date. Startup uses a short timeout so
				// lack of internet access does not hold SyMenu open for a long period.
				string onlineVersion;

				using (HttpClient client = new HttpClient())
				{
					client.Timeout = TimeSpan.FromSeconds(10);

					client.DefaultRequestHeaders.UserAgent.ParseAdd(
						"SyMenu-AudioPlugins/" + Version
					);

					onlineVersion = client
						.GetStringAsync(latestUrl)
						.GetAwaiter()
						.GetResult()
						.Trim();
				}

				if (!DateTime.TryParseExact(
					onlineVersion,
					"yyyy.MM.dd",
					CultureInfo.InvariantCulture,
					DateTimeStyles.None,
					out DateTime onlineDate))
				{
					throw new InvalidDataException(
						"Online _latest.txt does not contain a valid YYYY.MM.DD date."
					);
				}

				// Read and validate the installed collection marker when one exists.
				// A missing marker is handled separately below rather than guessed.
				string localVersion = "(none)";
				DateTime? localDate = null;

				if (File.Exists(localLatestFile))
				{
					localVersion = File.ReadAllText(localLatestFile).Trim();

					if (!DateTime.TryParseExact(
						localVersion,
						"yyyy.MM.dd",
						CultureInfo.InvariantCulture,
						DateTimeStyles.None,
						out DateTime parsedLocalDate))
					{
						throw new InvalidDataException(
							"Local _latest.txt does not contain a valid YYYY.MM.DD date."
						);
					}

					localDate = parsedLocalDate;
				}

				// Classify the current state before any files are changed.
				// Existing SPS files without a marker are treated as an unversioned
				// collection so they can be backed up before migration.
				string status;

				string[] unversionedSpsFiles =
					Array.Empty<string>();

				if (!localDate.HasValue)
				{
					unversionedSpsFiles =
						Directory.GetFiles(
							cacheFolder,
							"*.sps",
							SearchOption.TopDirectoryOnly
						);
				}

				if (!localDate.HasValue &&
					unversionedSpsFiles.Length != 0)
				{
					status = "UNVERSIONED EXISTING SUITE";
				}
				else if (!localDate.HasValue)
				{
					status = "FIRST INSTALL";
				}
				else if (onlineDate > localDate.Value)
				{
					status = "UPDATE AVAILABLE";
				}
				else if (onlineDate == localDate.Value)
				{
					status = "UP TO DATE";
				}
				else
				{
					status = "LOCAL NEWER";
				}

				// These status strings are retained for diagnostics/internal flow even
				// though normal user-facing messages are intentionally concise.
				string archiveStatus = "Not required.";
				string extractionStatus = "Not required.";
				string installationStatus = "Not required.";
				string archiveFile = string.Empty;

				// Only fetch the suite archive when an install, update, or migration is required.
				if (status == "FIRST INSTALL" ||
					status == "UPDATE AVAILABLE" ||
					status == "UNVERSIONED EXISTING SUITE")
				{
					// GitHub Actions publishes a fixed Suite.zip containing only the managed
					// AudioPlugins SPS collection from the repository Suite folder.
					const string archiveUrl =
						"https://github.com/sl2365/AudioPluginsSuite/releases/download/suite-current/Suite.zip";

					string archiveFileName =
						"AudioPlugins_" + onlineVersion + ".zip";

					archiveFile = Path.Combine(
						trashFolder,
						archiveFileName
					);

					// Download to a temporary filename first. A partial download is never
					// allowed to masquerade as a valid cached release archive.
					string temporaryArchiveFile =
						archiveFile + ".download";

					if (File.Exists(archiveFile))
					{
						archiveStatus =
							"Already downloaded:" +
							Environment.NewLine +
							archiveFile;
					}
					else
					{
						if (File.Exists(temporaryArchiveFile))
						{
							File.Delete(temporaryArchiveFile);
						}

						try
						{
							using (HttpClient client = new HttpClient())
							{
								client.Timeout = TimeSpan.FromMinutes(5);

								client.DefaultRequestHeaders.UserAgent.ParseAdd(
									"SyMenu-AudioPlugins/" + Version
								);

								using (HttpResponseMessage response = client
									.GetAsync(
										archiveUrl,
										HttpCompletionOption.ResponseHeadersRead
									)
									.GetAwaiter()
									.GetResult())
								{
									response.EnsureSuccessStatusCode();

									using (Stream source = response.Content
										.ReadAsStreamAsync()
										.GetAwaiter()
										.GetResult())
									using (FileStream destination = new FileStream(
										temporaryArchiveFile,
										FileMode.CreateNew,
										FileAccess.Write,
										FileShare.None))
									{
										source.CopyTo(destination);
									}
								}
							}

							FileInfo downloadedFile =
								new FileInfo(temporaryArchiveFile);

							if (!downloadedFile.Exists ||
								downloadedFile.Length == 0)
							{
								throw new InvalidDataException(
									"Downloaded AudioPlugins archive is empty."
								);
							}

							File.Move(
								temporaryArchiveFile,
								archiveFile
							);

							archiveStatus =
								"Downloaded successfully:" +
								Environment.NewLine +
								archiveFile +
								Environment.NewLine +
								Environment.NewLine +
								"Size: " +
								downloadedFile.Length.ToString("N0") +
								" bytes";
						}
						catch
						{
							if (File.Exists(temporaryArchiveFile))
							{
								File.Delete(temporaryArchiveFile);
							}

							throw;
						}
					}
				}

				// Validate and extract the generated Suite.zip with the 7-Zip executable bundled by SyMenu.
				// Extraction is staged under _Trash so the active _Cache is untouched
				// until the packaged marker and SPS files have been verified.
				if (status == "FIRST INSTALL" ||
					status == "UPDATE AVAILABLE" ||
					status == "UNVERSIONED EXISTING SUITE")
				{
					string sevenZipExe = Path.Combine(
						syMenuRoot,
						"Lib",
						"7z.exe"
					);

					if (!File.Exists(sevenZipExe))
					{
						throw new FileNotFoundException(
							"SyMenu 7z.exe was not found.",
							sevenZipExe
						);
					}

					if (string.IsNullOrWhiteSpace(archiveFile) ||
						!File.Exists(archiveFile))
					{
						throw new FileNotFoundException(
							"The downloaded AudioPlugins archive was not found.",
							archiveFile
						);
					}

					// Recreate the temporary extraction workspace on every run. If a prior
					// failed attempt left it behind, stale content is discarded first.
					string extractionFolder = Path.Combine(
						trashFolder,
						"_ExtractWork"
					);

					if (Directory.Exists(extractionFolder))
					{
						Directory.Delete(
							extractionFolder,
							true
						);
					}

					Directory.CreateDirectory(extractionFolder);

					ProcessStartInfo startInfo =
						new ProcessStartInfo();

					startInfo.FileName = sevenZipExe;
					startInfo.UseShellExecute = false;
					startInfo.CreateNoWindow = true;
					startInfo.RedirectStandardOutput = true;
					startInfo.RedirectStandardError = true;

					startInfo.ArgumentList.Add("x");
					startInfo.ArgumentList.Add("-y");
					startInfo.ArgumentList.Add("-bd");
					startInfo.ArgumentList.Add(
						"-o" + extractionFolder
					);
					startInfo.ArgumentList.Add(archiveFile);

					// Capture 7-Zip output so a failure can report the actual extraction error.
					using (Process process = new Process())
					{
						process.StartInfo = startInfo;

						if (!process.Start())
						{
							throw new InvalidOperationException(
								"Unable to start SyMenu 7z.exe."
							);
						}

						var outputTask =
							process.StandardOutput.ReadToEndAsync();

						var errorTask =
							process.StandardError.ReadToEndAsync();

						process.WaitForExit();

						string processOutput =
							outputTask.GetAwaiter().GetResult();

						string processError =
							errorTask.GetAwaiter().GetResult();

						if (process.ExitCode != 0)
						{
							deleteArchiveForRetry(
								archiveFile
							);

							string errorMessage =
								"7-Zip extraction failed with exit code " +
								process.ExitCode + ".";

							if (!string.IsNullOrWhiteSpace(processError))
							{
								errorMessage +=
									Environment.NewLine +
									Environment.NewLine +
									processError.Trim();
							}

							throw new InvalidDataException(
								errorMessage
							);
						}
					}

					// Suite.zip is built from inside the repository Suite folder, so its
					// _latest.txt and SPS files extract directly into this temporary folder.
					string packagedLatestFile =
						Path.Combine(
							extractionFolder,
							"_latest.txt"
						);

					if (!File.Exists(packagedLatestFile))
					{
						deleteArchiveForRetry(
							archiveFile
						);

						throw new InvalidDataException(
							"The downloaded Suite.zip does not contain _latest.txt."
						);
					}

					string packagedVersion =
						File.ReadAllText(packagedLatestFile).Trim();

					if (!string.Equals(
						packagedVersion,
						onlineVersion,
						StringComparison.Ordinal))
					{
						deleteArchiveForRetry(
							archiveFile
						);

						throw new InvalidDataException(
							"The downloaded Suite.zip does not match the published collection date. " +
							"Please try the update again shortly."
						);
					}

					// The published suite is intentionally flat: only SPS files directly
					// inside the package root are part of the managed AudioPlugins collection.
					string[] extractedSpsFiles =
						Directory.GetFiles(
							extractionFolder,
							"*.sps",
							SearchOption.TopDirectoryOnly
						);

					if (extractedSpsFiles.Length == 0)
					{
						deleteArchiveForRetry(
							archiveFile
						);

						throw new InvalidDataException(
							"The downloaded Suite.zip contains no SPS files."
						);
					}

					extractionStatus =
						"Extracted successfully." +
						Environment.NewLine +
						"SPS files found: " +
						extractedSpsFiles.Length;

					// FIRST INSTALL: _Cache must not already contain SPS definitions.
					// Copy the verified files, then commit _latest.txt last.
					if (status == "FIRST INSTALL")
					{
						string[] existingSpsFiles =
							Directory.GetFiles(
								cacheFolder,
								"*.sps",
								SearchOption.AllDirectories
							);

						if (existingSpsFiles.Length != 0)
						{
							throw new InvalidOperationException(
								"First install was stopped because _Cache already contains SPS files. " +
								"No existing SPS files were overwritten."
							);
						}

						List<string> installedFiles =
							new List<string>();

						string temporaryLatestFile =
							localLatestFile + ".new";

						try
						{
							if (File.Exists(temporaryLatestFile))
							{
								File.Delete(temporaryLatestFile);
							}

							foreach (string sourceSpsFile in extractedSpsFiles)
							{
								string destinationSpsFile =
									Path.Combine(
										cacheFolder,
										Path.GetFileName(sourceSpsFile)
									);

								if (File.Exists(destinationSpsFile))
								{
									throw new IOException(
										"An SPS file with the same name already exists in _Cache: " +
										Path.GetFileName(sourceSpsFile)
									);
								}

								File.Copy(
									sourceSpsFile,
									destinationSpsFile,
									false
								);

								installedFiles.Add(destinationSpsFile);
							}

							File.WriteAllText(
								temporaryLatestFile,
								onlineVersion + Environment.NewLine
							);

							File.Move(
								temporaryLatestFile,
								localLatestFile
							);

							installationStatus =
								"First install completed successfully." +
								Environment.NewLine +
								"SPS files installed: " +
								installedFiles.Count +
								Environment.NewLine +
								Environment.NewLine +
								"Local marker:" +
								Environment.NewLine +
								localLatestFile;
						}
						catch
						{
							if (File.Exists(temporaryLatestFile))
							{
								try
								{
									File.Delete(temporaryLatestFile);
								}
								catch
								{
								}
							}

							foreach (string installedFile in installedFiles)
							{
								try
								{
									if (File.Exists(installedFile))
									{
										File.Delete(installedFile);
									}
								}
								catch
								{
								}
							}

							throw;
						}
					}
					// UNVERSIONED MIGRATION: preserve the unknown existing collection first,
					// then replace it with the published set. Roll back from that backup on failure.
					else if (status == "UNVERSIONED EXISTING SUITE")
					{
						string[] existingSpsFiles =
							Directory.GetFiles(
								cacheFolder,
								"*.sps",
								SearchOption.TopDirectoryOnly
							);

						if (existingSpsFiles.Length == 0)
						{
							throw new InvalidOperationException(
								"Unversioned suite migration was requested, but no existing SPS files were found."
							);
						}

						string backupFolder =
							getUniqueBackupFolder(
								trashFolder,
								"Unversioned_" + onlineVersion
							);

						List<string> installedFiles =
							new List<string>();

						List<string> backupSpsFiles =
							new List<string>();

						string temporaryLatestFile =
							localLatestFile + ".new";

						bool backupComplete = false;

						try
						{
							// Complete the backup before deleting anything from the active cache.
							Directory.CreateDirectory(backupFolder);

							foreach (string existingSpsFile in existingSpsFiles)
							{
								string backupSpsFile =
									Path.Combine(
										backupFolder,
										Path.GetFileName(existingSpsFile)
									);

								File.Copy(
									existingSpsFile,
									backupSpsFile,
									false
								);

								backupSpsFiles.Add(backupSpsFile);
							}

							backupComplete = true;

							foreach (string existingSpsFile in existingSpsFiles)
							{
								File.Delete(existingSpsFile);
							}

							foreach (string sourceSpsFile in extractedSpsFiles)
							{
								string destinationSpsFile =
									Path.Combine(
										cacheFolder,
										Path.GetFileName(sourceSpsFile)
									);

								if (File.Exists(destinationSpsFile))
								{
									throw new IOException(
										"An SPS file with the same name already exists in _Cache: " +
										Path.GetFileName(sourceSpsFile)
									);
								}

								File.Copy(
									sourceSpsFile,
									destinationSpsFile,
									false
								);

								installedFiles.Add(destinationSpsFile);
							}

							if (File.Exists(temporaryLatestFile))
							{
								File.Delete(temporaryLatestFile);
							}

							File.WriteAllText(
								temporaryLatestFile,
								onlineVersion + Environment.NewLine
							);

							File.Move(
								temporaryLatestFile,
								localLatestFile
							);

							installationStatus =
								"Existing unversioned suite migrated successfully." +
								Environment.NewLine +
								"Current collection: " +
								onlineVersion +
								Environment.NewLine +
								"SPS files installed: " +
								installedFiles.Count +
								Environment.NewLine +
								Environment.NewLine +
								"Backup:" +
								Environment.NewLine +
								backupFolder;
						}
						catch (Exception migrationException)
						{
							if (File.Exists(temporaryLatestFile))
							{
								try
								{
									File.Delete(temporaryLatestFile);
								}
								catch
								{
								}
							}

							if (!backupComplete)
							{
								try
								{
									if (Directory.Exists(backupFolder))
									{
										Directory.Delete(
											backupFolder,
											true
										);
									}
								}
								catch
								{
								}

								throw;
							}

							// Backup completed, so restore the previous live collection before reporting failure.
							try
							{
								string[] currentSpsFiles =
									Directory.GetFiles(
										cacheFolder,
										"*.sps",
										SearchOption.TopDirectoryOnly
									);

								foreach (string currentSpsFile in currentSpsFiles)
								{
									File.Delete(currentSpsFile);
								}

								foreach (string backupSpsFile in backupSpsFiles)
								{
									string restoredSpsFile =
										Path.Combine(
											cacheFolder,
											Path.GetFileName(backupSpsFile)
										);

									File.Copy(
										backupSpsFile,
										restoredSpsFile,
										true
									);
								}

								if (File.Exists(localLatestFile))
								{
									File.Delete(localLatestFile);
								}
							}
							catch (Exception rollbackException)
							{
								throw new InvalidOperationException(
									"AudioPlugins migration failed and the automatic rollback also failed." +
									Environment.NewLine +
									Environment.NewLine +
									"Migration error: " +
									migrationException.Message +
									Environment.NewLine +
									Environment.NewLine +
									"Rollback error: " +
									rollbackException.Message +
									Environment.NewLine +
									Environment.NewLine +
									"Backup folder:" +
									Environment.NewLine +
									backupFolder,
									migrationException
								);
							}

							throw new InvalidOperationException(
								"AudioPlugins migration failed. The previous unversioned SPS collection was restored." +
								Environment.NewLine +
								Environment.NewLine +
								migrationException.Message,
								migrationException
							);
						}
					}
					// NORMAL UPDATE: back up the currently versioned collection and marker,
					// replace the SPS set as a whole, and advance the marker only after success.
					else if (status == "UPDATE AVAILABLE")
					{
						if (!localDate.HasValue ||
							!File.Exists(localLatestFile))
						{
							throw new InvalidOperationException(
								"Update was stopped because the existing local collection marker is missing."
							);
						}

						string backupFolder =
							getUniqueBackupFolder(
								trashFolder,
								localVersion
							);

						string[] existingSpsFiles =
							Directory.GetFiles(
								cacheFolder,
								"*.sps",
								SearchOption.TopDirectoryOnly
							);

						List<string> installedFiles =
							new List<string>();

						List<string> backupSpsFiles =
							new List<string>();

						string backupLatestFile =
							Path.Combine(
								backupFolder,
								"_latest.txt"
							);

						string temporaryLatestFile =
							localLatestFile + ".new";

						bool backupComplete = false;

						try
						{
							// Complete the backup before deleting anything from the active cache.
							Directory.CreateDirectory(backupFolder);

							foreach (string existingSpsFile in existingSpsFiles)
							{
								string backupSpsFile =
									Path.Combine(
										backupFolder,
										Path.GetFileName(existingSpsFile)
									);

								File.Copy(
									existingSpsFile,
									backupSpsFile,
									false
								);

								backupSpsFiles.Add(backupSpsFile);
							}

							File.Copy(
								localLatestFile,
								backupLatestFile,
								false
							);

							backupComplete = true;

							foreach (string existingSpsFile in existingSpsFiles)
							{
								File.Delete(existingSpsFile);
							}

							foreach (string sourceSpsFile in extractedSpsFiles)
							{
								string destinationSpsFile =
									Path.Combine(
										cacheFolder,
										Path.GetFileName(sourceSpsFile)
									);

								if (File.Exists(destinationSpsFile))
								{
									throw new IOException(
										"An SPS file with the same name already exists in _Cache: " +
										Path.GetFileName(sourceSpsFile)
									);
								}

								File.Copy(
									sourceSpsFile,
									destinationSpsFile,
									false
								);

								installedFiles.Add(destinationSpsFile);
							}

							if (File.Exists(temporaryLatestFile))
							{
								File.Delete(temporaryLatestFile);
							}

							File.WriteAllText(
								temporaryLatestFile,
								onlineVersion + Environment.NewLine
							);

							File.Move(
								temporaryLatestFile,
								localLatestFile,
								true
							);

							installationStatus =
								"Update completed successfully." +
								Environment.NewLine +
								"Previous collection: " +
								localVersion +
								Environment.NewLine +
								"Current collection: " +
								onlineVersion +
								Environment.NewLine +
								"SPS files installed: " +
								installedFiles.Count +
								Environment.NewLine +
								Environment.NewLine +
								"Backup:" +
								Environment.NewLine +
								backupFolder;
						}
						catch (Exception updateException)
						{
							if (File.Exists(temporaryLatestFile))
							{
								try
								{
									File.Delete(temporaryLatestFile);
								}
								catch
								{
								}
							}

							if (!backupComplete)
							{
								try
								{
									if (Directory.Exists(backupFolder))
									{
										Directory.Delete(
											backupFolder,
											true
										);
									}
								}
								catch
								{
								}

								throw;
							}

							// Backup completed, so restore the previous live collection before reporting failure.
							try
							{
								string[] currentSpsFiles =
									Directory.GetFiles(
										cacheFolder,
										"*.sps",
										SearchOption.TopDirectoryOnly
									);

								foreach (string currentSpsFile in currentSpsFiles)
								{
									File.Delete(currentSpsFile);
								}

								foreach (string backupSpsFile in backupSpsFiles)
								{
									string restoredSpsFile =
										Path.Combine(
											cacheFolder,
											Path.GetFileName(backupSpsFile)
										);

									File.Copy(
										backupSpsFile,
										restoredSpsFile,
										true
									);
								}

								File.Copy(
									backupLatestFile,
									localLatestFile,
									true
								);
							}
							catch (Exception rollbackException)
							{
								throw new InvalidOperationException(
									"AudioPlugins update failed and the automatic rollback also failed." +
									Environment.NewLine +
									Environment.NewLine +
									"Update error: " +
									updateException.Message +
									Environment.NewLine +
									Environment.NewLine +
									"Rollback error: " +
									rollbackException.Message +
									Environment.NewLine +
									Environment.NewLine +
									"Backup folder:" +
									Environment.NewLine +
									backupFolder,
									updateException
								);
							}

							throw new InvalidOperationException(
								"AudioPlugins update failed. The previous SPS collection was restored." +
								Environment.NewLine +
								Environment.NewLine +
								updateException.Message,
								updateException
							);
						}
					}

					// Successful operations no longer need the temporary extraction workspace.
					if (Directory.Exists(extractionFolder))
					{
						Directory.Delete(
							extractionFolder,
							true
						);
					}
				}

				// Automatic startup checks are silent on success. Only an explicit
				// plugin-menu check displays the final status to the user.
				if (showResult)
				{
					string resultMessage;

					if (status == "UP TO DATE")
					{
						resultMessage =
							"AudioPlugins suite is up to date." +
							Environment.NewLine +
							Environment.NewLine +
							"Collection: " +
							onlineVersion;
					}
					else if (status == "LOCAL NEWER")
					{
						resultMessage =
							"The installed AudioPlugins collection is newer than the published collection." +
							Environment.NewLine +
							Environment.NewLine +
							"Installed: " +
							localVersion +
							Environment.NewLine +
							"Published: " +
							onlineVersion +
							Environment.NewLine +
							Environment.NewLine +
							"No changes were made.";
					}
					else if (status == "UNVERSIONED EXISTING SUITE")
					{
						resultMessage =
							"Existing AudioPlugins SPS files were backed up and migrated successfully." +
							Environment.NewLine +
							Environment.NewLine +
							"Collection: " +
							onlineVersion;
					}
					else if (status == "FIRST INSTALL")
					{
						resultMessage =
							"AudioPlugins suite installed successfully." +
							Environment.NewLine +
							Environment.NewLine +
							"Collection: " +
							onlineVersion;
					}
					else
					{
						resultMessage =
							"AudioPlugins suite updated successfully." +
							Environment.NewLine +
							Environment.NewLine +
							"Previous collection: " +
							localVersion +
							Environment.NewLine +
							"Current collection: " +
							onlineVersion;
					}

					MessageBox.Show(
						resultMessage,
						"AudioPlugins"
					);
				}
			}
			catch (Exception ex)
			{
				// No internet connection or a network timeout is an expected transient state.
				// Keep automatic startup silent, but explain the problem after a manual check.
				bool networkFailure =
					ex is System.Threading.Tasks.TaskCanceledException ||
					(ex is HttpRequestException httpException &&
						!httpException.StatusCode.HasValue);

				if (networkFailure)
				{
					if (showResult)
					{
						MessageBox.Show(
							"Unable to check for AudioPlugins updates." +
							Environment.NewLine +
							Environment.NewLine +
							"Please check your internet connection and try again.",
							"AudioPlugins"
						);
					}

					return;
				}

				// All other failures are unexpected or potentially persistent. Record full
				// diagnostic detail beside the plugin DLL before showing the concise error.
				writeCriticalLog(
					showResult
						? "Manual suite-definition check"
						: "Automatic startup suite-definition check",
					ex
				);

				MessageBox.Show(
					"AudioPlugins update check failed." +
					Environment.NewLine + Environment.NewLine +
					ex.Message,
					"AudioPlugins"
				);
			}
		}
		
		/// <summary>
		/// This is the method the plugin invokes when SyMenu is launched.
		/// </summary>
		void syMenuStartup()
		{
			// Complete the normal silent startup check before exposing the external
			// command listener. This avoids a launcher racing the initial transaction.
			runSuiteCheck(false);

			// The companion AudioPlugins.Check.exe can now request the same manual
			// check that the SyMenu Plugins -> AudioPlugins menu item performs.
			startCommandListener();
		}
	}
}
