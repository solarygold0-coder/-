using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PatientRecordsSaudi.Services
{
    public sealed class SecuritySession : IDisposable
    {
        public string Username { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Role { get; internal set; }
        internal byte[] DatabaseKeyBytes { get; set; }
        public bool UsesDefaultCredentials { get; internal set; }
        public bool IsAdmin { get { return Role == "مدير"; } }
        public bool IsReadOnly { get { return Role == "قراءة فقط"; } }
        public string MaterializeDatabasePassword()
        {
            if (DatabaseKeyBytes == null || DatabaseKeyBytes.Length == 0) throw new ObjectDisposedException(nameof(SecuritySession));
            return Convert.ToBase64String(DatabaseKeyBytes);
        }
        public void Dispose()
        {
            if (DatabaseKeyBytes != null) { CryptographicOperations.ZeroMemory(DatabaseKeyBytes); DatabaseKeyBytes = null; }
        }
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
        public SecurityStore() { }
        public int Version { get; set; }
        public List<SecurityUserRecord> Users { get; set; }
    }

    internal sealed class SecurityUserRecord
    {
        public SecurityUserRecord() { }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public string Salt { get; set; }
        public string Verifier { get; set; }
        public string WrappedKeyIv { get; set; }
        public string WrappedKeyCipher { get; set; }
        public string WrappedKeyMac { get; set; }
        public int EncryptionVersion { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTime? LockoutUntilUtc { get; set; }
    }

    public sealed class SecurityAuditEvent
    {
        public DateTime OccurredAt { get; set; }
        public string UserName { get; set; }
        public string Action { get; set; }
        public string Details { get; set; }
    }

    public sealed class AppSecurity
    {
        private const int Iterations = 180000;
        private readonly string authPath, auditPath;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        public AppSecurity(string appDataPath) { authPath = Path.Combine(appDataPath, "auth.dat"); auditPath = Path.Combine(appDataPath, "security.audit"); TryDelete(authPath + ".bak"); TryDelete(authPath + ".legacy"); }
        public bool IsConfigured { get { return File.Exists(authPath); } }

        public bool EnsureDefaultConfiguration()
        {
            if (IsConfigured) return false;
            byte[] dbKey = RandomBytes(32);
            try
            {
                var store = new SecurityStore { Version = 2, Users = new List<SecurityUserRecord>() };
                store.Users.Add(CreateRecord("admin", "مدير النظام", "مدير", "admin", Convert.ToBase64String(dbKey)));
                SaveStore(store); TryLog("admin", "إعداد الحماية", "إنشاء الحساب الافتراضي؛ يلزم تغيير كلمة المرور");
                return true;
            }
            finally { CryptographicOperations.ZeroMemory(dbKey); }
        }

        public SecuritySession Configure(string displayName, string password)
        {
            if (IsConfigured) throw new InvalidOperationException("تم إعداد الحماية مسبقًا.");
            ValidatePassword(password);
            byte[] dbKey = RandomBytes(32); string dbPassword = Convert.ToBase64String(dbKey);
            var store = new SecurityStore { Version = 2, Users = new List<SecurityUserRecord>() };
            store.Users.Add(CreateRecord("admin", CleanDisplayName(displayName), "مدير", password, dbPassword));
            SaveStore(store); TryLog("admin", "إعداد الحماية", "إنشاء حساب المدير الأول");
            return new SecuritySession { Username = "admin", DisplayName = CleanDisplayName(displayName), Role = "مدير", DatabaseKeyBytes = dbKey };
        }

        public SecuritySession Login(string username, string password)
        {
            if (!IsConfigured) throw new InvalidOperationException("لم يتم إعداد الحماية.");
            if (IsLegacyTwoLineFile()) return LoginAndUpgradeLegacy(username, password);
            SecurityStore store = LoadStore(); string key = NormalizeUsername(username);
            SecurityUserRecord user = store.Users.FirstOrDefault(x => x.Username == key);
            if (user == null || !user.IsActive) { TryLog(key, "محاولة دخول مرفوضة", user == null ? "حساب غير موجود" : "حساب معطل"); throw new UnauthorizedAccessException("اسم المستخدم أو كلمة المرور غير صحيحة."); }
            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow) { TryLog(key, "محاولة دخول أثناء القفل", "الحساب مقفل مؤقتًا"); throw new UnauthorizedAccessException("الحساب مقفل مؤقتًا بسبب محاولات دخول متكررة. حاول بعد " + user.LockoutUntilUtc.Value.ToLocalTime().ToString("HH:mm") + "."); }
            bool clearedExpiredLock = user.LockoutUntilUtc.HasValue; if (clearedExpiredLock) { user.LockoutUntilUtc = null; user.FailedLoginCount = 0; }
            string dbPassword;
            if (!TryUnwrap(user, password, out dbPassword))
            {
                user.FailedLoginCount++; if (user.FailedLoginCount >= 5) { user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15); user.FailedLoginCount = 0; } SaveStore(store); TryLog(key, user.LockoutUntilUtc.HasValue ? "قفل حساب" : "فشل تسجيل دخول", user.LockoutUntilUtc.HasValue ? "خمس محاولات غير صحيحة" : "كلمة مرور غير صحيحة");
                throw new UnauthorizedAccessException(user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow ? "تم قفل الحساب لمدة 15 دقيقة بعد خمس محاولات غير صحيحة." : "اسم المستخدم أو كلمة المرور غير صحيحة.");
            }
            if (clearedExpiredLock || user.FailedLoginCount != 0 || user.LockoutUntilUtc.HasValue) { user.FailedLoginCount = 0; user.LockoutUntilUtc = null; SaveStore(store); }
            if (user.EncryptionVersion < 2)
            {
                SecurityUserRecord upgraded = CreateRecord(user.Username, user.DisplayName, user.Role, password, dbPassword); upgraded.IsActive = user.IsActive; store.Users[store.Users.IndexOf(user)] = upgraded; user = upgraded; SaveStore(store);
            }
            TryLog(key, "تسجيل دخول ناجح", user.Role);
            return Session(user, dbPassword, key == "admin" && password == "admin");
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
            store.Users.Add(CreateRecord(key, CleanDisplayName(displayName), role, password, session.MaterializeDatabasePassword())); SaveStore(store); TryLog(session.Username, "إضافة حساب", key + " - " + role);
        }

        public void ResetPassword(SecuritySession session, string username, string newPassword)
        {
            RequireAdmin(session); ValidatePassword(newPassword); SecurityStore store = LoadStore();
            SecurityUserRecord old = FindUser(store, username); SecurityUserRecord replacement = CreateRecord(old.Username, old.DisplayName, old.Role, newPassword, session.MaterializeDatabasePassword());
            replacement.IsActive = old.IsActive; store.Users[store.Users.IndexOf(old)] = replacement; SaveStore(store); TryLog(session.Username, "إعادة تعيين كلمة مرور", old.Username);
        }

        public void ChangePassword(SecuritySession session, string currentPassword, string newPassword)
        {
            ValidatePassword(newPassword); SecurityStore store = LoadStore(); SecurityUserRecord old = FindUser(store, session.Username);
            string dbPassword; if (!TryUnwrap(old, currentPassword, out dbPassword)) throw new UnauthorizedAccessException("كلمة المرور الحالية غير صحيحة.");
            SecurityUserRecord replacement = CreateRecord(old.Username, old.DisplayName, old.Role, newPassword, dbPassword);
            replacement.IsActive = old.IsActive; store.Users[store.Users.IndexOf(old)] = replacement; SaveStore(store); session.UsesDefaultCredentials = false; TryLog(session.Username, "تغيير كلمة المرور", session.Username);
        }

        public void SetUserState(SecuritySession session, string username, bool active)
        {
            RequireAdmin(session); SecurityStore store = LoadStore(); SecurityUserRecord user = FindUser(store, username);
            if (user.Username == session.Username && !active) throw new InvalidOperationException("لا يمكنك تعطيل حسابك الحالي.");
            if (!active && user.Role == "مدير" && store.Users.Count(x => x.IsActive && x.Role == "مدير") <= 1) throw new InvalidOperationException("يجب إبقاء مدير واحد فعال على الأقل.");
            user.IsActive = active; SaveStore(store); TryLog(session.Username, active ? "تفعيل حساب" : "تعطيل حساب", user.Username);
        }

        private SecuritySession LoginAndUpgradeLegacy(string username, string password)
        {
            string key = NormalizeUsername(username); if (key.Length > 0 && key != "admin") throw new UnauthorizedAccessException("استخدم اسم المستخدم admin للدخول إلى النسخة القديمة.");
            string[] lines = File.ReadAllLines(authPath); if (lines.Length < 2) throw new UnauthorizedAccessException("ملف الحماية غير صالح.");
            byte[] salt = Convert.FromBase64String(lines[0]), expected = Convert.FromBase64String(lines[1]), actual = Derive(password ?? "", salt, expected.Length);
            if (!FixedEquals(expected, actual)) throw new UnauthorizedAccessException("اسم المستخدم أو كلمة المرور غير صحيحة.");
            string dbPassword = Convert.ToBase64String(Derive("DB|" + password, salt, 32));
            var store = new SecurityStore { Version = 2, Users = new List<SecurityUserRecord> { CreateRecord("admin", "مدير النظام", "مدير", password, dbPassword) } };
            SaveStore(store); TryLog("admin", "ترقية ملف الحماية", "الانتقال إلى تنسيق الحسابات الجديد"); return Session(store.Users[0], dbPassword, false);
        }

        private SecurityUserRecord CreateRecord(string username, string displayName, string role, string password, string dbPassword)
        {
            username = NormalizeUsername(username); byte[] salt = RandomBytes(24), derived = Derive(password, salt, 64), encKey = derived.Take(32).ToArray(), macKey = derived.Skip(32).Take(32).ToArray();
            byte[] nonce = RandomBytes(12), plain = Encoding.UTF8.GetBytes(dbPassword), cipher = new byte[plain.Length], tag = new byte[16], verifier;
            try
            {
                using (var gcm = new AesGcm(encKey, tag.Length)) gcm.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(username));
                using (var h = new HMACSHA256(macKey)) verifier = h.ComputeHash(Encoding.UTF8.GetBytes("VERIFY|" + username));
                return new SecurityUserRecord { Username = username, DisplayName = CleanDisplayName(displayName), Role = role, IsActive = true, Salt = Convert.ToBase64String(salt), Verifier = Convert.ToBase64String(verifier), WrappedKeyIv = Convert.ToBase64String(nonce), WrappedKeyCipher = Convert.ToBase64String(cipher), WrappedKeyMac = Convert.ToBase64String(tag), EncryptionVersion = 2 };
            }
            finally { CryptographicOperations.ZeroMemory(derived); CryptographicOperations.ZeroMemory(encKey); CryptographicOperations.ZeroMemory(macKey); CryptographicOperations.ZeroMemory(plain); }
        }

        private bool TryUnwrap(SecurityUserRecord user, string password, out string dbPassword)
        {
            dbPassword = null; byte[] derived = null, encKey = null, macKey = null, protectedValue = null;
            try
            {
                byte[] salt = Convert.FromBase64String(user.Salt); derived = Derive(password ?? "", salt, 64); encKey = derived.Take(32).ToArray(); macKey = derived.Skip(32).Take(32).ToArray();
                byte[] verifier; using (var h = new HMACSHA256(macKey)) verifier = h.ComputeHash(Encoding.UTF8.GetBytes("VERIFY|" + user.Username)); if (!FixedEquals(Convert.FromBase64String(user.Verifier), verifier)) return false;
                byte[] iv = Convert.FromBase64String(user.WrappedKeyIv), cipher = Convert.FromBase64String(user.WrappedKeyCipher);
                if (user.EncryptionVersion >= 2)
                {
                    protectedValue = new byte[cipher.Length]; using (var gcm = new AesGcm(encKey, 16)) gcm.Decrypt(iv, cipher, Convert.FromBase64String(user.WrappedKeyMac), protectedValue, Encoding.UTF8.GetBytes(user.Username));
                }
                else
                {
                    byte[] mac; using (var h = new HMACSHA256(macKey)) mac = h.ComputeHash(Join(iv, cipher)); if (!FixedEquals(Convert.FromBase64String(user.WrappedKeyMac), mac)) return false;
                    using (Aes aes = Aes.Create()) { aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; using (ICryptoTransform d = aes.CreateDecryptor()) protectedValue = d.TransformFinalBlock(cipher, 0, cipher.Length); }
                }
                dbPassword = Encoding.UTF8.GetString(protectedValue);
                return true;
            }
            catch { return false; }
            finally
            {
                if (protectedValue != null) CryptographicOperations.ZeroMemory(protectedValue);
                if (derived != null) CryptographicOperations.ZeroMemory(derived);
                if (encKey != null) CryptographicOperations.ZeroMemory(encKey);
                if (macKey != null) CryptographicOperations.ZeroMemory(macKey);
            }
        }

        private SecurityStore LoadStore()
        {
            byte[] stored = File.ReadAllBytes(authPath), plain; bool migratePlaintext = false;
            try { plain = ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser); }
            catch (CryptographicException)
            {
                string legacyJson = Encoding.UTF8.GetString(stored);
                if (!legacyJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) throw new InvalidDataException("ملف الحماية غير صالح أو يعود إلى مستخدم Windows آخر.");
                plain = stored; migratePlaintext = true;
            }
            SecurityStore s;
            try { s = JsonSerializer.Deserialize<SecurityStore>(Encoding.UTF8.GetString(plain), JsonOptions); }
            finally { if (!ReferenceEquals(plain, stored)) CryptographicOperations.ZeroMemory(plain); }
            if (s == null || s.Version != 2 || s.Users == null || s.Users.Count == 0) throw new InvalidDataException("ملف الحماية غير صالح.");
            if (migratePlaintext) SaveStore(s);
            return s;
        }
        private void SaveStore(SecurityStore store)
        {
            byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(store, JsonOptions));
            try { AtomicWriteBytes(authPath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        private void TryLog(string userName, string action, string details)
        {
            try
            {
                List<SecurityAuditEvent> items = ReadAudit(); items.Add(new SecurityAuditEvent { OccurredAt = DateTime.Now, UserName = NormalizeUsername(userName), Action = action, Details = details ?? "" }); if (items.Count > 2000) items = items.Skip(items.Count - 2000).ToList();
                byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items, JsonOptions)), encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser), temp = encrypted; string tmp = auditPath + ".tmp"; File.WriteAllBytes(tmp, temp); if (File.Exists(auditPath)) File.Replace(tmp, auditPath, null, true); else File.Move(tmp, auditPath);
            }
            catch { }
        }
        private List<SecurityAuditEvent> ReadAudit()
        {
            if (!File.Exists(auditPath)) return new List<SecurityAuditEvent>(); try { byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(auditPath), null, DataProtectionScope.CurrentUser); return JsonSerializer.Deserialize<List<SecurityAuditEvent>>(Encoding.UTF8.GetString(plain), JsonOptions) ?? new List<SecurityAuditEvent>(); } catch { return new List<SecurityAuditEvent>(); }
        }
        public void FlushPendingAudit(AppDatabase database)
        {
            if (database == null) return; List<SecurityAuditEvent> items = ReadAudit(); if (items.Count == 0) return; foreach (SecurityAuditEvent item in items) database.AuditSecurityEvent(item.UserName, item.Action, item.Details, item.OccurredAt); try { File.Delete(auditPath); } catch { }
        }
        private bool IsLegacyTwoLineFile()
        {
            try
            {
                string value = Encoding.UTF8.GetString(File.ReadAllBytes(authPath));
                if (value.TrimStart().StartsWith("{", StringComparison.Ordinal)) return false;
                string[] lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length != 2) return false;
                Convert.FromBase64String(lines[0].Trim()); Convert.FromBase64String(lines[1].Trim()); return true;
            }
            catch { return false; }
        }
        private static SecuritySession Session(SecurityUserRecord u, string db, bool usesDefaultCredentials)
        {
            byte[] key;
            try { key = Convert.FromBase64String(db); }
            catch (FormatException) { throw new InvalidDataException("مفتاح قاعدة البيانات غير صالح."); }
            return new SecuritySession { Username = u.Username, DisplayName = u.DisplayName, Role = u.Role, DatabaseKeyBytes = key, UsesDefaultCredentials = usesDefaultCredentials };
        }
        private static SecurityUserRecord FindUser(SecurityStore s, string name) { string key = NormalizeUsername(name); SecurityUserRecord u = s.Users.FirstOrDefault(x => x.Username == key); if (u == null) throw new InvalidOperationException("المستخدم غير موجود."); return u; }
        private static void RequireAdmin(SecuritySession s) { if (s == null || !s.IsAdmin) throw new UnauthorizedAccessException("هذه العملية متاحة للمدير فقط."); }
        private static void ValidatePassword(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) throw new ArgumentException("أدخل كلمة المرور.");
            int letters = p.Count(c => c >= 'a' && c <= 'z'), digits = p.Count(c => c >= '0' && c <= '9');
            if (letters < 1 || digits < 1 || letters > 4 || digits > 4 || letters + digits != p.Length)
                throw new ArgumentException("كلمة المرور الجديدة يجب أن تحتوي حرفًا إنجليزيًا صغيرًا ورقمًا على الأقل، وبحد أقصى 4 أحرف و4 أرقام، دون رموز أو أحرف كبيرة.");
        }
        private static void ValidateRole(string r) { if (r != "مدير" && r != "موظف" && r != "قراءة فقط") throw new ArgumentException("الصلاحية غير صحيحة."); }
        private static string NormalizeUsername(string s) { return (s ?? "").Trim().ToLowerInvariant(); }
        private static string CleanDisplayName(string s) { string v = (s ?? "").Trim(); if (v.Length < 2) throw new ArgumentException("أدخل اسم الموظف بصورة صحيحة."); return v; }
        private static byte[] RandomBytes(int n) { byte[] b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
        private static byte[] Derive(string p, byte[] salt, int n) { return Rfc2898DeriveBytes.Pbkdf2(p ?? "", salt, Iterations, HashAlgorithmName.SHA256, n); }
        private static byte[] Join(byte[] a, byte[] b) { byte[] r = new byte[a.Length + b.Length]; Buffer.BlockCopy(a, 0, r, 0, a.Length); Buffer.BlockCopy(b, 0, r, a.Length, b.Length); return r; }
        private static bool FixedEquals(byte[] a, byte[] b) { if (a == null || b == null) return false; int x = a.Length ^ b.Length; for (int i = 0; i < a.Length && i < b.Length; i++) x |= a[i] ^ b[i]; return x == 0; }
        private static void AtomicWriteBytes(string p, byte[] content) { string t = p + ".tmp"; File.WriteAllBytes(t, content); if (File.Exists(p)) File.Replace(t, p, null, true); else File.Move(t, p); TryDelete(p + ".bak"); TryDelete(p + ".legacy"); }
        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}
