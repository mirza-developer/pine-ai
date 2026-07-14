using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PineAI.Bots.Shared.Tools;
using PineAI.Persistence.Services;
using PineAI.Shared;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Centralised factory for building admin-notification log strings shared between
/// <c>PineAI.Bots.Bale</c> and <c>PineAI.Bots.Telegram</c>.
///
/// Every method returns a ready-to-send string; callers are responsible for
/// delivering it to the target admin chat via the platform-specific bot client.
/// </summary>
public static class FeedbackLogBuilder
{
    // ─── Order-lookup helpers ────────────────────────────────────────────────

    /// <summary>
    /// Looks up an order by <paramref name="orderCode"/> and returns a short formatted
    /// status block (with postal tracking link when available), or a "not found" line.
    /// Returns an empty string when <paramref name="orderCode"/> is null or empty.
    /// </summary>
    public static async Task<string> LookupOrderAsync(
        string? orderCode, PineAIDbContext dbContext, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(orderCode))
            return string.Empty;

        orderCode = ResponseBlockTools.NormalizeDigits(orderCode);

        var order = await dbContext.CustomerOrder
            .Include(o => o.OrderStatus)
            .FirstOrDefaultAsync(o => o.OrderCode == orderCode, CancellationToken.None);

        if (order is not null)
        {
            return "\n" +
                $"📦 سفارش «{order.OrderCode}»:\n" +
                $"وضعیت: {order.OrderStatus.Title}\n" +
                (!string.IsNullOrEmpty(order.PostalTrackingCode)
                    ? $"کد مرسوله پستی: {order.PostalTrackingCode}\n"
                    : "") +
                " کد ۲۴ رقمیو بزن تو سایت پست https://tracking.post.ir/ از وضعیت بسته باخبر شو";
        }

