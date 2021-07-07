using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using OtpNet;
using PassWinmenu.Configuration;
using PassWinmenu.ExternalPrograms;
using PassWinmenu.ExternalPrograms.Gpg;
using PassWinmenu.PasswordManagement;
using PassWinmenu.Utilities;
using PassWinmenu.Utilities.ExtensionMethods;
using PassWinmenu.WinApi;

namespace PassWinmenu.Windows
{
	internal class DialogCreator
	{
		private readonly INotificationService notificationService;
		private readonly ISyncService syncService;
		private readonly IPasswordManager passwordManager;
		private readonly PasswordFileParser passwordFileParser;
		private readonly PathDisplayHelper pathDisplayHelper;

		public DialogCreator(
			INotificationService notificationService,
			IPasswordManager passwordManager,
			PasswordFileParser passwordFileParser,
			Option<ISyncService> syncService)
		{
			this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
			this.passwordManager = passwordManager ?? throw new ArgumentNullException(nameof(passwordManager));
			this.passwordFileParser = passwordFileParser;
			this.syncService = syncService.Value;
			this.pathDisplayHelper = new PathDisplayHelper(ConfigManager.Config.Interface.DirectorySeparator);
		}
		public void EditPassword()
		{
			var selectedFile = RequestPasswordFile();
			if (selectedFile == null) return;

			if (ConfigManager.Config.Interface.PasswordEditor.UseBuiltin)
			{
				DecryptedPasswordFile decryptedFile;
				try
				{
					decryptedFile = passwordManager.DecryptPassword(selectedFile, false);
				}
				catch (Exception e)
				{
					notificationService.ShowErrorWindow($"Unable to edit your password (decryption failed): {e.Message}");
					return;
				}
				EditWithEditWindow(decryptedFile);
			}
			else
			{
				EditWithTextEditor(selectedFile);
			}
		}

		/// <summary>
		/// Adds a new password to the password store.
		/// </summary>
		public void AddPassword()
		{
			Helpers.AssertOnUiThread();

			var passwordFilePath = ShowFileSelectionWindow();
			// passwordFileName will be null if no file was selected
			if (passwordFilePath == null) return;

			// Display the password generation window.
			string password;
			string metadata;
			using (var passwordWindow = new PasswordWindow(Path.GetFileName(passwordFilePath), ConfigManager.Config.PasswordStore.PasswordGeneration))
			{
				passwordWindow.ShowDialog();
				if (!passwordWindow.DialogResult.GetValueOrDefault())
				{
					return;
				}
				password = passwordWindow.Password.Text;
				metadata = passwordWindow.ExtraContent.Text.Replace(Environment.NewLine, "\n");
			}

			PasswordFile passwordFile;
			try
			{
				passwordFile = passwordManager.AddPassword(passwordFilePath, password, metadata);
			}
			catch (GpgException e)
			{
				notificationService.ShowErrorWindow("Unable to encrypt your password: " + e.Message);
				return;
			}
			catch (ConfigurationException e)
			{
				notificationService.ShowErrorWindow("Unable to encrypt your password: " + e.Message);
				return;
			}
			// Copy the newly generated password.
			ClipboardHelper.Place(password, TimeSpan.FromSeconds(ConfigManager.Config.Interface.ClipboardTimeout));

			if (ConfigManager.Config.Notifications.Types.PasswordGenerated)
			{
				notificationService.Raise($"The new password has been copied to your clipboard.\nIt will be cleared in {ConfigManager.Config.Interface.ClipboardTimeout:0.##} seconds.", Severity.Info);
			}
			// Add the password to Git
			syncService?.AddPassword(passwordFile.FullPath);
		}

