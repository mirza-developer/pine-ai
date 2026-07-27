namespace PineAI.Bots.Shared.Messages;

/// <summary>
/// Centralised collection of user-facing bot message strings shared between
/// <c>PineAI.Bots.Bale</c> and <c>PineAI.Bots.Telegram</c>.
///
/// Messages that contain the platform name (e.g. "بله" / "تلگرام") are exposed
/// as static methods that accept a <paramref name="platformName"/> argument so
/// changing any wording only requires a single edit here.
/// </summary>
public static class BotSharedMessages
{
    // ─── Shared constants ───────────────────────────────────────────────────

    /// <summary>Appended to most support-acknowledgement messages.</summary>
    public const string SupportWaitNotice =
        "\nلطفاً تا ۷۲ ساعت کاری آینده صبوری کنید. درخواست شما بررسی می‌شود. لطفاً دیگر پیام ندهید، پاسخ‌گویی بر اساس آخرین پیام‌ها انجام می‌شود.";

    /// <summary>Sent to a user when a penalty is first applied.</summary>
    public const string PenaltyApplied =
        "⛔ به دلیل رفتار نامناسب مکرر، دسترسی شما به مدت ۱۰ دقیقه محدود شد. " +
        "لطفاً پس از ۱۰ دقیقه مجدداً تلاش کنید.";

    /// <summary>Sent to a user who is already under an active penalty.</summary>
    public const string PenaltyLocked =
        "⛔ دسترسی شما موقتاً محدود است. لطفاً ۱۰ دقیقه صبر کنید.";

    /// <summary>Sent to a user after a Satisfaction feedback is recorded.</summary>
    public const string SatisfactionSuccess =
        "مبارکتون باشه. خوشحالیم تونستیم پاسخ اعتمادتون رو بدیم. به امید دیدار مجدد در خرید های بعدی";

    /// <summary>Sent to a user after an InStoreComplaint feedback is recorded.</summary>
    public const string InStoreComplaintSuccess =
        "✅ پیام شما به پشتیبان‌های ما ارسال شد و تا ۷۲ ساعت کاری پشتیبان به شما پاسخ میده." +
        SupportWaitNotice;

    /// <summary>Sent to a user after a StoreHoursQuery feedback is recorded.</summary>
    public const string StoreHoursQuerySuccess =
        "✅ پیام شما به پشتیبان‌های ما ارسال شد و به زودی ساعت کاری اون تاریخ رو بهتون اطلاع می‌دیم.";

    // ─── Platform-parameterised messages ────────────────────────────────────

    /// <summary>Sent to a user after a Complaint feedback is recorded.</summary>
    public static string ComplaintSuccess(string platformName) =>
        $"📣 اطلاعات شما ثبت شد:\nپشتیبانی ما در {platformName} در اسرع وقت به شما پیام می‌دهد." +
        SupportWaitNotice;

    /// <summary>
    /// Generic "info registered, support will contact you" message.
    /// Used for: DefectiveProduct, PhotoMismatch, ReturnedPackage, WrongSize,
    /// InStoreBillingError, and the unconfigured-TargetChatId fallback.
    /// </summary>
    public static string SupportAcknowledgement(string platformName) =>
        $"✅ اطلاعات شما ثبت شد. پشتیبانی ما در {platformName} در اسرع وقت به شما پیام می‌دهد." +
        SupportWaitNotice;

    /// <summary>Sent to a user after a Wholesale feedback is recorded.</summary>
    public static string WholesaleSuccess(string platformName) =>
        $"✅ درخواست عمده شما ثبت شد. پشتیبانی ما در {platformName} در اسرع وقت به شما پیام می‌دهد." +
        SupportWaitNotice;

    /// <summary>
    /// Sent to a user after a NoOrderCode or FailedPayment feedback is recorded.
    /// </summary>
    public static string ReviewSuccess(string platformName) =>
        $"✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از بررسی در {platformName} به شما پیام می‌دهد." +
        SupportWaitNotice;

    /// <summary>Sent to a user after a DelayedDelivery feedback is recorded.</summary>
    public static string FollowUpSuccess(string platformName) =>
        $"✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از پیگیری در {platformName} به شما پیام می‌دهد." +
        SupportWaitNotice;

    /// <summary>Sent to a user after an UnknownQuery feedback is recorded.</summary>
    public static string MessageReceivedSuccess(string platformName) =>
        $"✅ پیام شما ثبت شد. پشتیبانی ما در {platformName} در اسرع وقت به شما پیام می‌دهد." +
        SupportWaitNotice;
}
