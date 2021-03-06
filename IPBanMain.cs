﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace IPBan
{
    public static class IPBanMain
    {
        public static int Main(string[] args)
        {
            IPBanExtensionMethods.RequireAdministrator();

            if (args.Length != 0 && args[0].Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("System info: {0}", IPBanOS.OSString());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                IPBanWindowsApp.WindowsMain(args);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IPBanLinuxApp.LinuxMain(args);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                throw new PlatformNotSupportedException("Mac OSX is not yet supported, but may be in the future.");
                //IPBanMacApp.MacMain(args);
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
            return 0;
        }
    }
}