		/// <summary>
		/// Asks the user to choose a password file, decrypts it, and copies the resulting value to the clipboard.
		/// </summary>
		public void DecryptPassword(bool copyToClipboard, bool typeUsername, bool typePassword)
		{
			var selectedFile = RequestPasswordFile();
			// If the user cancels their selection, the password decryption should be cancelled too.
			if (selectedFile == null) return;

			KeyedPasswordFile passFile;
			try
			{
				passFile = passwordManager.DecryptPassword(selectedFile, ConfigManager.Config.PasswordStore.FirstLineOnly);
			}
			catch (Exception e) when (e is GpgError || e is GpgException || e is ConfigurationException)
			{
				notificationService.ShowErrorWindow("Password decryption failed: " + e.Message);
				return;
			}
			catch (Exception e)
			{
				notificationService.ShowErrorWindow($"Password decryption failed: An error occurred: {e.GetType().Name}: {e.Message}");
				return;
			}

			if (copyToClipboard)
			{
				ClipboardHelper.Place(passFile.Password, TimeSpan.FromSeconds(ConfigManager.Config.Interface.ClipboardTimeout));
				if (ConfigManager.Config.Notifications.Types.PasswordCopied)
				{
					notificationService.Raise($"The password has been copied to your clipboard.\nIt will be cleared in {ConfigManager.Config.Interface.ClipboardTimeout:0.##} seconds.", Severity.Info);
				}
			}
			var usernameEntered = false;
			if (typeUsername)
			{
				try
				{
					var username = passwordFileParser.GetUsername(passFile);
					if (username != null)
					{
						KeyboardEmulator.EnterText(username);
						usernameEntered = true;
					}
				}
				catch (Exception e)
				{
					notificationService.ShowErrorWindow($"Could not retrieve your username: {e.Message}");
					return;
				}
			}
			if (typePassword)
			{
				// If a username has also been entered, press Tab to switch to the password field.
				if (usernameEntered) KeyboardEmulator.EnterTab();

				KeyboardEmulator.EnterText(passFile.Password);
			}
		}

		/// <summary>
		/// Asks the user to choose a password file, decrypts it,
		/// generates an OTP code from the secret in the totp field, and copies the resulting value to the clipboard.
		/// </summary>
		public void GenerateTotpCode(bool copyToClipboard, bool typeTotpCode)
		{
			var selectedFile = RequestPasswordFile();
			// If the user cancels their selection, the password decryption should be cancelled too.
			if (selectedFile == null) return;

			KeyedPasswordFile passFile;
			try
			{
				passFile = passwordManager.DecryptPassword(selectedFile, true);
			}
			catch (Exception e) when (e is GpgError || e is GpgException || e is ConfigurationException)
			{
				notificationService.ShowErrorWindow("TOTP decryption failed: " + e.Message);
				return;
			}
			catch (Exception e)
			{
				notificationService.ShowErrorWindow($"TOTP decryption failed: An error occurred: {e.GetType().Name}: {e.Message}");
				return;
			}

			String secretKey = null;

			foreach (var k in passFile.Keys)
			{
				// totp: Xxxx4
				if (k.Key == "totp")
				{
					secretKey = k.Value;
				}

				// otpauth: //totp/account?secret=FxxxX&digits=6
				if (k.Key == "otpauth")
				{
					var regex = new Regex("secret=([a-zA-Z0-9]+)&", RegexOptions.Compiled | RegexOptions.IgnoreCase);
					var matches = regex.Match(k.Value);
					if (matches.Success) {
						secretKey = matches.Groups[1].Value;
					}
				}
			}

			var foo = String.Join(",", passFile.Keys.Select(f => f.Key));

			if (secretKey == null)
			{
				notificationService.ShowErrorWindow($"TOTP decryption failed: Failed to find an OTP secret. Keys = ${foo}");
				return;
			}

			var secretKeyBytes = Base32Encoding.ToBytes(secretKey);
			var totp = new Totp(secretKeyBytes);
			var totpCode = totp.ComputeTotp();

			if (copyToClipboard)
			{
				ClipboardHelper.Place(totpCode, TimeSpan.FromSeconds(ConfigManager.Config.Interface.ClipboardTimeout));
				if (ConfigManager.Config.Notifications.Types.TotpCopied)
				{
					notificationService.Raise($"The totp code has been copied to your clipboard.\nIt will be cleared in {ConfigManager.Config.Interface.ClipboardTimeout:0.##} seconds.", Severity.Info);
				}
			}

			if (typeTotpCode)
			{
				KeyboardEmulator.EnterText(totpCode, ConfigManager.Config.Output.DeadKeys);
			}
		}

