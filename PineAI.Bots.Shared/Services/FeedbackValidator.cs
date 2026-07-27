using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Centralised validation helpers shared between <c>PineAI.Bots.Bale</c> and
/// <c>PineAI.Bots.Telegram</c>.  All methods are static so callers do not need
/// to register an additional service.
/// </summary>
public static class FeedbackValidator
{
    /// <summary>
    /// Maps each FEEDBACK type to the string fields that must be present, non-empty,
    /// and not a literal placeholder value (e.g. "{OrderCode}") before the admin
    /// notification is dispatched.  If any required field fails, the handler falls
    /// back to sending the AI's visible text to the user and skips the admin
    /// notification.
    /// </summary>
    public static readonly Dictionary<string, string[]> RequiredFeedbackFields =
        new(StringComparer.Ordinal)
        {
            // OrderCode intentionally omitted — instructions say do NOT ask if user didn't mention it
            ["Satisfaction"]        = ["Description"],
            ["Complaint"]           = ["OrderCode", "PhoneNumber", "Date", "Description", "FullName"],
            ["DefectiveProduct"]    = ["OrderCode", "PhoneNumber", "FullName", "Description"],
            ["PhotoMismatch"]       = ["OrderCode", "PhoneNumber", "FullName", "Description"],
            ["ReturnedPackage"]     = ["OrderCode", "PhoneNumber", "FullName", "TrackingCode"],
            ["Wholesale"]           = ["PhoneNumber", "FullName", "Description"],
            ["NoOrderCode"]         = ["FullName", "PhoneNumber", "OrderAmount", "PaymentDate"],
            ["FailedPayment"]       = ["PhoneNumber", "FullName", "OrderAmount", "PaymentDate", "Description"],
            ["DelayedDelivery"]     = ["OrderCode", "PhoneNumber", "FullName"],
            ["WrongSize"]           = ["OrderCode", "PhoneNumber", "FullName", "Description"],
            // FullName omitted — user may be anonymous; Description is the minimum useful signal
            ["UnknownQuery"]        = ["Description"],
            ["InStoreBillingError"] = ["PhoneNumber", "FullName", "BranchName", "Description"],
            ["InStoreComplaint"]    = ["PhoneNumber", "FullName", "BranchName", "Description"],
            ["StoreHoursQuery"]     = ["Description"],
        };

    /// <summary>
    /// Returns <c>true</c> when all required fields for <paramref name="feedbackType"/>
    /// are present and valid.  Logs a structured warning for each missing field.
    /// Returns <c>true</c> (allow) when the type is unknown to avoid blocking new types
    /// that have not yet been added to <see cref="RequiredFeedbackFields"/>.
    /// </summary>
    public static bool ValidateFeedbackJson(string feedbackType, JsonElement root, ILogger logger)
    {
        if (!RequiredFeedbackFields.TryGetValue(feedbackType, out var requiredFields))
        {
            logger.LogWarning(
                "Feedback type '{FeedbackType}' has no required-field definition — dispatching without validation",
                feedbackType);
            return true;
        }

        var valid = true;
        foreach (var field in requiredFields)
        {
            if (IsFieldMissing(root, field))
            {
                logger.LogWarning(
                    "Feedback type '{FeedbackType}' blocked: required field '{Field}' is missing or is still a placeholder",
                    feedbackType, field);
                valid = false;
            }
        }

        return valid;
    }

