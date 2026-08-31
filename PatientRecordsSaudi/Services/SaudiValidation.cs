using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PatientRecordsSaudi.Services
{
    public static class SaudiValidation
    {
        public static string NormalizeDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var result = new StringBuilder(value.Length);
            foreach (char ch in value.Trim())
            {
                if (ch >= '\u0660' && ch <= '\u0669') result.Append((char)('0' + (ch - '\u0660')));
                else if (ch >= '\u06F0' && ch <= '\u06F9') result.Append((char)('0' + (ch - '\u06F0')));
                else result.Append(ch);
            }
            return result.ToString();
        }

        public static bool DigitsOnly(string value)
        {
            string v = NormalizeDigits(value);
            return v.Length > 0 && v.All(c => c >= '0' && c <= '9');
        }

        public static bool ValidateSaudiIdentity(string value, string identityType, out string error)
        {
            string id = NormalizeDigits(value);
            if (!Regex.IsMatch(id, "^[0-9]{10}$"))
            {
                error = "رقم الهوية/الإقامة يجب أن يتكون من 10 أرقام فقط.";
                return false;
            }
            if (identityType == "هوية وطنية" && id[0] != '1')
            {
                error = "رقم الهوية الوطنية السعودية يبدأ بالرقم 1.";
                return false;
            }
            if (identityType == "إقامة" && id[0] != '2')
            {
                error = "رقم الإقامة يبدأ بالرقم 2.";
                return false;
            }
            int sum = 0;
            for (int i = 0; i < 10; i++)
            {
                int digit = id[i] - '0';
                if (i % 2 == 0)
                {
                    int doubled = digit * 2;
                    sum += doubled / 10 + doubled % 10;
                }
                else sum += digit;
            }
            if (sum % 10 != 0)
            {
                error = "رقم الهوية/الإقامة غير صحيح وفق رقم التحقق السعودي.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static bool ValidateSaudiMobile(string value, bool required, out string error)
        {
            string mobile = NormalizeDigits(value).Replace(" ", "").Replace("-", "");
            if (!required && mobile.Length == 0) { error = string.Empty; return true; }
            if (mobile.StartsWith("+966")) mobile = "0" + mobile.Substring(4);
            else if (mobile.StartsWith("966")) mobile = "0" + mobile.Substring(3);
            if (!Regex.IsMatch(mobile, "^05[0-9]{8}$"))
            {
                error = "رقم الجوال يجب أن يكون سعوديًا من 10 أرقام ويبدأ بـ 05.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static string NormalizeSaudiMobile(string value)
        {
            string mobile = NormalizeDigits(value).Replace(" ", "").Replace("-", "");
            if (mobile.StartsWith("+966")) return "0" + mobile.Substring(4);
            if (mobile.StartsWith("966")) return "0" + mobile.Substring(3);
            return mobile;
        }

        public static bool IsOfficialWorkingDay(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Friday && date.DayOfWeek != DayOfWeek.Saturday;
        }

        public static string ArabicDayName(DateTime date)
        {
            string[] days = { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" };
            return days[(int)date.DayOfWeek];
        }

        public static string MonthLabel(int month)
        {
            string[] names = { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
            return month.ToString("00") + " - " + names[month - 1];
        }
    }
}
