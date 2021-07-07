namespace PassWinmenu.Configuration
{
	internal class NotificationTypesConfig
	{
		public bool PasswordCopied { get; set; } = true;
		public bool TotpCopied { get; set; } = true;
		public bool PasswordGenerated { get; set; } = false;
		public bool PasswordUpdated { get; set; } = true;
		public bool GitPush { get; set; } = true;
		public bool GitPull { get; set; } = true;
		public bool UpdateAvailable { get; set; } = true;
		public bool ImportantUpdateAvailable { get; set; } = true;
	}
}
