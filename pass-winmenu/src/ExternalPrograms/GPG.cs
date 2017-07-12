﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PassWinmenu.Configuration;

namespace PassWinmenu.ExternalPrograms
{
	/// <summary>
	/// Simple wrapper over GPG.
	/// </summary>
	internal class GPG
	{
		private const string statusMarker = "[GNUPG:] ";
		private const string defaultGpgExePath = @"C:\Program Files (x86)\gnupg\bin\gpg.exe";
		private readonly TimeSpan gpgCallTimeout = TimeSpan.FromSeconds(5);
		private readonly string gpgExePath = defaultGpgExePath;

		/// <summary>
		/// Initialises the wrapper.
		/// </summary>
		/// <param name="gpgBinDir">The path to GPG's bin directory When set to null,
		/// the default location will be used.</param>
		public GPG(string gpgBinDir)
		{
			if (gpgBinDir != null)
			{
				gpgExePath = Path.Combine(gpgBinDir, "gpg.exe");
			}
		}

		private GpgResult CallGpg(string arguments, string input = null)
		{
			// Maybe use --display-charset utf-8?
			var argList = new List<string>
			{
				"--batch", // Ensure GPG does not ask for input or user action
				"--no-tty", // Let GPG know we're not a TTY
				"--status-fd 2", // Write status messages to stderr
				"--with-colons", // Use colon notation for displaying keys
				"--exit-on-status-write-error", //  Exit if status messages cannot be written
			};
			if (ConfigManager.Config.GnupghomeOverride != null)
			{
				 argList.Add($"--homedir \"{ConfigManager.Config.GnupghomeOverride}\"");
			}
			var psi = new ProcessStartInfo
			{
				FileName = gpgExePath,
				Arguments = $"{string.Join(" ", argList)} {arguments}",
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				CreateNoWindow = true
			};
			if (input != null)
			{
				// Only redirect stdin if we're going to send anything to it.
				psi.RedirectStandardInput = true;
			}
			var gpgProc = Process.Start(psi);
			if (input != null)
			{
				gpgProc.StandardInput.WriteLine(input);
				gpgProc.StandardInput.Flush();
				gpgProc.StandardInput.Close();
			}
			gpgProc.WaitForExit((int) gpgCallTimeout.TotalMilliseconds);

			
			string stderrLine;
			var stderrMessages = new List<string>();
			var statusMessages = new List<StatusMessage>();
			while((stderrLine = gpgProc.StandardError.ReadLine()) != null)
			{
				if (stderrLine.StartsWith(statusMarker))
				{
					// This line is a status line, so extract status information from it.
					var statusLine = stderrLine.Substring(statusMarker.Length);
					var spaceIndex = statusLine.IndexOf(" ");
					if (spaceIndex == -1)
					{
						statusMessages.Add(new StatusMessage(statusLine, null));
					}
					else
					{
						var statusLabel = statusLine.Substring(0, spaceIndex);
						// Length+1 because the space after the status label should be skipped.
						var statusMessage = statusLine.Substring(statusLabel.Length + 1);
						statusMessages.Add(new StatusMessage(statusLabel, statusMessage));
					}
				}
				else
				{
					stderrMessages.Add(stderrLine);
				}
			}
			var output = gpgProc.StandardOutput.ReadToEnd();

			return new GpgResult(gpgProc.ExitCode, output, statusMessages, stderrMessages);
		}

		/// <summary>
		/// Decrypt a file with GPG.
		/// </summary>
		/// <param name="file">The path to the file to be decrypted.</param>
		/// <returns>The contents of the decrypted file.</returns>
		/// <exception cref="GpgException">Thrown when decryption fails.</exception>
		public string Decrypt(string file)
		{
			var result = CallGpg($"--decrypt \"{file}\"");
			VerifyDecryption(result);
			return result.Stdout;
		}

		/// <summary>
		/// Decrypt a file to a plaintext file with GPG.
		/// </summary>
		/// <param name="encryptedFile">The path to the file to be decrypted.</param>
		/// <param name="outputFile">The path where the decrypted file should be placed.</param>
		/// <exception cref="GpgException">Thrown when decryption fails.</exception>
		public void DecryptToFile(string encryptedFile, string outputFile)
		{
			var result = CallGpg($"--output \"{outputFile}\" --decrypt \"{encryptedFile}\"");
			VerifyDecryption(result);
		}

