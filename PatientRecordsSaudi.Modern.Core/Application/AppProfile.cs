using System;
using System.IO;

namespace PatientRecordsSaudi.Services
{
    public static class AppProfile
    {
        public const string DataFolderName = "SaudiPatientRecordsV6";
        public const string GenerationMarkerName = ".generation-v6";

        public static string ResolveDataDirectory(string localApplicationData)
        {
            if (string.IsNullOrWhiteSpace(localApplicationData)) throw new ArgumentException("مسار بيانات Windows غير صالح.", nameof(localApplicationData));
            return Path.Combine(localApplicationData, DataFolderName);
        }

        public static void Initialize(string dataDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
            string marker = Path.Combine(dataDirectory, GenerationMarkerName);
            if (!File.Exists(marker)) File.WriteAllText(marker, "Saudi Patient Records independent data generation 6");
        }
    }
}
