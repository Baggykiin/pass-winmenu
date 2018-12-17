﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PassWinmenu.UpdateChecking;

namespace PassWinmenu.UpdateChecking.Dummy
{
	internal sealed class DummyUpdateSource : IUpdateSource
	{
		public ProgramVersion LatestVersion { get; set; }
		public List<ProgramVersion> Versions { get; set; }

		public bool RequiresConnectivity => false;

		public ProgramVersion GetLatestVersion()
		{
			return LatestVersion;
		}

		public IEnumerable<ProgramVersion> GetAllReleases()
		{
			return Versions;
		}
	}
}