		public void DecryptMetadata(bool copyToClipboard, bool type)
		{
			var selectedFile = RequestPasswordFile();
			KeyedPasswordFile passFile;
			try
			{
				passFile = passwordManager.DecryptPassword(selectedFile, ConfigManager.Config.PasswordStore.FirstLineOnly);
			}
			catch (GpgError e)
			{
				notificationService.ShowErrorWindow("Password decryption failed: " + e.Message);
				return;
			}
			catch (GpgException e)
			{
				notificationService.ShowErrorWindow("Password decryption failed. " + e.Message);
				return;
			}
			catch (ConfigurationException e)
			{
				notificationService.ShowErrorWindow("Password decryption failed: " + e.Message);
				return;
			}
			catch (Exception e)
			{
				notificationService.ShowErrorWindow($"Password decryption failed: An error occurred: {e.GetType().Name}: {e.Message}");
				return;
			}

			if (copyToClipboard)
			{
				ClipboardHelper.Place(passFile.Metadata, TimeSpan.FromSeconds(ConfigManager.Config.Interface.ClipboardTimeout));
				if (ConfigManager.Config.Notifications.Types.PasswordCopied)
				{
					notificationService.Raise($"The key has been copied to your clipboard.\nIt will be cleared in {ConfigManager.Config.Interface.ClipboardTimeout:0.##} seconds.", Severity.Info);
				}
			}
			if (type)
			{
				KeyboardEmulator.EnterText(passFile.Metadata);
			}
		}

		public void GetKey(bool copyToClipboard, bool type, string key)
		{
			var selectedFile = RequestPasswordFile();
			if (selectedFile == null) return;

			KeyedPasswordFile passFile;
			try
			{
				passFile = passwordManager.DecryptPassword(selectedFile, ConfigManager.Config.PasswordStore.FirstLineOnly);
			}
			catch (GpgError e)
			{
				notificationService.ShowErrorWindow("Password decryption failed: " + e.Message);
				return;
			}
			catch (GpgException e)
			{
				notificationService.ShowErrorWindow("Password decryption failed. " + e.Message);
				return;
			}
			catch (ConfigurationException e)
			{
				notificationService.ShowErrorWindow("Password decryption failed: " + e.Message);
				return;
			}
			catch (Exception e)
			{
				notificationService.ShowErrorWindow($"Password decryption failed: An error occurred: {e.GetType().Name}: {e.Message}");
				return;
			}

			if (string.IsNullOrWhiteSpace(key))
			{
				var keys = passFile.Keys.Select(k => k.Key).Distinct();
				var selection = ShowPasswordMenu(keys);
				if (selection == null) return;
				key = selection;
			}

			var values = passFile.Keys.Where(k => k.Key == key).ToList();

			if (values.Count == 0)
			{
				return;
			}

			string chosenValue;
			if (values.Count > 1)
			{
				chosenValue = ShowPasswordMenu(values.Select(v => v.Value));
				if (chosenValue == null)
				{
					return;
				}
			}
			else
			{
				chosenValue = values[0].Value;
			}


			if (copyToClipboard)
			{
				ClipboardHelper.Place(chosenValue, TimeSpan.FromSeconds(ConfigManager.Config.Interface.ClipboardTimeout));
				if (ConfigManager.Config.Notifications.Types.PasswordCopied)
				{
					notificationService.Raise($"The key has been copied to your clipboard.\nIt will be cleared in {ConfigManager.Config.Interface.ClipboardTimeout:0.##} seconds.", Severity.Info);
				}
			}
			if (type)
			{
				KeyboardEmulator.EnterText(chosenValue);
			}
		}

