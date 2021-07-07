using System;
using System.Collections.Generic;
using PassWinmenu.Configuration;
using PassWinmenu.Windows;

namespace PassWinmenu.Actions
{
	class ActionDispatcher
	{
		private readonly DialogCreator dialogCreator;

		private readonly Dictionary<HotkeyAction, IAction> actions;

		public ActionDispatcher(DialogCreator dialogCreator, Dictionary<HotkeyAction, IAction> actions)
		{
			this.dialogCreator = dialogCreator;
			this.actions = actions;
		}

		public void AddPassword()
		{
			dialogCreator.AddPassword();
		}

		public void EditPassword()
		{
			dialogCreator.EditPassword();
		}

		/// <summary>
		/// Asks the user to choose a password file, decrypts it, and copies the resulting value to the clipboard.
		/// </summary>
		public void DecryptPassword(bool copyToClipboard, bool typeUsername, bool typePassword)
		{
			dialogCreator.DecryptPassword(copyToClipboard, typeUsername, typePassword);
		}

		/// <summary>
		/// Asks the user to choose a password file, decrypts it,
		/// generates an OTP code from the secret in the totp field, and copies the resulting value to the clipboard.
		/// </summary>
		public void GenerateTotpCode(bool copyToClipboard, bool typeTotpCode)
		{
			dialogCreator.GenerateTotpCode(copyToClipboard, typeTotpCode);
		}

		public void DecryptMetadata(bool copyToClipboard, bool type)
		{
			dialogCreator.DecryptMetadata(copyToClipboard, type);
		}

		public void DecryptPasswordField(bool copyToClipboard, bool type, string fieldName = null)
		{
			dialogCreator.GetKey(copyToClipboard, type, fieldName);
		}

		internal void ShowDebugInfo()
		{
			// TODO: determine how to handle this.
			// Either create a new window accessible from 'More Actions',
			// or add the information to the log viewer.
			throw new NotImplementedException("Not implemented.");
		}

		public Action Dispatch(HotkeyAction hotkeyAction)
		{
			if (actions.TryGetValue(hotkeyAction, out var action))
			{
				return action.Execute;
			}
			throw new NotImplementedException("Action does not exist.");
		}
	}
}
