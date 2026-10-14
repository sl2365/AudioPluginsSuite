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
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using SyMenu.Plugin.Resources.V01.Interfaces;

[assembly: SupportedOSPlatform("windows6.1")]

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
			Name = "AudioPlugins";
			Version = "1.00";
			Author = "sl23";
			Description = "Keeps the AudioPlugins SPS suite definitions up to date.";

			Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SyMenu.Plugin.AudioPlugins.AppIcons.pluginIcon.ico");
			MenuIcon = new Icon(iconStream);

			PluginEventHandler = pluginClick;
			
			OnStartup = syMenuStartup;
		}
		
		/// <summary>
		/// This is the method SyMenu calls when the user clicks on the plugin menu item.
		/// </summary>
		public void pluginClick(object sender, System.EventArgs e)
		{
			checkSuiteDefinitions(true);
		}

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

		private void checkSuiteDefinitions(bool showResult)
		{
			const string latestUrl =
				"https://raw.githubusercontent.com/sl2365/AudioPluginsSuite/main/_latest.txt";

			try
			{
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

				Directory.CreateDirectory(suiteFolder);
				Directory.CreateDirectory(cacheFolder);
				Directory.CreateDirectory(trashFolder);

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

				string archiveStatus = "Not required.";
				string extractionStatus = "Not required.";
				string installationStatus = "Not required.";
				string archiveFile = string.Empty;

				if (status == "FIRST INSTALL" ||
					status == "UPDATE AVAILABLE" ||
					status == "UNVERSIONED EXISTING SUITE")
				{
					const string archiveUrl =
						"https://github.com/sl2365/AudioPluginsSuite/releases/latest/download/AudioPlugins.rar";

					string archiveFileName =
						"AudioPlugins_" + onlineVersion + ".rar";

					archiveFile = Path.Combine(
						trashFolder,
						archiveFileName
					);

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

					string[] extractedSpsFiles =
						Directory.GetFiles(
							extractionFolder,
							"*.sps",
							SearchOption.AllDirectories
						);

					if (extractedSpsFiles.Length == 0)
					{
						deleteArchiveForRetry(
							archiveFile
						);

						throw new InvalidDataException(
							"The archive extracted successfully, but no SPS files were found."
						);
					}

					extractionStatus =
						"Extracted successfully." +
						Environment.NewLine +
						"SPS files found: " +
						extractedSpsFiles.Length;

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

					if (Directory.Exists(extractionFolder))
					{
						Directory.Delete(
							extractionFolder,
							true
						);
					}
				}

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
			checkSuiteDefinitions(false);
		}
	}
}