    /// <summary>
    /// Returns <c>true</c> when a field must be considered missing, meaning the admin
    /// notification must NOT be dispatched.  A field is missing when it is:
    /// <list type="bullet">
    ///   <item>absent from the JSON object,</item>
    ///   <item>null, empty, or whitespace, or</item>
    ///   <item>still a literal template placeholder such as <c>{OrderCode}</c> — the AI
    ///         copied the template without substituting a real value.</item>
    /// </list>
    /// </summary>
    public static bool IsFieldMissing(JsonElement root, string fieldName)
    {
        if (!root.TryGetProperty(fieldName, out var element))
            return true;

        if (element.ValueKind == JsonValueKind.Null)
            return true;

        var value = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return true;

        // Detect unresolved template placeholders: {AnyWord}
        var trimmed = value.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '{' && trimmed[^1] == '}')
        {
            var inner = trimmed[1..^1];
            if (inner.Length > 0 && inner.All(char.IsLetter))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a boolean <c>HasPhoto</c> field from the JSON, handling all three cases
    /// the AI may produce: JSON <c>true</c>/<c>false</c> literals, or the strings
    /// <c>"true"</c>/<c>"false"</c>.
    /// </summary>
    public static bool GetHasPhoto(JsonElement root)
    {
        if (!root.TryGetProperty("HasPhoto", out var el))
            return false;

        return el.ValueKind switch
        {
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b,
            _                    => false,
        };
    }

    /// <summary>
    /// Arabic-only characters that must NEVER appear in a <c>&lt;&lt;VERIFICATION&gt;&gt;</c>
    /// block emitted by the AI.  The instruction file mandates that verification text be
    /// written in Persian script only — using <c>ی</c>/<c>ک</c>/<c>ه</c> instead of the
    /// Arabic <c>ي</c>/<c>ك</c>/<c>ة</c>, with no Arabic alif variants or harakat.
    /// </summary>
    public static readonly char[] ArabicOnlyCharacters =
    {
        '\u064A', // ARABIC LETTER YEH         ي  (Persian uses ی U+06CC)
        '\u0643', // ARABIC LETTER KAF         ك  (Persian uses ک U+06A9)
        '\u0629', // ARABIC LETTER TEH MARBUTA ة  (Persian uses ه U+0647)
        '\u0649', // ARABIC LETTER ALEF MAKSURA ى
        '\u0622', // ARABIC LETTER ALEF WITH MADDA ABOVE آ
        '\u0623', // ARABIC LETTER ALEF WITH HAMZA ABOVE أ
        '\u0625', // ARABIC LETTER ALEF WITH HAMZA BELOW إ
        '\u0624', // ARABIC LETTER WAW WITH HAMZA ABOVE ؤ
        '\u0671', // ARABIC LETTER ALEF WASLA ٱ
        '\u064B', // ARABIC FATHATAN  ـً
        '\u064C', // ARABIC DAMMATAN  ـٌ
        '\u064D', // ARABIC KASRATAN  ـٍ
        '\u064E', // ARABIC FATHA     ـَ
        '\u064F', // ARABIC DAMMA     ـُ
        '\u0650', // ARABIC KASRA     ـِ
        '\u0651', // ARABIC SHADDA    ـّ
        '\u0652', // ARABIC SUKUN     ـْ
    };

    /// <summary>
    /// Validates the inner text of a <c>&lt;&lt;VERIFICATION&gt;&gt;</c> block produced
    /// by the AI against the rules defined in the chat-instruction file: the text must be
    /// Persian-only and contain none of <see cref="ArabicOnlyCharacters"/>.
    /// Violations are logged but never thrown — the block is stripped from the visible
    /// reply in every code path, so a malformed verification cannot reach the user.
    /// </summary>
    public static void ValidateAiVerificationText(string? verificationText, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(verificationText))
            return;

        var offendingChars = new HashSet<char>();
        foreach (var c in verificationText)
        {
            if (Array.IndexOf(ArabicOnlyCharacters, c) >= 0)
                offendingChars.Add(c);
        }

        if (offendingChars.Count == 0)
            return;

        var codepoints = string.Join(
            ", ",
            offendingChars.Select(c => $"U+{((int)c):X4} '{c}'"));

        logger.LogWarning(
            "AI <<VERIFICATION>> block violates the Persian-only rule from the instruction file. " +
            "Offending Arabic-only character(s): {Codepoints}. Verification text: {Text}",
            codepoints,
            verificationText);
    }
}
