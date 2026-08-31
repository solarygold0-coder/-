using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PatientRecordsSaudi.Services
{
    public sealed class AppSecurity
    {
        private readonly string authPath;
        public AppSecurity(string appDataPath) { authPath = Path.Combine(appDataPath, "auth.dat"); }
        public bool IsConfigured { get { return File.Exists(authPath); } }

        public void Configure(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                throw new ArgumentException("كلمة المرور يجب ألا تقل عن 6 خانات.");
            byte[] salt = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash = Derive(password, salt, 32);
            AtomicWrite(authPath, Convert.ToBase64String(salt) + Environment.NewLine + Convert.ToBase64String(hash));
        }

        public bool Verify(string password)
        {
            try
            {
                string[] lines = File.ReadAllLines(authPath);
                if (lines.Length < 2) return false;
                byte[] salt = Convert.FromBase64String(lines[0]);
                byte[] expected = Convert.FromBase64String(lines[1]);
                byte[] actual = Derive(password ?? "", salt, expected.Length);
                int diff = expected.Length ^ actual.Length;
                for (int i = 0; i < expected.Length && i < actual.Length; i++) diff |= expected[i] ^ actual[i];
                return diff == 0;
            }
            catch { return false; }
        }

        public string DatabasePassword(string password)
        {
            string[] lines = File.ReadAllLines(authPath);
            byte[] salt = Convert.FromBase64String(lines[0]);
            return Convert.ToBase64String(Derive("DB|" + password, salt, 32));
        }

        private static byte[] Derive(string password, byte[] salt, int bytes)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 120000, HashAlgorithmName.SHA256))
                return pbkdf2.GetBytes(bytes);
        }

        private static void AtomicWrite(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