		private void VerifyDecryption(GpgResult result)
		{
			if (result.HasStatusCodes(GpgStatusCode.FAILURE, GpgStatusCode.NODATA))
			{
				throw new GpgError("The file to be decrypted does not look like a valid GPG file.");
			}
			if (result.HasStatusCodes(GpgStatusCode.DECRYPTION_FAILED, GpgStatusCode.NO_SECKEY))
			{
				var keyIds = result.StatusMessages.Where(m => m.StatusCode == GpgStatusCode.NO_SECKEY);

				throw new GpgError($"None of your private keys appear to be able to decrypt this file.\n" +
				                   $"The file was encrypted for the following (sub)key(s): {string.Join(", ", keyIds.Select(m => m.Message))}");
			}
			if (result.HasStatusCodes(GpgStatusCode.FAILURE))
			{
				result.GenerateError();
			}

			result.EnsureNonZeroExitCode();
		}

		private void VerifyEncryption(GpgResult result)
		{
			if (result.HasStatusCodes(GpgStatusCode.FAILURE, GpgStatusCode.INV_RECP))
			{
				var failedrcps = result.StatusMessages.Where(m => m.StatusCode == GpgStatusCode.INV_RECP).Select(m => m.Message.Substring(m.Message.IndexOf(" ")));
				throw new GpgError($"Invalid/unknown recipient(s): {string.Join(", ", failedrcps)}\n" +
				                   $"Make sure that you have imported and trusted the keys belonging to those recipients, and that they have not expired.");
			}
			result.EnsureNonZeroExitCode();
		}

		/// <summary>
		/// Encrypt a string with GPG.
		/// </summary>
		/// <param name="data">The text to be encrypted.</param>
		/// <param name="outputFile">The path to the output file.</param>
		/// <param name="recipients">An array of GPG ids for which the file should be encrypted.</param>
		/// <exception cref="GpgException">Thrown when encryption fails.</exception>
		public void Encrypt(string data, string outputFile, params string[] recipients)
		{
			if (recipients == null) recipients = new string[0];
			var recipientList = string.Join(" ", recipients.Select(r => $"--recipient \"{r}\""));

			var result = CallGpg($"--output \"{outputFile}\" {recipientList} --encrypt", data);
			VerifyEncryption(result);
		}

		/// <summary>
		/// Encrypt a file with GPG.
		/// </summary>
		/// <param name="inputFile">The path to the file to be encrypted.</param>
		/// <param name="outputFile">The path to the output file.</param>
		/// <param name="recipients">An array of GPG ids for which the file should be encrypted.</param>
		/// <exception cref="GpgException">Thrown when encryption fails.</exception>
		public void EncryptFile(string inputFile, string outputFile, params string[] recipients)
		{
			if (recipients == null) recipients = new string[0];
			var recipientList = string.Join(" ", recipients.Select(r => $"--recipient \"{r}\""));

			var result = CallGpg($"--output \"{outputFile}\" {recipientList} --encrypt \"{inputFile}\"");
			result.EnsureNonZeroExitCode();
			VerifyEncryption(result);
		}

		private void ListSecretKeys()
		{
			var result = CallGpg("--list-secret-keys");
			if (result.Stdout.Length == 0)
			{
				throw new GpgError("No private keys found. Pass-winmenu will not be able to decrypt your passwords.");
			}
			// At some point in the future we might have a use for this data,
			// But for now, all we really use this method for is to ensure the GPG agent is started.
		}

		public void StartAgent()
		{
			// Looking up a private key will start the GPG agent.
			ListSecretKeys();
		}
	}

	internal class GpgResult
	{
		public StatusMessage[] StatusMessages { get; }
		public string[] StderrMessages { get; }
		public string Stdout { get; }
		public int ExitCode { get; }

		private IEnumerable<GpgStatusCode> StatusCodes => StatusMessages.Select(m => m.StatusCode);

		public GpgResult(int exitCode, string stdout, IEnumerable<StatusMessage> statusMessages, IEnumerable<string> stderrMessages)
		{
			ExitCode = exitCode;
			Stdout = stdout;
			StatusMessages = statusMessages.ToArray();
			StderrMessages = stderrMessages.ToArray();
		}

		public void GenerateError()
		{
			throw new GpgException($"GPG returned the following errors: \n{string.Join("\n", StderrMessages.Select(m => "    "+m))}");
		}

		public void EnsureNonZeroExitCode()
		{
			if (ExitCode != 0)
			{
				throw new GpgException($"GPG exited with status {ExitCode}\n\nOutput:\n{string.Join("\n", StderrMessages)}");
			}
		}
		
		public bool HasStatusCodes(params GpgStatusCode[] required)
		{
			return required.All(StatusCodes.Contains);
		}

