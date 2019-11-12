using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileStorageExportTool
{
    internal static class Utils
    {
        public static string DateTimetoString(DateTime aDateTime)
        {
            return aDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
    }
}
