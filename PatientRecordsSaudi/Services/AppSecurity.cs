using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PatientRecordsSaudi.Services
{
    public sealed class SecuritySession
    {
        public string Username { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Role { get; internal set; }
        public string DatabasePassword { get; internal set; }
        public bool IsAdmin { get { return Role == "مدير"; } }
        public bool IsReadOnly { get { return Role == "قراءة فقط"; } }
    }

    public sealed class SecurityUserInfo
    {
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public string StatusText { get { return IsActive ? "فعال" : "معطل"; } }
    }

    internal sealed class SecurityStore
    {
        public int Version { get; set; }
        public List<SecurityUserRecord> Users { get; set; }
    }

    internal sealed class SecurityUserRecord
    {
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public string Salt { get; set; }
        public string Verifier { get; set; }
        public string WrappedKeyIv { get; set; }
        public string WrappedKeyCipher { get; set; }
        public string WrappedKeyMac { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTime? LockoutUntilUtc { get; set; }
    }

    public sealed class AppSecurity
    {
        private const int Iterations = 180000;
        private readonly string authPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public AppSecurity(string appDataPath) { authPath = Path.Combine(appDataPath, "auth.dat"); }
        public bool IsConfigured { get { return File.Exists(authPath); } }

        public SecuritySession Configure(string displayName, string password)
        {
            if (IsConfigured) throw new InvalidOperationException("تم إعداد الحماية مسبقًا.");
            ValidatePassword(password);
            string dbPassword = Convert.ToBase64String(RandomBytes(32));
            var store = new SecurityStore { Version = 2, Users = new List<SecurityUserRecord>() };
            store.Users.Add(CreateRecord("admin", CleanDisplayName(displayName), "مدير", password, dbPassword));
            SaveStore(store);
            return new SecuritySession { Username = "admin", DisplayName = CleanDisplayName(displayName), Role = "مدير", DatabasePassword = dbPassword };
        }

        public SecuritySession Login(string username, string password)
        {
            if (!IsConfigured) throw new InvalidOperationException("لم يتم إعداد الحماية.");
            if (IsLegacyFile()) return LoginAndUpgradeLegacy(username, password);
            SecurityStore store = LoadStore(); string key = NormalizeUsername(username);
            SecurityUserRecord user = store.Users.FirstOrDefault(x => x.Username == key);
            if (user == null || !user.IsActive) throw new UnauthorizedAccessException("اسم المستخدم أو كلمة المرور غير صحيحة.");
            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow) throw new UnauthorizedAccessException("الحساب مقفل مؤقتًا بسبب محاولات دخول متكررة. حاول بعد " + user.LockoutUntilUtc.Value.ToLocalTime().ToString("HH:mm") + ".");
            if (user.LockoutUntilUtc.HasValue) { user.LockoutUntilUtc = null; user.FailedLoginCount = 0; }
            string dbPassword;
            if (!TryUnwrap(user, password, out dbPassword))
            {
                user.FailedLoginCount++; if (user.FailedLoginCount >= 5) { user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15); user.FailedLoginCount = 0; } SaveStore(store);
                throw new UnauthorizedAccessException(user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow ? "تم قفل الحساب لمدة 15 دقيقة بعد خمس محاولات غير صحيحة." : "اسم المستخدم أو كلمة المرور غير صحيحة.");
            }
            if (user.FailedLoginCount != 0 || user.LockoutUntilUtc.HasValue) { user.FailedLoginCount = 0; user.LockoutUntilUtc = null; SaveStore(store); }
            return Session(user, dbPassword);
        }

        public List<SecurityUserInfo> GetUsers(SecuritySession session)
        {
            RequireAdmin(session);
            return LoadStore().Users.Select(x => new SecurityUserInfo { Username = x.Username, DisplayName = x.DisplayName, Role = x.Role, IsActive = x.IsActive }).OrderBy(x => x.Username).ToList();
        }

        public void AddUser(SecuritySession session, string username, string displayName, string role, string password)
        {
            RequireAdmin(session); ValidatePassword(password); ValidateRole(role);
            SecurityStore store = LoadStore(); string key = NormalizeUsername(username);
            if (key.Length < 3) throw new ArgumentException("اسم المستخدم يجب ألا يقل عن 3 خانات.");
            if (store.Users.Any(x => x.Username == key)) throw new InvalidOperationException("اسم المستخدم موجود مسبقًا.");
            store.Users.Add(CreateRecord(key, CleanDisplayName(displayName), role, password, session.DatabasePassword)); SaveStore(store);
        }

        public void ResetPassword(SecuritySession session, string username, string newPassword)
        {
            RequireAdmin(session); ValidatePassword(newPassword); SecurityStore store = LoadStore();
            SecurityUserRecord old = FindUser(store, username); SecurityUserRecord replacement = CreateRecord(old.Username, old.DisplayName, old.Role, newPassword, session.DatabasePassword);
            replacement.IsActive = old.IsActive; store.Users[store.Users.IndexOf(old)] = replacement; SaveStore(store);
        }

        public void ChangePassword(SecuritySession session, string currentPassword, string newPassword)
        {
            ValidatePassword(newPassword); SecurityStore store = LoadStore(); SecurityUserRecord old = FindUser(store, session.Username);
            string dbPassword; if (!TryUnwrap(old, currentPassword, out dbPassword)) throw new UnauthorizedAccessException("كلمة المرور الحالية غير صحيحة.");
            SecurityUserRecord replacement = CreateRecord(old.Username, old.DisplayName, old.Role, newPassword, dbPassword);
            replacement.IsActive = old.IsActive; store.Users[store.Users.IndexOf(old)] = replacement; SaveStore(store);
        }

        public void SetUserState(SecuritySession session, string username, bool active)
        {
            RequireAdmin(session); SecurityStore store = LoadStore(); SecurityUserRecord user = FindUser(store, username);
            if (user.Username == session.Username && !active) throw new InvalidOperationException("لا يمكنك تعطيل حسابك الحالي.");
            if (!active && user.Role == "مدير" && store.Users.Count(x => x.IsActive && x.Role == "مدير") <= 1) throw new InvalidOperationException("يجب إبقاء مدير واحد فعال على الأقل.");
            user.IsActive = active; SaveStore(store);
        }

        private SecuritySession LoginAndUpgradeLegacy(string username, string password)
        {
            string key = NormalizeUsername(username); if (key.Length > 0 && key != "admin") throw new UnauthorizedAccessException("استخدم اسم المستخدم admin للدخول إلى النسخة القديمة.");
            string[] lines = File.ReadAllLines(authPath); if (lines.Length < 2) throw new UnauthorizedAccessException("ملف الحماية غير صالح.");
            byte[] salt = Convert.FromBase64String(lines[0]), expected = Convert.FromBase64String(lines[1]), actual = Derive(password ?? "", salt, expected.Length);
            if (!FixedEquals(expected, actual)) throw new UnauthorizedAccessException("اسم المستخدم أو كلمة المرور غير صحيحة.");
            string dbPassword = Convert.ToBase64String(Derive("DB|" + password, salt, 32));
            var store = new SecurityStore { Version = 2, Users = new List<SecurityUserRecord> { CreateRecord("admin", "مدير النظام", "مدير", password, dbPassword) } };
            File.Copy(authPath, authPath + ".legacy", true); SaveStore(store); return Session(store.Users[0], dbPassword);
        }

        private SecurityUserRecord CreateRecord(string username, string displayName, string role, string password, string dbPassword)
        {
            username = NormalizeUsername(username); byte[] salt = RandomBytes(24), derived = Derive(password, salt, 64), encKey = derived.Take(32).ToArray(), macKey = derived.Skip(32).Take(32).ToArray();
            byte[] iv = RandomBytes(16), cipher, plain = Encoding.UTF8.GetBytes(dbPassword);
            using (Aes aes = Aes.Create()) { aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; using (ICryptoTransform e = aes.CreateEncryptor()) cipher = e.TransformFinalBlock(plain, 0, plain.Length); }
            byte[] verifier, mac; using (var h = new HMACSHA256(macKey)) verifier = h.ComputeHash(Encoding.UTF8.GetBytes("VERIFY|" + username)); using (var h = new HMACSHA256(macKey)) mac = h.ComputeHash(Join(iv, cipher));
            return new SecurityUserRecord { Username = username, DisplayName = CleanDisplayName(displayName), Role = role, IsActive = true, Salt = Convert.ToBase64String(salt), Verifier = Convert.ToBase64String(verifier), WrappedKeyIv = Convert.ToBase64String(iv), WrappedKeyCipher = Convert.ToBase64String(cipher), WrappedKeyMac = Convert.ToBase64String(mac) };
        }

        private bool TryUnwrap(SecurityUserRecord user, string password, out string dbPassword)
        {
            dbPassword = null;
            try
            {
                byte[] salt = Convert.FromBase64String(user.Salt), derived = Derive(password ?? "", salt, 64), encKey = derived.Take(32).ToArray(), macKey = derived.Skip(32).Take(32).ToArray();
                byte[] verifier; using (var h = new HMACSHA256(macKey)) verifier = h.ComputeHash(Encoding.UTF8.GetBytes("VERIFY|" + user.Username)); if (!FixedEquals(Convert.FromBase64String(user.Verifier), verifier)) return false;
                byte[] iv = Convert.FromBase64String(user.WrappedKeyIv), cipher = Convert.FromBase64String(user.WrappedKeyCipher), mac; using (var h = new HMACSHA256(macKey)) mac = h.ComputeHash(Join(iv, cipher)); if (!FixedEquals(Convert.FromBase64String(user.WrappedKeyMac), mac)) return false;
                using (Aes aes = Aes.Create()) { aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; using (ICryptoTransform d = aes.CreateDecryptor()) dbPassword = Encoding.UTF8.GetString(d.TransformFinalBlock(cipher, 0, cipher.Length)); }
                return true;
            }
            catch { return false; }
        }

        private SecurityStore LoadStore() { SecurityStore s = json.Deserialize<SecurityStore>(File.ReadAllText(authPath, Encoding.UTF8)); if (s == null || s.Version != 2 || s.Users == null || s.Users.Count == 0) throw new InvalidDataException("ملف الحماية غير صالح."); return s; }
        private void SaveStore(SecurityStore store) { AtomicWrite(authPath, json.Serialize(store)); }
        private bool IsLegacyFile() { using (var r = new StreamReader(authPath, Encoding.UTF8, true)) { int c; do { c = r.Read(); } while (c >= 0 && char.IsWhiteSpace((char)c)); return c != '{'; } }
        private static SecuritySession Session(SecurityUserRecord u, string db) { return new SecuritySession { Username = u.Username, DisplayName = u.DisplayName, Role = u.Role, DatabasePassword = db }; }
        private static SecurityUserRecord FindUser(SecurityStore s, string name) { string key = NormalizeUsername(name); SecurityUserRecord u = s.Users.FirstOrDefault(x => x.Username == key); if (u == null) throw new InvalidOperationException("المستخدم غير موجود."); return u; }
        private static void RequireAdmin(SecuritySession s) { if (s == null || !s.IsAdmin) throw new UnauthorizedAccessException("هذه العملية متاحة للمدير فقط."); }
        private static void ValidatePassword(string p)
        {
            if (string.IsNullOrWhiteSpace(p) || p.Length < 10) throw new ArgumentException("كلمة المرور يجب ألا تقل عن 10 خانات.");
            if (!p.Any(char.IsLetter) || !p.Any(char.IsDigit) || !p.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))) throw new ArgumentException("كلمة المرور يجب أن تتضمن حرفًا ورقمًا ورمزًا خاصًا على الأقل.");
        }
        private static void ValidateRole(string r) { if (r != "مدير" && r != "موظف" && r != "قراءة فقط") throw new ArgumentException("الصلاحية غير صحيحة."); }
        private static string NormalizeUsername(string s) { return (s ?? "").Trim().ToLowerInvariant(); }
        private static string CleanDisplayName(string s) { string v = (s ?? "").Trim(); if (v.Length < 2) throw new ArgumentException("أدخل اسم الموظف بصورة صحيحة."); return v; }
        private static byte[] RandomBytes(int n) { byte[] b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
        private static byte[] Derive(string p, byte[] salt, int n) { using (var d = new Rfc2898DeriveBytes(p, salt, Iterations, HashAlgorithmName.SHA256)) return d.GetBytes(n); }
        private static byte[] Join(byte[] a, byte[] b) { byte[] r = new byte[a.Length + b.Length]; Buffer.BlockCopy(a, 0, r, 0, a.Length); Buffer.BlockCopy(b, 0, r, a.Length, b.Length); return r; }
        private static bool FixedEquals(byte[] a, byte[] b) { if (a == null || b == null) return false; int x = a.Length ^ b.Length; for (int i = 0; i < a.Length && i < b.Length; i++) x |= a[i] ^ b[i]; return x == 0; }
        private static void AtomicWrite(string p, string c) { string t = p + ".tmp"; File.WriteAllText(t, c, new UTF8Encoding(false)); if (File.Exists(p)) File.Replace(t, p, p + ".bak", true); else File.Move(t, p); }
    }
}
