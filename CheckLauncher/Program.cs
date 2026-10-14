using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

[assembly: SupportedOSPlatform("windows")]

namespace AudioPlugins.Check
{
	internal static class Program
	{
		private const string commandPipePrefix =
			"SyMenu.AudioPlugins.Command.";

		[STAThread]
		private static void Main()
		{
			try
			{
				// The helper is intended to live beside the plugin DLL. Deriving the
				// pipe name from this folder keeps separate portable SyMenu copies isolated.
				string pluginFolder =
					AppContext.BaseDirectory;

				string pipeName =
					getCommandPipeName(pluginFolder);

				using (NamedPipeClientStream pipe =
					new NamedPipeClientStream(
						".",
						pipeName,
						PipeDirection.InOut,
						PipeOptions.None
					))
				{
					// A short timeout keeps the helper responsive when SyMenu or the
					// AudioPlugins plugin is not currently running.
					pipe.Connect(1500);

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

						writer.WriteLine("CHECK");

						string response =
							reader.ReadLine();

						if (string.Equals(
							response,
							"ACCEPTED",
							StringComparison.Ordinal))
						{
							return;
						}

						if (string.Equals(
							response,
							"BUSY",
							StringComparison.Ordinal))
						{
							MessageBox.Show(
								"An AudioPlugins update check is already in progress.",
								"AudioPlugins"
							);

							return;
						}

						MessageBox.Show(
							"AudioPlugins could not start the requested update check.",
							"AudioPlugins"
						);
					}
				}
			}
			catch (TimeoutException)
			{
				showPluginUnavailable();
			}
			catch (IOException)
			{
				showPluginUnavailable();
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"AudioPlugins manual check could not be started." +
					Environment.NewLine +
					Environment.NewLine +
					ex.Message,
					"AudioPlugins"
				);
			}
		}

		private static void showPluginUnavailable()
		{
			MessageBox.Show(
				"AudioPlugins is not currently available." +
				Environment.NewLine +
				Environment.NewLine +
				"Start SyMenu and try again.",
				"AudioPlugins"
			);
		}

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
	}
}