		public void EnsureSuccess(GpgStatusCode[] requiredCodes, GpgStatusCode[] disallowedCodes)
		{
			var missing = requiredCodes.Where(c => !StatusCodes.Contains(c)).ToList();
			if (missing.Count > 0)
			{
				throw new GpgException($"Expected status(es) \"{string.Join(", ", missing)}\" not returned by GPG");
			}
			var present = disallowedCodes.Where(c => StatusCodes.Contains(c)).ToList();
			if (present.Count > 0)
			{
				throw new GpgException($"GPG returned disallowes status(es) \"{string.Join(", ", present)}\"");
			}
		}
	}

	/// <summary>
	/// Represents a generic GPG error.
	/// The exception message does not necessarily contain information useful to the user,
	/// and may contain cryptic error messages directly passed on from GPG.
	/// </summary>
	internal class GpgException : Exception
	{
		public GpgException(string message) : base(message) { }
	}

	/// <summary>
	/// Represents an error type that is recognised by <see cref="GPG"/>.
	/// The exception message contains useful information that can be displayed directly to the user.
	/// </summary>
	internal class GpgError : GpgException
	{
		public GpgError(string message) : base(message) { }
	}

	// Refer to the GPG source code, doc/DETAILS for a detailed explanation of status codes and their meaning.
	internal class StatusMessage
	{
		public GpgStatusCode StatusCode { get; }
		public string RawStatusCode { get; }
		public string Message { get; }

		public StatusMessage(string rawStatusCode, string message)
		{
			RawStatusCode = rawStatusCode;
			Message = message;
			if (Enum.TryParse(rawStatusCode, false, out GpgStatusCode parsedStatusCode))
			{
				StatusCode = parsedStatusCode;
			}
			else
			{
				StatusCode = GpgStatusCode.UnknownStatusCode;
			}
		}

		public override string ToString() => $"[{RawStatusCode}] {Message}";
	}

	internal enum GpgStatusCode
	{
		UnknownStatusCode,
		// ReSharper disable InconsistentNaming
		// Match exact status names.
		NEWSIG,
		GOODSIG,
		EXPSIG,
		EXPKEYSIG,
		REVKEYSIG,
		BADSIG,
		ERRSIG,
		VALIDSIG,
		SIG_ID,
		ENC_TO,
		BEGIN_DECRYPTION,
		END_DECRYPTION,
		DECRYPTION_KEY,
		DECRYPTION_INFO,
		DECRYPTION_FAILED,
		DECRYPTION_OKAY,
		SESSION_KEY,
		BEGIN_ENCRYPTION,
		END_ENCRYPTION,
		FILE_START,
		FILE_DONE,
		BEGIN_SIGNING,
		ALREADY_SIGNED,
		SIG_CREATED,
		NOTATION_,
		POLICY_URL,
		PLAINTEXT,
		PLAINTEXT_LENGTH,
		ATTRIBUTE,
		SIG_SUBPACKET,
		ENCRYPTION_COMPLIANCE_MODE,
		DECRYPTION_COMPLIANCE_MODE,
		VERIFICATION_COMPLIANCE_MODE,
		INV_RECP,
		NO_RECP,
		NO_SGNR,
		KEY_CONSIDERED,
		KEYEXPIRED,
		KEYREVOKED,
		NO_PUBKEY,
		NO_SECKEY,
		KEY_CREATED,
		KEY_NOT_CREATED,
		TRUST_,
		TOFU_USER,
		TOFU_STATS,
		TOFU_STATS_SHORT,
		TOFU_STATS_LONG,
		PKA_TRUST_,
		GET_BOOL,
		USERID_HINT,
		NEED_PASSPHRASE,
		NEED_PASSPHRASE_SYM,
		NEED_PASSPHRASE_PIN,
		MISSING_PASSPHRASE,
		BAD_PASSPHRASE,
		GOOD_PASSPHRASE,
		IMPORT_CHECK,
		IMPORTED,
		IMPORT_OK,
		IMPORT_PROBLEM,
		IMPORT_RES,
		EXPORTED,
		EXPORT_RES,
		CARDCTRL,
		SC_OP_FAILURE,
		SC_OP_SUCCESS,
		NODATA,
		UNEXPECTED,
		TRUNCATED,
		ERROR,
		WARNING,
		SUCCESS,
		FAILURE,
		BADARMOR,
		DELETE_PROBLEM,
		PROGRESS,
		BACKUP_KEY_CREATED,
		MOUNTPOINT,
		PINENTRY_LAUNCHED,
		SIGEXPIRED,
		RSA_OR_IDEA,
		SHM_INFO,
		BEGIN_STREAM,
		// These are not listed in the documentation but sometimes returned by GPG
		GOODMDC
	}
}