        return "\n" + $"❌ سفارشی با کد «{orderCode}» یافت نشد.";
    }

    /// <summary>
    /// Looks up an order by <paramref name="orderCode"/> and returns a status block that
    /// includes the <em>last-update timestamp</em> (used in complaint reports).
    /// Returns an empty string when <paramref name="orderCode"/> is null or empty.
    /// </summary>
    public static async Task<string> LookupComplaintOrderAsync(
        string? orderCode, PineAIDbContext dbContext, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(orderCode))
            return string.Empty;

        var order = await dbContext.CustomerOrder
            .Include(o => o.OrderStatus)
            .FirstOrDefaultAsync(o => o.OrderCode == orderCode, CancellationToken.None);

        if (order is not null)
        {
            return "\n" +
                $"📦 سفارش «{order.OrderCode}»:\n" +
                $"وضعیت: {order.OrderStatus.Title}\n" +
                $"آخرین به‌روزرسانی: {PersianCalendarTools.GregorianToPersian(order.UpdatedAt)} {order.UpdatedAt:HH:mm}";
        }

        return "\n" + $"❌ سفارشی با کد «{orderCode}» یافت نشد.";
    }

    // ─── Log builders ────────────────────────────────────────────────────────

    /// <summary>Builds the admin notification for a Satisfaction feedback.</summary>
    public static string BuildSatisfactionLog(JsonElement root, string userPlatformUsername)
    {
        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() ?? ""       : "";

        return $"🌸 پیام رضایت جدید ثبت شد:\n" +
               $"کد سفارش: {orderCode}\n" +
               $"توضیحات: {description}" +
               userPlatformUsername;
    }

    /// <summary>
    /// Builds the admin notification for a Complaint feedback, including a live order
    /// status lookup.  Pass the pre-fetched order info from
    /// <see cref="LookupComplaintOrderAsync"/>.
    /// </summary>
    public static string BuildComplaintLog(
        JsonElement root, string userPlatformUsername, string orderInfo)
    {
        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var date        = root.TryGetProperty("Date",        out var dtProp)  ? dtProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"📣 شکایت/درخواست پیگیری جدید ثبت شد:\n" +
               $"کد سفارش: {orderCode}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"تاریخ: {date}\n" +
               $"توضیحات: {description}\n" +
               orderInfo +
               userPlatformUsername +
               "\n #case ";
    }

    /// <summary>
    /// Builds the admin notification for a DefectiveProduct feedback.  Pass the
    /// pre-fetched order info from <see cref="LookupOrderAsync"/>.
    /// </summary>
    public static string BuildDefectiveProductLog(
        JsonElement root, string userPlatformUsername, bool hasPhoto, string orderInfo)
    {
        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"⚠️ گزارش محصول معیوب/خراب:\n" +
               $"کد سفارش: {orderCode}\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"توضیحات: {description}\n" +
               $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}\n" +
               orderInfo +
               userPlatformUsername +
               "\n #defective";
    }

    /// <summary>
    /// Builds the admin notification for a PhotoMismatch feedback.  Pass the
    /// pre-fetched order info from <see cref="LookupOrderAsync"/>.
    /// </summary>
    public static string BuildPhotoMismatchLog(
        JsonElement root, string userPlatformUsername, bool hasPhoto, string orderInfo)
    {
        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"📸 گزارش مغایرت عکس و محصول:\n" +
               $"کد سفارش: {orderCode}\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"توضیحات: {description}\n" +
               $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}\n" +
               orderInfo +
               userPlatformUsername +
               "\n #mismatch";
    }

    /// <summary>
    /// Builds the admin notification for a ReturnedPackage feedback.  Pass the
    /// pre-fetched order info from <see cref="LookupOrderAsync"/>.
    /// </summary>
    public static string BuildReturnedPackageLog(
        JsonElement root, string userPlatformUsername, string orderInfo)
    {
        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var trackingCode = root.TryGetProperty("TrackingCode", out var tcProp)  ? tcProp.GetString()  ?? "نامشخص" : "نامشخص";

        return $"📦 گزارش بسته برگشت خورده:\n" +
               $"کد سفارش: {orderCode}\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"کد رهگیری پست: {trackingCode}\n" +
               orderInfo +
               userPlatformUsername +
               "\n #returned";
    }

    /// <summary>Builds the admin notification for a Wholesale feedback.</summary>
    public static string BuildWholesaleLog(JsonElement root, string userPlatformUsername)
    {
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"📦 درخواست سفارش عمده جدید:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"توضیحات: {description}" +
               userPlatformUsername +
               "\n #wholesale";
    }

    /// <summary>Builds the admin notification for a NoOrderCode feedback.</summary>
    public static string BuildNoOrderCodeLog(JsonElement root, string userPlatformUsername)
    {
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var orderAmount = root.TryGetProperty("OrderAmount", out var oaProp)  ? oaProp.GetString()  ?? "نامشخص" : "نامشخص";
        var paymentDate = root.TryGetProperty("PaymentDate", out var pdProp)  ? pdProp.GetString()  ?? "نامشخص" : "نامشخص";

        return $"🔍 درخواست یافتن کد سفارش:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"مبلغ سفارش: {orderAmount}\n" +
               $"تاریخ پرداخت: {paymentDate}" +
               userPlatformUsername +
               "\n #nocode";
    }

    /// <summary>Builds the admin notification for a FailedPayment feedback.</summary>
    public static string BuildFailedPaymentLog(JsonElement root, string userPlatformUsername)
    {
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var orderAmount = root.TryGetProperty("OrderAmount", out var oaProp)  ? oaProp.GetString()  ?? "نامشخص" : "نامشخص";
        var paymentDate = root.TryGetProperty("PaymentDate", out var pdProp)  ? pdProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"💳 گزارش پرداخت ناموفق:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"مبلغ: {orderAmount}\n" +
               $"تاریخ پرداخت: {paymentDate}\n" +
               $"توضیحات: {description}" +
               userPlatformUsername +
               "\n #failedpayment";
    }

    /// <summary>
    /// Builds the admin notification for a DelayedDelivery feedback.  Pass the
    /// pre-fetched order info from <see cref="LookupOrderAsync"/>.
    /// </summary>
    public static string BuildDelayedDeliveryLog(
        JsonElement root, string userPlatformUsername, string orderInfo)
    {
        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp) ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp) ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp) ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";

        return $"⏰ گزارش تاخیر در تحویل (بالای ۸ روز کاری):\n" +
               $"کد سفارش: {orderCode}\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               orderInfo +
               userPlatformUsername +
               "\n #delayed";
    }

    /// <summary>
    /// Builds the admin notification for a WrongSize feedback.  Pass the
    /// pre-fetched order info from <see cref="LookupOrderAsync"/>.
    /// </summary>
    public static string BuildWrongSizeLog(
        JsonElement root, string userPlatformUsername, string orderInfo)
    {
        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"📏 گزارش سایز نامناسب:\n" +
               $"کد سفارش: {orderCode}\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"توضیحات: {description}\n" +
               orderInfo +
               userPlatformUsername +
               "\n #wrongsize";
    }

    /// <summary>Builds the admin notification for an UnknownQuery feedback.</summary>
    public static string BuildUnknownQueryLog(JsonElement root, string userPlatformUsername)
    {
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)   ? fnProp.GetString()   : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        return $"❓ درخواست نامشخص:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"توضیحات: {description}" +
               userPlatformUsername +
               "\n #unknown";
    }

    /// <summary>
    /// Builds the admin notification for an InStoreBillingError feedback.
    /// </summary>
    public static string BuildInStoreBillingErrorLog(
        JsonElement root, string userPlatformUsername, bool hasPhoto)
    {
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var branchName  = root.TryGetProperty("BranchName",  out var bnProp)  ? bnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"🧾 گزارش خطای فاکتور خرید حضوری:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"شعبه: {branchName}\n" +
               $"توضیحات: {description}\n" +
               $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}" +
               userPlatformUsername +
               "\n #instorebillingerror";
    }

    /// <summary>Builds the admin notification for an InStoreComplaint feedback.</summary>
    public static string BuildInStoreComplaintLog(JsonElement root, string userPlatformUsername)
    {
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var branchName  = root.TryGetProperty("BranchName",  out var bnProp)  ? bnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        return $"🏬 گزارش شکایت از رفتار پرسنل خرید حضوری:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"شماره تماس: {phoneNumber}\n" +
               $"شعبه: {branchName}\n" +
               $"توضیحات: {description}" +
               userPlatformUsername +
               "\n #instorecomplaint";
    }

    /// <summary>Builds the admin notification for a StoreHoursQuery feedback.</summary>
    public static string BuildStoreHoursQueryLog(JsonElement root, string userPlatformUsername)
    {
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)   ? fnProp.GetString()   : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        return $"🕒 درخواست پرسش ساعت کاری تعطیلات:\n" +
               $"نام و نام خانوادگی: {fullName}\n" +
               $"توضیحات: {description}" +
               userPlatformUsername +
               "\n #storehoursquery";
    }
}
