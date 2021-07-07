using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PassWinmenu.Configuration;

namespace PassWinmenu.PasswordManagement
{
	internal class PasswordFileParser
	{
		private readonly UsernameDetectionConfig usernameDetection;

		public PasswordFileParser(UsernameDetectionConfig usernameDetection)
		{
			this.usernameDetection = usernameDetection;
		}

		/// <summary>
		/// Extracts the username and any possible metadata from a password file
		/// by auto-detecting the correct line-endings.
		/// </summary>
		/// <param name="file">A <see cref="PasswordFile"/> specifying the file to be decrypted.</param>
		/// <param name="content">Content of the password file</param>
		/// <param name="entireFile">If set to true, any line endings are considered to be part of the password.</param>
		/// <returns>A <see cref="KeyedPasswordFile"/> structure containing the password and metadata</returns>
		public KeyedPasswordFile Parse(PasswordFile file, string content, bool entireFile)
		{
			if (entireFile)
			{
				// If the password contains any line endings, there is no additional metadata available.
				return new KeyedPasswordFile(file, content, null, null);
			}
			else
			{
				// The first line contains the password, any other lines contain additional (contextual) content.
				var match = Regex.Match(content, @"([^\n\r]*)(?:(?:\r\n|\n|\r)(.*))?", RegexOptions.Singleline);
				var password = match.Groups[1].Value;
				var metadata = match.Groups[2].Value;

				var keys = ExtractKeys(metadata);

				return new KeyedPasswordFile(file, password, metadata, keys.ToList());
			}
		}

		private static IEnumerable<KeyValuePair<string, string>> ExtractKeys(string metadata)
		{
			var matches = Regex.Matches(metadata, @"([A-z0-9-_ ]+):(.*?)([\r\n]+|$)", RegexOptions.Singleline);
			foreach (Match match in matches)
			{
				var key = match.Groups[1].Value;
				var value = match.Groups[2].Value;
				yield return new KeyValuePair<string, string>(key, value);
			}
		}

		/// <summary>
		/// Attempts to retrieve the username from a password file.
		/// </summary>
		/// <param name="passwordFile">
		/// A <see cref="ParsedPasswordFile"/> representing the password file from which the username should be fetched.
		/// </param>
		/// <returns>
		/// A string containing the username if the password file contains one, null if no username was found.
		/// </returns>
		public string GetUsername(ParsedPasswordFile passwordFile)
		{
			var options = usernameDetection.Options;
			switch (usernameDetection.Method)
			{
				case UsernameDetectionMethod.FileName:
					return passwordFile.FileNameWithoutExtension;
				case UsernameDetectionMethod.LineNumber:
					var extraLines = passwordFile.Metadata.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
					var lineNumber = options.LineNumber - 2;
					if (lineNumber < 0) throw new PasswordParseException($"The username may not be located on line #{options.LineNumber}.");
					return lineNumber < extraLines.Length ? extraLines[lineNumber] : null;
				case UsernameDetectionMethod.Regex:
					var rgxOptions = options.RegexOptions.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
					rgxOptions = rgxOptions | (options.RegexOptions.Multiline ? RegexOptions.Multiline : RegexOptions.None);
					rgxOptions = rgxOptions | (options.RegexOptions.Singleline ? RegexOptions.Singleline : RegexOptions.None);
					var match = Regex.Match(passwordFile.Metadata, options.Regex, rgxOptions);
					return match.Groups["username"].Success ? match.Groups["username"].Value : null;
				default:
					throw new ArgumentOutOfRangeException("username-detection.method", "Invalid username detection method.");
			}
		}
	}

	public class PasswordParseException : Exception
	{
		public PasswordParseException(string message) : base(message)
		{
			
		}
	}
}
