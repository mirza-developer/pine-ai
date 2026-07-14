using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Shared.Services;
using PineAI.Bots.Shared.Tools;
using PineAI.Persistence.Services;
using PineAI.Shared;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PineAI.Bots.Telegram.Services;

/// <summary>
/// Handles incoming Telegram bot updates by acting as a transparent bypass between
/// the user and an AI chat agent.
///
/// Flow:
///  1. <c>/start</c> — clears any existing session, greets the user via AI.
///  2. Any other message — forwarded to the AI agent using the user's persisted session.
///  3. If the AI response contains <c>&lt;&lt;ORDER_CODE … &gt;&gt;</c> blocks, the order
///     code is looked up in the database and the result is appended to the reply.
///  4. If the AI response contains <c>&lt;&lt;FEEDBACK … &gt;&gt;</c> blocks, the feedback
///     is routed to the appropriate chat ID based on the feedback type.
/// </summary>
public class BotUpdateHandler(
        ITelegramBotClient botClient,
        PineAIDbContext dbContext,
        IChatAgentService agentService,
        ChatSessionStore sessionStore,
        BotChatMessageQueue chatMessageQueue,
        PhotoMessageStore photoMessageStore,
        UserPenaltyStore penaltyStore,
        ILogger<BotUpdateHandler> logger,
        IConfiguration configuration) : IBotUpdateHandler
{
    private const string SupportWaitNotice = "\nلطفاً تا ۷۲ ساعت کاری آینده صبوری کنید. درخواست شما بررسی می‌شود. لطفاً دیگر پیام ندهید، پاسخ‌گویی بر اساس آخرین پیام‌ها انجام می‌شود.";

    private const string PenaltyAppliedMessage =
        "⛔ به دلیل رفتار نامناسب مکرر، دسترسی شما به مدت ۱۰ دقیقه محدود شد. " +
        "لطفاً پس از ۱۰ دقیقه مجدداً تلاش کنید.";

    private const string PenaltyLockedMessage =
        "⛔ دسترسی شما موقتاً محدود است. لطفاً ۱۰ دقیقه صبر کنید.";

    /// <summary>
    /// Maps each FEEDBACK type to the string fields that must be present, non-empty,
    /// and not a literal placeholder value (e.g. "{OrderCode}") before the admin
    /// notification is dispatched.
    /// </summary>
    private static readonly Dictionary<string, string[]> RequiredFeedbackFields = new(StringComparer.Ordinal)
    {
        ["Satisfaction"]         = ["Description"],
        ["Complaint"]            = ["OrderCode", "PhoneNumber", "Date", "Description", "FullName"],
        ["DefectiveProduct"]     = ["OrderCode", "PhoneNumber", "FullName", "Description"],
        ["PhotoMismatch"]        = ["OrderCode", "PhoneNumber", "FullName", "Description"],
        ["ReturnedPackage"]      = ["OrderCode", "PhoneNumber", "FullName", "TrackingCode"],
        ["Wholesale"]            = ["PhoneNumber", "FullName", "Description"],
        ["NoOrderCode"]          = ["FullName", "PhoneNumber", "OrderAmount", "PaymentDate"],
        ["FailedPayment"]        = ["PhoneNumber", "FullName", "OrderAmount", "PaymentDate", "Description"],
        ["DelayedDelivery"]      = ["OrderCode", "PhoneNumber", "FullName"],
        ["WrongSize"]            = ["OrderCode", "PhoneNumber", "FullName", "Description"],
        ["UnknownQuery"]         = ["Description"],
        ["InStoreBillingError"]  = ["PhoneNumber", "FullName", "BranchName", "Description"],
        ["InStoreComplaint"]     = ["PhoneNumber", "FullName", "BranchName", "Description"],
        ["StoreHoursQuery"]      = ["Description"],
    };

    private readonly List<long> internalChatIds = new()
    {
        // Ananas Support Groups (Telegram chat IDs — configure per-deployment)
        6318588996, 5715522360, 6215427121, 6137308408,
        5518881690, 5000226193, 5225037607, 6178785306,
        5477856928, 5172013155, 5249048339,
        // Akhlaghi Group
        5372010785, 5535142626, 6188981039, 4413431598,
        5988414706, 6020396255, 6282035661,
        6309128770, 6108224018, 5437659346, 4427614753,
        5286810467, 5135010906
    };

    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        var message = update.Message;
        if (message is null)
            return;

        bool hasPhoto = message.Photo is { Length: > 0 };
        bool hasText = !string.IsNullOrWhiteSpace(message.Text);
        bool hasCaption = !string.IsNullOrWhiteSpace(message.Caption);

        if (!hasText && !hasPhoto && !hasCaption)
            return;

        var chatId = message.Chat.Id;

        if (internalChatIds.Contains(chatId))
            return;

        var username = message.From?.Username;

        if (string.IsNullOrEmpty(username))
        {
            await botClient.SendMessage(chatId, $"""
                همراه عزیز {configuration["Business:NameFa"]}
                نام کاربری (یوزرنیم) تلگرام شما در دسترس نیست
                جهت امکان پذیر شدن ارتباط با شما
                لطفا نام کاربری (یوزرنیم) خود را ست کنید
                """, cancellationToken: ct);
            return;
        }

        var blockedUsernames = configuration.GetSection("BlockedUsernames").Get<string[]>() ?? [];
        if (blockedUsernames.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Blocked username @{Username} — message suppressed", username);
            return;
        }

        logger.LogInformation("Update {UpdateId}: chat={ChatId} hasPhoto={HasPhoto}", update.Id, chatId, hasPhoto);

        if (hasPhoto)
        {
            // Use the highest-resolution variant (last element in the array)
            var bestPhoto = message.Photo![^1];
            photoMessageStore.StorePhoto(chatId, message.MessageId);
            logger.LogInformation("Stored photo message_id={MessageId} for chat={ChatId} (file_id={FileId})",
                message.MessageId, chatId, bestPhoto.FileId);
        }

        if (hasPhoto && !hasText && !hasCaption)
            return;

        string text;
        if (hasText)
            text = message.Text!.Trim();
        else
            text = message.Caption!.Trim();

        var pendingPhotoCount = photoMessageStore.PeekPhotos(chatId).Count;
        if (pendingPhotoCount > 0)
            text += $"\n[سیستم: کاربر {pendingPhotoCount} تصویر ارسال کرده است]";

        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, chatId, text, IsFromBot: false, DateTime.UtcNow));

        if (penaltyStore.IsUnderPenalty(chatId))
        {
            logger.LogInformation("Chat {ChatId} is under penalty — message suppressed", chatId);
            await botClient.SendMessage(chatId, PenaltyLockedMessage, cancellationToken: ct);
            return;
        }

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
            sessionStore.RemoveSession(chatId);

        var existingSession = sessionStore.GetSession(chatId);
        var response = await agentService.SendWithSessionAsync(existingSession, text);

        var textAfterPenalty = ResponseBlockTools.StripPenaltyBlocks(response.ResponseText, out var penaltyText);

        if (!string.IsNullOrEmpty(penaltyText))
        {
            sessionStore.RemoveSession(chatId);
            penaltyStore.ApplyPenalty(chatId);
            logger.LogWarning("Penalty applied to chat {ChatId}. Reason: {Reason}", chatId, penaltyText);
            await SendAndEnqueueBotReplyAsync(chatId, username, PenaltyAppliedMessage, ct);
            return;
        }

        sessionStore.SetSession(chatId, response.SerializedSession);

        var orderCodes = new List<string>();
        var visibleText = ResponseBlockTools.StripOrderCodeBlocks(textAfterPenalty, orderCodes);
        visibleText = ResponseBlockTools.StripFeedbackBlocks(visibleText, out var feedbackJson);
        visibleText = ResponseBlockTools.StripVerificationBlocks(visibleText, out var aiVerificationText);
        ValidateAiVerificationText(aiVerificationText);

        if (orderCodes.Count > 0)
        {
            var statusLines = new List<string>();
            foreach (var orderCode in orderCodes)
            {
                var order = await dbContext.CustomerOrder
                    .Include(o => o.OrderStatus)
                    .FirstOrDefaultAsync(o => o.OrderCode == orderCode, ct);

                if (order != null)
                {
                    statusLines.Add(
                        $"📦 سفارش «{order.OrderCode}»:\n" +
                        $"وضعیت: {order.OrderStatus.Title}\n" +
                        (!string.IsNullOrEmpty(order.PostalTrackingCode) ? $"کد مرسوله پستی: {order.PostalTrackingCode}\n" : "") +
                        $" کد ۲۴ رقمیو بزن تو سایت پست https://tracking.post.ir/ از وضعیت بسته باخبر شو");
                }
                else
                {
                    statusLines.Add($"❌ سفارشی با کد «{orderCode}» یافت نشد.");
                }
            }

            var statusBlock = string.Join("\n\n", statusLines);

            if (!string.IsNullOrWhiteSpace(visibleText))
                visibleText = visibleText + "\n\n" + statusBlock;
            else
                visibleText = statusBlock;

            if (!string.IsNullOrWhiteSpace(visibleText))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);

            if (!string.IsNullOrEmpty(feedbackJson))
                await TryDispatchFeedbackAsync(feedbackJson, visibleText: null, chatId, username, ct);
        }
        else if (!string.IsNullOrEmpty(feedbackJson))
        {
            await TryDispatchFeedbackAsync(feedbackJson, visibleText: visibleText, chatId, username, ct);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(visibleText))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
        }
    }

    private async Task SendAndEnqueueBotReplyAsync(long userChatId, string username, string text, CancellationToken ct)
    {
        await botClient.SendMessage(userChatId, text, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, text, IsFromBot: true, DateTime.UtcNow));
    }

    private async Task<bool> TryDispatchFeedbackAsync(
        string feedbackJson, string? visibleText, long chatId, string username, CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(feedbackJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Feedback JSON produced by AI is malformed — skipping admin notification");
            if (!string.IsNullOrWhiteSpace(visibleText))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type", out var typeProp))
            {
                logger.LogWarning("Feedback JSON missing 'Type' field — skipping admin notification");
                if (!string.IsNullOrWhiteSpace(visibleText))
                    await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
                return false;
            }

            var feedbackType = typeProp.GetString() ?? string.Empty;

            if (!ValidateFeedbackJson(feedbackType, root))
            {
                if (!string.IsNullOrWhiteSpace(visibleText))
                    await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
                return false;
            }

            await HandleFeedbackAsync(chatId, root, username, ct);
            return true;
        }
    }

    private async Task HandleFeedbackAsync(long userChatId, JsonElement root, string username, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(username))
        {
            var noUsernameMsg = $"دوست عزیز {configuration["Business:NameFa"]}، لطفاً نام کاربری خود را در تلگرام تنظیم کنید و در دسترس قرار دهید تا بتوانیم به شما پاسخ دهیم.";
            await botClient.SendMessage(userChatId, noUsernameMsg, cancellationToken: ct);
            chatMessageQueue.TryEnqueue(new BotChatMessageEntry(userChatId.ToString(), userChatId, noUsernameMsg, IsFromBot: true, DateTime.UtcNow));
            return;
        }

        var feedbackType = root.TryGetProperty("Type", out var typeProp)
            ? typeProp.GetString() ?? string.Empty
            : string.Empty;

        if (!root.TryGetProperty("TargetChatId", out var chatIdProperty) ||
            !chatIdProperty.TryGetInt64(out long targetChatId))
        {
            logger.LogWarning("Feedback JSON missing or invalid 'TargetChatId' for type: {FeedbackType}", feedbackType);
            return;
        }

        if (targetChatId == 0)
        {
            logger.LogWarning("Chat ID not configured for feedback type: {FeedbackType}", feedbackType);
            const string unconfiguredMsg = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
            await botClient.SendMessage(userChatId, unconfiguredMsg, cancellationToken: ct);
            chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, unconfiguredMsg, IsFromBot: true, DateTime.UtcNow));
            return;
        }

        string userTelegramUsername = $"\n کاربری: @{username}";

        switch (feedbackType)
        {
            case "Satisfaction":
                await HandleSatisfactionAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "Complaint":
                await HandleComplaintAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "DefectiveProduct":
                await HandleDefectiveProductAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "PhotoMismatch":
                await HandlePhotoMismatchAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "ReturnedPackage":
                await HandleReturnedPackageAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "Wholesale":
                await HandleWholesaleAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "NoOrderCode":
                await HandleNoOrderCodeAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "FailedPayment":
                await HandleFailedPaymentAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "DelayedDelivery":
                await HandleDelayedDeliveryAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "WrongSize":
                await HandleWrongSizeAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "UnknownQuery":
                await HandleUnknownQueryAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "InStoreBillingError":
                await HandleInStoreBillingErrorAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "InStoreComplaint":
                await HandleInStoreComplaintAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            case "StoreHoursQuery":
                await HandleStoreHoursQueryAsync(userChatId, targetChatId, root, userTelegramUsername, username, ct);
                break;
            default:
                logger.LogWarning("Unhandled feedback type: {FeedbackType}", feedbackType);
                break;
        }
    }

    private async Task HandleSatisfactionAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSatisfactionSuccess = "مبارکتون باشه. خوشحالیم تونستیم پاسخ اعتمادتون رو بدیم. به امید دیدار مجدد در خرید های بعدی";
        await botClient.SendMessage(userChatId, messageSatisfactionSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSatisfactionSuccess, IsFromBot: true, DateTime.UtcNow));

        string orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() ?? "نامشخص" : "نامشخص";
        string description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() ?? "" : "";

        string satisfactionLog = $"🌸 پیام رضایت جدید ثبت شد:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"توضیحات: {description}" +
            userTelegramUsername;

        await botClient.SendMessage(targetChatId, satisfactionLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageComplaintSuccess = "📣 اطلاعات شما ثبت شد:\nپشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageComplaintSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageComplaintSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var date        = root.TryGetProperty("Date",        out var dtProp)  ? dtProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        string complaintLog = $"📣 شکایت/درخواست پیگیری جدید ثبت شد:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"تاریخ: {date}\n" +
            $"توضیحات: {description}\n";

        var order = await dbContext.CustomerOrder
               .Include(o => o.OrderStatus)
               .FirstOrDefaultAsync(o => o.OrderCode == orderCode, CancellationToken.None);

        if (order is not null)
        {
            complaintLog += "\n" +
                $"📦 سفارش «{order.OrderCode}»:\n" +
                $"وضعیت: {order.OrderStatus.Title}\n" +
                $"آخرین به‌روزرسانی: {PersianCalendarTools.GregorianToPersian(order.UpdatedAt)} {order.UpdatedAt:HH:mm}";
        }
        else
        {
            complaintLog += "\n" + $"❌ سفارشی با کد «{orderCode}» یافت نشد.";
        }

        complaintLog += userTelegramUsername + "\n #case ";
        await botClient.SendMessage(targetChatId, complaintLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleDefectiveProductAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";
        bool hasPhoto   = GetHasPhoto(root);

        string defectiveLog = $"⚠️ گزارش محصول معیوب/خراب:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}\n" +
            $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}\n";

        var orderInfo = await LookupOrderAsync(orderCode, ct);
        defectiveLog += orderInfo + userTelegramUsername + "\n #defective";

        await botClient.SendMessage(targetChatId, defectiveLog, cancellationToken: CancellationToken.None);

        if (hasPhoto)
        {
            var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
            if (storedMessageIds.Count > 0)
            {
                foreach (var msgId in storedMessageIds)
                    await botClient.ForwardMessage(targetChatId, userChatId, (int)msgId, cancellationToken: CancellationToken.None);
            }
            else
            {
                logger.LogWarning("HasPhoto=true for DefectiveProduct but no stored photo found for chat {ChatId}", userChatId);
            }
        }
    }

    private async Task HandlePhotoMismatchAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";
        bool hasPhoto   = GetHasPhoto(root);

        string mismatchLog = $"📸 گزارش مغایرت عکس و محصول:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}\n" +
            $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}\n";

        var orderInfo = await LookupOrderAsync(orderCode, ct);
        mismatchLog += orderInfo + userTelegramUsername + "\n #mismatch";

        await botClient.SendMessage(targetChatId, mismatchLog, cancellationToken: CancellationToken.None);

        if (hasPhoto)
        {
            var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
            if (storedMessageIds.Count > 0)
            {
                foreach (var msgId in storedMessageIds)
                    await botClient.ForwardMessage(targetChatId, userChatId, (int)msgId, cancellationToken: CancellationToken.None);
            }
            else
            {
                logger.LogWarning("HasPhoto=true for PhotoMismatch but no stored photo found for chat {ChatId}", userChatId);
            }
        }
    }

    private async Task HandleReturnedPackageAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var trackingCode = root.TryGetProperty("TrackingCode", out var tcProp)  ? tcProp.GetString()  ?? "نامشخص" : "نامشخص";

        string returnedLog = $"📦 گزارش بسته برگشت خورده:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"کد رهگیری پست: {trackingCode}\n";

        var orderInfo = await LookupOrderAsync(orderCode, ct);
        returnedLog += orderInfo + userTelegramUsername + "\n #returned";
        await botClient.SendMessage(targetChatId, returnedLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleWholesaleAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ درخواست عمده شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        string wholesaleLog = $"📦 درخواست سفارش عمده جدید:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}" +
            userTelegramUsername + "\n #wholesale";

        await botClient.SendMessage(targetChatId, wholesaleLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleNoOrderCodeAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از بررسی در تلگرام به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var orderAmount = root.TryGetProperty("OrderAmount", out var oaProp)  ? oaProp.GetString()  ?? "نامشخص" : "نامشخص";
        var paymentDate = root.TryGetProperty("PaymentDate", out var pdProp)  ? pdProp.GetString()  ?? "نامشخص" : "نامشخص";

        string noCodeLog = $"🔍 درخواست یافتن کد سفارش:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"مبلغ سفارش: {orderAmount}\n" +
            $"تاریخ پرداخت: {paymentDate}" +
            userTelegramUsername + "\n #nocode";

        await botClient.SendMessage(targetChatId, noCodeLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleFailedPaymentAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از بررسی در تلگرام به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var orderAmount = root.TryGetProperty("OrderAmount", out var oaProp)  ? oaProp.GetString()  ?? "نامشخص" : "نامشخص";
        var paymentDate = root.TryGetProperty("PaymentDate", out var pdProp)  ? pdProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        string failedPaymentLog = $"💳 گزارش پرداخت ناموفق:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"مبلغ: {orderAmount}\n" +
            $"تاریخ پرداخت: {paymentDate}\n" +
            $"توضیحات: {description}" +
            userTelegramUsername + "\n #failedpayment";

        await botClient.SendMessage(targetChatId, failedPaymentLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleDelayedDeliveryAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از پیگیری در تلگرام به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";

        string delayedLog = $"⏰ گزارش تاخیر در تحویل (بالای ۸ روز کاری):\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n";

        var orderInfo = await LookupOrderAsync(orderCode, ct);
        delayedLog += orderInfo + userTelegramUsername + "\n #delayed";
        await botClient.SendMessage(targetChatId, delayedLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleWrongSizeAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode   = root.TryGetProperty("OrderCode",   out var ocProp)  ? ocProp.GetString()  ?? "نامشخص" : "نامشخص";
        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        string wrongSizeLog = $"📏 گزارش سایز نامناسب:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}\n";

        var orderInfo = await LookupOrderAsync(orderCode, ct);
        wrongSizeLog += orderInfo + userTelegramUsername + "\n #wrongsize";
        await botClient.SendMessage(targetChatId, wrongSizeLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleUnknownQueryAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ پیام شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        string unknownLog = $"❓ درخواست نامشخص:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"توضیحات: {description}" +
            userTelegramUsername + "\n #unknown";

        await botClient.SendMessage(targetChatId, unknownLog, cancellationToken: CancellationToken.None);
    }

    private async Task HandleInStoreBillingErrorAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در تلگرام در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var branchName  = root.TryGetProperty("BranchName",  out var bnProp)  ? bnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";
        bool hasPhoto   = GetHasPhoto(root);

        string logText = $"🧾 گزارش خطای فاکتور خرید حضوری:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"شعبه: {branchName}\n" +
            $"توضیحات: {description}\n" +
            $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}" +
            userTelegramUsername + "\n #instorebillingerror";

        await botClient.SendMessage(targetChatId, logText, cancellationToken: CancellationToken.None);

        if (hasPhoto)
        {
            var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
            if (storedMessageIds.Count > 0)
            {
                foreach (var msgId in storedMessageIds)
                    await botClient.ForwardMessage(targetChatId, userChatId, (int)msgId, cancellationToken: CancellationToken.None);
            }
            else
            {
                logger.LogWarning("HasPhoto=true for InStoreBillingError but no stored photo found for chat {ChatId}", userChatId);
            }
        }
    }

    private async Task HandleInStoreComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ پیام شما به پشتیبان‌های ما ارسال شد و تا ۷۲ ساعت کاری پشتیبان به شما پاسخ میده." + SupportWaitNotice;
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber = root.TryGetProperty("PhoneNumber", out var phProp)  ? phProp.GetString()  ?? "نامشخص" : "نامشخص";
        var fullName    = root.TryGetProperty("FullName",    out var fnProp)  ? fnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var branchName  = root.TryGetProperty("BranchName",  out var bnProp)  ? bnProp.GetString()  ?? "نامشخص" : "نامشخص";
        var description = root.TryGetProperty("Description", out var dscProp) ? dscProp.GetString() ?? "نامشخص" : "نامشخص";

        string logText = $"🏬 گزارش شکایت از رفتار پرسنل خرید حضوری:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"شعبه: {branchName}\n" +
            $"توضیحات: {description}" +
            userTelegramUsername + "\n #instorecomplaint";

        await botClient.SendMessage(targetChatId, logText, cancellationToken: CancellationToken.None);
    }

    private async Task HandleStoreHoursQueryAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ پیام شما به پشتیبان‌های ما ارسال شد و به زودی ساعت کاری اون تاریخ رو بهتون اطلاع می‌دیم.";
        await botClient.SendMessage(userChatId, messageSuccess, cancellationToken: ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var fullName    = root.TryGetProperty("FullName",    out var fnProp)   ? fnProp.GetString()   : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        string logText = $"🕒 درخواست پرسش ساعت کاری تعطیلات:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"توضیحات: {description}" +
            userTelegramUsername + "\n #storehoursquery";

        await botClient.SendMessage(targetChatId, logText, cancellationToken: CancellationToken.None);
    }

    private async Task<string> LookupOrderAsync(string? orderCode, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(orderCode))
            return "";

        orderCode = ResponseBlockTools.NormalizeDigits(orderCode);

        var order = await dbContext.CustomerOrder
            .Include(o => o.OrderStatus)
            .FirstOrDefaultAsync(o => o.OrderCode == orderCode, CancellationToken.None);

        if (order is not null)
        {
            return "\n" +
                 $"📦 سفارش «{order.OrderCode}»:\n" +
                         $"وضعیت: {order.OrderStatus.Title}\n" +
                         (!string.IsNullOrEmpty(order.PostalTrackingCode) ? $"کد مرسوله پستی: {order.PostalTrackingCode}\n" : "") +
                         $" کد ۲۴ رقمیو بزن تو سایت پست https://tracking.post.ir/ از وضعیت بسته باخبر شو";
        }
        else
        {
            return "\n" + $"❌ سفارشی با کد «{orderCode}» یافت نشد.";
        }
    }

    private static bool IsFieldMissing(JsonElement root, string fieldName)
    {
        if (!root.TryGetProperty(fieldName, out var element))
            return true;

        if (element.ValueKind == JsonValueKind.Null)
            return true;

        var value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '{' && trimmed[^1] == '}')
        {
            var inner = trimmed[1..^1];
            if (inner.Length > 0 && inner.All(char.IsLetter))
                return true;
        }

        return false;
    }

    private bool ValidateFeedbackJson(string feedbackType, JsonElement root)
    {
        if (!RequiredFeedbackFields.TryGetValue(feedbackType, out var requiredFields))
        {
            logger.LogWarning("Feedback type '{FeedbackType}' has no required-field definition — dispatching without validation", feedbackType);
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

    private static bool GetHasPhoto(JsonElement root)
    {
        if (!root.TryGetProperty("HasPhoto", out var el))
            return false;

        return el.ValueKind switch
        {
            JsonValueKind.True  => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b,
            _ => false
        };
    }

    private static readonly char[] ArabicOnlyCharacters =
    {
        '\u064A', '\u0643', '\u0629', '\u0649', '\u0622', '\u0623', '\u0625',
        '\u0624', '\u0671', '\u064B', '\u064C', '\u064D', '\u064E', '\u064F',
        '\u0650', '\u0651', '\u0652',
    };

    private void ValidateAiVerificationText(string? verificationText)
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
