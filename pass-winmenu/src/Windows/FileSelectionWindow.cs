using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PassWinmenu.Windows
{
	internal class FileSelectionWindow : SelectionWindow
	{
		private readonly DirectoryAutocomplete autocomplete;
		private readonly string baseDirectory;

		public FileSelectionWindow(string baseDirectory, SelectionWindowConfiguration configuration) : base(configuration)
		{
			this.baseDirectory = baseDirectory;
			autocomplete = new DirectoryAutocomplete(baseDirectory);
		}
		
		protected override void OnContentRendered(EventArgs e)
		{
			base.OnContentRendered(e);

			var completions = autocomplete.GetCompletionList("");
			RedrawLabels(completions);
		}

		protected override void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
		{
			var completions = autocomplete.GetCompletionList(SearchBox.Text).ToList();
			if (!string.IsNullOrWhiteSpace(SearchBox.Text)
				&& !SearchBox.Text.EndsWith("/")
				&& !completions.Contains(SearchBox.Text))
			{
				completions.Insert(0, SearchBox.Text);
			}
			RedrawLabels(completions);
		}

		protected override void HandleSelect()
		{
			// If a suggestion is selected, put that suggestion in the searchbox.
			if (Options.IndexOf(Selected) > 0 || string.IsNullOrEmpty(SearchBox.Text))
			{
				var selection = GetSelection();
				if (selection.EndsWith(Program.EncryptedFileExtension))
				{
					selection = selection.Substring(0, selection.Length - 4);
				}
				SetSearchBoxText(selection);
			}
			else
			{
				if (SearchBox.Text.EndsWith("/"))
				{
					return;
				}
				var selection = GetSelection();
				if (selection.EndsWith(Program.EncryptedFileExtension))
				{
					MessageBox.Show("A .gpg extension will be added automatically and does not need to be entered here.");
					selection = selection.Substring(0, selection.Length - 4);
					SetSearchBoxText(selection);
					return;
				}
				if (File.Exists(Path.Combine(baseDirectory, selection + Program.EncryptedFileExtension)))
				{
					MessageBox.Show($"The password file \"{selection + Program.EncryptedFileExtension}\" already exists.");
					return;
				}
				Success = true;
				Close();
			}
		}
	}
}