		/// <summary>
		/// Opens a window where the user can choose the location for a new password file.
		/// </summary>
		/// <returns>The path to the file that the user has chosen</returns>
		private string ShowFileSelectionWindow()
		{
			SelectionWindowConfiguration windowConfig;
			try
			{
				windowConfig = SelectionWindowConfiguration.ParseMainWindowConfiguration(ConfigManager.Config);
			}
			catch (ConfigurationParseException e)
			{
				notificationService.Raise(e.Message, Severity.Error);
				return null;
			}

			// Ask the user where the password file should be placed.
			var pathWindow = new FileSelectionWindow(ConfigManager.Config.PasswordStore.Location, windowConfig);
			pathWindow.ShowDialog();
			if (!pathWindow.Success)
			{
				return null;
			}
			return pathWindow.GetSelection() + Program.EncryptedFileExtension;
		}

		/// <summary>
		/// Opens the password menu and displays it to the user, allowing them to choose an existing password file.
		/// </summary>
		/// <param name="options">A list of options the user can choose from.</param>
		/// <returns>One of the values contained in <paramref name="options"/>, or null if no option was chosen.</returns>
		private string ShowPasswordMenu(IEnumerable<string> options)
		{
			SelectionWindowConfiguration windowConfig;
			try
			{
				windowConfig = SelectionWindowConfiguration.ParseMainWindowConfiguration(ConfigManager.Config);
			}
			catch (ConfigurationParseException e)
			{
				notificationService.Raise(e.Message, Severity.Error);
				return null;
			}

			var menu = new PasswordSelectionWindow(options, windowConfig);
			menu.ShowDialog();
			if (menu.Success)
			{
				return menu.GetSelection();
			}
			return null;
		}

		/// <summary>
		/// Asks the user to choose a password file.
		/// </summary>
		/// <returns>
		/// The path to the chosen password file (relative to the password directory),
		/// or null if the user didn't choose anything.
		/// </returns>
		private PasswordFile RequestPasswordFile()
		{
			Helpers.AssertOnUiThread();

			// Find GPG-encrypted password files
			var passFiles = passwordManager.GetPasswordFiles(ConfigManager.Config.PasswordStore.PasswordFileMatch).ToList();
			if (passFiles.Count == 0)
			{
				MessageBox.Show("Your password store doesn't appear to contain any passwords yet.", "Empty password store", MessageBoxButton.OK, MessageBoxImage.Information);
				return null;
			}
			var selection = ShowPasswordMenu(passFiles.Select(pathDisplayHelper.GetDisplayPath));
			if (selection == null) return null;
			return passFiles.Single(f => pathDisplayHelper.GetDisplayPath(f) == selection);
		}

		private void EditWithEditWindow(DecryptedPasswordFile file)
		{
			Helpers.AssertOnUiThread();

			using (var window = new EditWindow(pathDisplayHelper.GetDisplayPath(file), file.Content, ConfigManager.Config.PasswordStore.PasswordGeneration))
			{
				if (!window.ShowDialog() ?? true)
				{
					return;
				}

				var newFile = new DecryptedPasswordFile(file, window.PasswordContent.Text);
				try
				{
					passwordManager.EncryptPassword(newFile);

					syncService?.EditPassword(newFile.FullPath);
					if (ConfigManager.Config.Notifications.Types.PasswordUpdated)
					{
						notificationService.Raise($"Password file \"{newFile.FileNameWithoutExtension}\" has been updated.", Severity.Info);
					}
				}
				catch (GitException e)
				{
					notificationService.ShowErrorWindow($"Unable to commit your changes: {e.Message}");
					EditWithEditWindow(newFile);
				}
				catch (Exception e)
				{
					notificationService.ShowErrorWindow($"Unable to save your password (encryption failed): {e.Message}");
					EditWithEditWindow(newFile);
				}
			}
		}

