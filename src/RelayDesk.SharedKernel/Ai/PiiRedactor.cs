using System.Text.RegularExpressions;

namespace RelayDesk.SharedKernel.Ai;

/// <summary>
/// Removes personal data from text before it is sent to a language model (ADR 0008, GDPR data minimisation):
/// email addresses, phone numbers, payment card numbers (Luhn-checked) and IBANs. Placeholders keep the
/// sentence readable for the model, e.g. "write to [email]".
/// </summary>
public static partial class PiiRedactor
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b")]
    private static partial Regex Iban();

    // 13-19 digits, optionally grouped with spaces or dashes.
    [GeneratedRegex(@"\b\d(?:[ \-]?\d){12,18}\b")]
    private static partial Regex CardCandidate();

    // International or local phone numbers: at least 7 digits with common separators.
    [GeneratedRegex(@"(?<!\w)(?:\+\d{1,3}[ .\-]?)?(?:\(?\d{2,4}\)?[ .\-]?){2,4}\d{2,4}(?!\w)")]
    private static partial Regex PhoneCandidate();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var result = Email().Replace(text, "[email]");
        result = Iban().Replace(result, "[iban]");
        result = CardCandidate().Replace(result, m => IsLuhn(Digits(m.Value)) ? "[card]" : m.Value);
        result = PhoneCandidate().Replace(result, m => Digits(m.Value).Length >= 7 && !LooksLikeDateOrNumber(m.Value) ? "[phone]" : m.Value);
        return result;
    }

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    // Avoid redacting things like order numbers "#123456" or years "2024-2026" as phones.
    private static bool LooksLikeDateOrNumber(string s) =>
        !s.Any(c => c is ' ' or '-' or '.' or '(' or '+') || Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$");

    private static bool IsLuhn(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt && (d *= 2) > 9) d -= 9;
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}
