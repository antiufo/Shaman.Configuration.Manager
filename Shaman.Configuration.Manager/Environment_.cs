using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace System
{
#if !STANDALONE
    [RestrictedAccess]
#endif
    internal static class Environment_
    {
        public static string CurrentDirectory
        {
            get
            {
                return Directory.GetCurrentDirectory();
            }
        }


    }
}