		/// <summary>
		/// Ensures the file at the given path is deleted, warning the user if deletion failed.
		/// </summary>
		private void EnsureRemoval(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception e)
			{
				notificationService.ShowErrorWindow($"Unable to delete the plaintext file at {path}.\n" +
													$"An error occurred: {e.GetType().Name} ({e.Message}).\n\n" +
													$"Please navigate to the given path and delete it manually.", "Plaintext file not deleted.");
			}
		}

		private static string CreateTemporaryPlaintextFile()
		{
			var tempDir = ConfigManager.Config.Interface.PasswordEditor.TemporaryFileDirectory;

			if (string.IsNullOrWhiteSpace(tempDir))
			{
				Log.Send("No temporary file directory specified, using default.", LogLevel.Warning);
				tempDir = Path.GetTempPath();
			}

			if (!Directory.Exists(tempDir))
			{
				Log.Send($"Temporary directory \"{tempDir}\" does not exist, it will be created.", LogLevel.Info);
				Directory.CreateDirectory(tempDir);
			}

			var tempFile = Path.GetRandomFileName();
			var tempPath = Path.Combine(tempDir, tempFile + Program.PlaintextFileExtension);
			return tempPath;
		}

		private void EditWithTextEditor(PasswordFile selectedFile)
		{
			// Generate a random plaintext filename.
			var plaintextFile = CreateTemporaryPlaintextFile();
			try
			{
				var passwordFile = passwordManager.DecryptPassword(selectedFile, false);
				File.WriteAllText(plaintextFile, passwordFile.Content);
			}
			catch (Exception e)
			{
				EnsureRemoval(plaintextFile);
				notificationService.ShowErrorWindow($"Unable to edit your password (decryption failed): {e.Message}");
				return;
			}

			// Open the file in the user's default editor
			try
			{
				Process.Start(plaintextFile);
			}
			catch (Win32Exception e)
			{
				EnsureRemoval(plaintextFile);
				notificationService.ShowErrorWindow($"Unable to open an editor to edit your password file ({e.Message}).");
				return;
			}

			var result = MessageBox.Show(
				"Please keep this window open until you're done editing the password file.\n" +
				"Then click Yes to save your changes, or No to discard them.",
				$"Save changes to {selectedFile.FileNameWithoutExtension}?",
				MessageBoxButton.YesNo,
				MessageBoxImage.Information);

			if (result == MessageBoxResult.Yes)
			{
				// Fetch the content from the file, and delete it.
				var content = File.ReadAllText(plaintextFile);
				EnsureRemoval(plaintextFile);

				var newPasswordFile = new DecryptedPasswordFile(selectedFile, content);
				try
				{
					passwordManager.EncryptPassword(newPasswordFile);
					syncService?.EditPassword(selectedFile.FullPath);

					if (ConfigManager.Config.Notifications.Types.PasswordUpdated)
					{
						notificationService.Raise($"Password file \"{selectedFile}\" has been updated.", Severity.Info);
					}
				}
				catch (GitException e)
				{
					notificationService.ShowErrorWindow($"Unable to commit your changes: {e.Message}");
					EditWithTextEditor(newPasswordFile);
				}
				catch (Exception e)
				{
					notificationService.ShowErrorWindow($"Unable to save your password (encryption failed): {e.Message}");
					EditWithTextEditor(newPasswordFile);
				}
			}
			else
			{
				File.Delete(plaintextFile);
			}
		}
	}

	internal class PathDisplayHelper
	{
		private readonly string directorySeparator;

		public PathDisplayHelper(string directorySeparator)
		{
			this.directorySeparator = directorySeparator;
		}

		public string GetDisplayPath(PasswordFile file)
		{
			var names = new List<string>
			{
				file.FileNameWithoutExtension
			};

			var current = file.Directory;
			while (!current.PathEquals(file.PasswordStore))
			{
				names.Insert(0, current.Name);
				current = current.Parent;
			}

			return string.Join(directorySeparator, names);
		}
	}
}
