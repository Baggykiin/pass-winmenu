﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PassWinmenu.Hotkeys
{
    // See:
    //      /src/Hotkeys/HotkeyRegistrars.Windows.cs
    //      /src/Hotkeys/HotkeyRegistrars.Control.cs

    /// <summary>
    /// Provides a default set of <see cref="IHotkeyRegistrar"/>s.
    /// </summary>
    public static partial class HotkeyRegistrars
    {
        /// <summary>
        /// A registrar for registering system-wide hotkeys through the
        /// Windows API.
        /// </summary>
        public static IHotkeyRegistrar Windows => WindowsHotkeyRegistrar.Retrieve();
    }
}
