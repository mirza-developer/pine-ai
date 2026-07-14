using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Bale.Models;
using PineAI.Bots.Shared.Services;
using PineAI.Bots.Shared.Tools;
using PineAI.Persistence.Services;
using PineAI.Shared;

namespace PineAI.Bots.Bale.Services;

/// <summary>
/// Handles incoming Bale bot updates by acting as a transparent bypass between
/// the user and an AI chat agent powered by the Microsoft Agents SDK.
///
/// Flow:
///  1. <c>/start</c> — clears any existing session, greets the user via AI.
///  2. Any other message — forwarded to the AI agent using the user's persisted session.
///  3. If the AI response contains <c>&lt;&lt;ORDER_CODE … &gt;&gt;</c> blocks, the order
///     code is looked up in the database and the result is appended to the reply.
///  4. If the AI response contains <c>&lt;&lt;FEEDBACK … &gt;&gt;</c> blocks, the feedback
///     is routed to the appropriate chat ID based on the feedback type (10 different types supported).
/// </summary>
public class BotUpdateHandler(BaleBotClient botClient,
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
    /// notification is dispatched. If any required field fails, the handler falls back
    /// to sending the AI's visible text to the user and skips the admin notification.
    /// </summary>
    private static readonly Dictionary<string, string[]> RequiredFeedbackFields = new(StringComparer.Ordinal)
    {
        // OrderCode intentionally omitted — instructions say do NOT ask if user didn't mention it
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
        // FullName omitted — user may be anonymous; Description is the minimum useful signal
        ["UnknownQuery"]         = ["Description"],
        ["InStoreBillingError"]  = ["PhoneNumber", "FullName", "BranchName", "Description"],
        ["InStoreComplaint"]     = ["PhoneNumber", "FullName", "BranchName", "Description"],
        ["StoreHoursQuery"]      = ["Description"],
    };

    private readonly List<long> chatIds = new()
    {
        // Ananas Support Groups
        6318588996,5715522360,6215427121,6137308408,
        5518881690,5000226193,5225037607,6178785306,
        5477856928,5172013155,5249048339,
        //Akhlaghi Group
        5372010785,5535142626,6188981039,4413431598,
        5988414706,6020396255,6282035661,
        6309128770,6108224018,5437659346,4427614753,
        5286810467,5135010906
    };

    public async Task HandleAsync(BaleUpdate update, CancellationToken ct)
    {
        var message = update.Message;
        if (message is null)
            return;

        bool hasPhoto = message.Photo is { Length: > 0 };
        bool hasText = !string.IsNullOrWhiteSpace(message.Text);
        bool hasCaption = !string.IsNullOrWhiteSpace(message.Caption);

        // Ignore messages with no text, caption, or photo content
        if (!hasText && !hasPhoto && !hasCaption)
            return;

        var chatId = message.Chat.Id;

        if (chatIds.Contains(message.Chat.Id))
        {
            return;
        }

        var username = message.From?.Username;

        if (string.IsNullOrEmpty(username))
        {
            await botClient.SendMessageAsync(chatId, $"""
                همراه عزیز {configuration["Business:NameFa"]}
                نام کاربری (آیدی) بله شما در دسترس نیست
                جهت امکان پذیر شدن ارتباط با شما
                لطفا نام کاربری (آیدی) خود را ست کنید
                یا اگر ست کرده اید، در دسترسی عمومی قرار دهید
                """, ct);

            return;
        }

        // Silently drop messages from explicitly blocked usernames.
        // No reply is sent, nothing is persisted, and the AI is never called.
        var blockedUsernames = configuration.GetSection("BlockedUsernames").Get<string[]>() ?? [];
        if (blockedUsernames.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Blocked username @{Username} — message suppressed", username);
            return;
        }

        logger.LogInformation("Update {UpdateId}: chat={ChatId} hasPhoto={HasPhoto}", update.UpdateId, chatId, hasPhoto);

        // When the user sends a photo, store it for later forwarding
        if (hasPhoto)
        {
            // Use the highest-resolution variant (last element in the array)
            var bestPhoto = message.Photo![^1];
            photoMessageStore.StorePhoto(chatId, message.MessageId);
            logger.LogInformation("Stored photo message_id={MessageId} for chat={ChatId} (file_id={FileId})",
                message.MessageId, chatId, bestPhoto.FileId);
        }

        // A photo-only message (no text, no caption) is stored above and nothing else.
        // We must NOT forward it to the AI — the user may be sending several photos in a row
        // and each would independently trigger an AI response, causing TakePhotos() to drain
        // the store prematurely so later photos are never forwarded to support.
        if (hasPhoto && !hasText && !hasCaption)
            return;

        // Build the text that will be forwarded to the AI.
        // For a photo+caption message the photo is already stored above, so we pass only
        // the raw caption — no wrapper prefix — to avoid confusing the AI into thinking
        // it needs to handle a fresh photo separately from the ones already queued.
        string text;
        if (hasText)
            text = message.Text!.Trim();
        else
            text = message.Caption!.Trim();

        // If the user has queued photos that the AI does not yet know about, append a
        // system note so the AI sets HasPhoto:true in the FEEDBACK block.
        var pendingPhotoCount = photoMessageStore.PeekPhotos(chatId).Count;
        if (pendingPhotoCount > 0)
            text += $"\n[سیستم: کاربر {pendingPhotoCount} تصویر ارسال کرده است]";

        // Enqueue user message for background persistence (fire-and-forget)
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, chatId, text, IsFromBot: false, DateTime.UtcNow));

        // Penalty gate: if the user is locked out, reject without calling the AI.
        // The gate fires AFTER persistence so penalised messages are still auditable.
        // It also fires BEFORE the /start session-reset so users cannot escape the
        // lock by sending /start.
        if (penaltyStore.IsUnderPenalty(chatId))
        {
            logger.LogInformation("Chat {ChatId} is under penalty — message suppressed", chatId);
            await botClient.SendMessageAsync(chatId, PenaltyLockedMessage, ct);
            return;
        }

        // /start → reset session so user always gets a fresh greeting
        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            sessionStore.RemoveSession(chatId);
        }

        var existingSession = sessionStore.GetSession(chatId);
        var response = await agentService.SendWithSessionAsync(existingSession, text);

        // Strip <<PENALTY>> FIRST and BEFORE saving the session.
        // If the raw block is saved into session history the AI sees it on the next
        // turn, copies it verbatim, and it leaks to the user even after stripping.
        var textAfterPenalty = ResponseBlockTools.StripPenaltyBlocks(response.ResponseText, out var penaltyText);

        if (!string.IsNullOrEmpty(penaltyText))
        {
            // Clear session so the user starts fresh after the lock expires.
            sessionStore.RemoveSession(chatId);
            penaltyStore.ApplyPenalty(chatId);
            logger.LogWarning("Penalty applied to chat {ChatId}. Reason: {Reason}", chatId, penaltyText);
            await SendAndEnqueueBotReplyAsync(chatId, username, PenaltyAppliedMessage, ct);
            return;
        }

        // Session is saved only after confirming no penalty block is present.
        sessionStore.SetSession(chatId, response.SerializedSession);

        // Continue normal block processing on the penalty-stripped text
        var orderCodes = new List<string>();
        var visibleOrderCodes = ResponseBlockTools.StripOrderCodeBlocks(textAfterPenalty, orderCodes);
        visibleOrderCodes = ResponseBlockTools.StripFeedbackBlocks(visibleOrderCodes, out var feedbackJson);

        // Strip any <<VERIFICATION>> block the AI emitted. The block carries the AI's
        // proposed "data was sent to support" sentence. We never forward that sentence
        // to the user — each successful HandleXxxAsync method sends its own authoritative
        // confirmation. Discarding the AI's verification here is what guarantees the user
        // is never told their data was delivered when, in fact, no admin dispatch occurred
        // (malformed JSON, missing required fields, missing TargetChatId, …).
        visibleOrderCodes = ResponseBlockTools.StripVerificationBlocks(visibleOrderCodes, out var aiVerificationText);
        ValidateAiVerificationText(aiVerificationText);

        // If the AI signalled one or more order codes, resolve them from the DB
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

            // Append status info after the AI's visible text
            if (!string.IsNullOrWhiteSpace(visibleOrderCodes))
                visibleOrderCodes = visibleOrderCodes + "\n\n" + statusBlock;
            else
                visibleOrderCodes = statusBlock;

            if (!string.IsNullOrWhiteSpace(visibleOrderCodes))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleOrderCodes, ct);

            // A FEEDBACK block may accompany the ORDER_CODE block (e.g. DelayedDelivery
            // where the AI checks the order and escalates in the same turn). Process it too.
            // If dispatch fails here the order-status reply was already sent, so no fallback needed.
            if (!string.IsNullOrEmpty(feedbackJson))
                await TryDispatchFeedbackAsync(feedbackJson, visibleText: null, chatId, username, ct);
        }
        else if (!string.IsNullOrEmpty(feedbackJson))
        {
            // If the FEEDBACK is invalid/incomplete, fall back to sending the AI's visible text
            // (which should contain the AI asking the user for the missing field). The AI's
            // proposed delivery confirmation has already been removed by StripVerificationBlocks
            // above, so no false "data sent to support" claim can leak through this path.
            await TryDispatchFeedbackAsync(feedbackJson, visibleText: visibleOrderCodes, chatId, username, ct);
        }
        else
        {
            // No FEEDBACK block at all — the AI is just chatting with the user.
            if (!string.IsNullOrWhiteSpace(visibleOrderCodes))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleOrderCodes, ct);
        }
    }

    /// <summary>Sends a bot reply to the user and enqueues it for background persistence.</summary>
    private async Task SendAndEnqueueBotReplyAsync(long userChatId, string username, string text, CancellationToken ct)
    {
        await botClient.SendMessageAsync(userChatId, text, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, text, IsFromBot: true, DateTime.UtcNow));
    }

    /// <summary>
    /// Parses <paramref name="feedbackJson"/>, validates that all required fields for the
    /// feedback type are present and non-placeholder, then dispatches to the admin-group
    /// handler. Returns <c>true</c> when dispatching succeeded.
    ///
    /// When any check fails the method:
    /// <list type="bullet">
    ///   <item>logs a structured warning,</item>
    ///   <item>sends <paramref name="visibleText"/> to the user (when not null/empty) so the
    ///         AI's follow-up question — asking for the missing field — reaches the user, and</item>
    ///   <item>returns <c>false</c> without contacting any admin group.</item>
    /// </list>
    /// </summary>
    private async Task<bool> TryDispatchFeedbackAsync(
        string feedbackJson, string? visibleText, long chatId, string username, CancellationToken ct)
    {
        // The caller has already stripped any <<VERIFICATION>> block from visibleText,
        // so the only sentences that can survive in this fallback path are the AI's
        // ask/answer text (e.g. "please send your order code"). No further sanitisation
        // of free-form prose is required: false delivery confirmations can only enter
        // the visible stream through a VERIFICATION block, and those are gone.

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
                // ValidateFeedbackJson already logged per-field warnings.
                // Send the AI's visible text — it should contain the question asking for the missing field.
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
            var noUsernameMsg = $"دوست عزیز {configuration["Business:NameFa"]}، لطفاً نام کاربری خود را در بله تنظیم کنید و در دسترس قرار دهید تا بتوانیم به شما پاسخ دهیم.";
            await botClient.SendMessageAsync(userChatId, noUsernameMsg, ct);
            chatMessageQueue.TryEnqueue(new BotChatMessageEntry(userChatId.ToString(), userChatId, noUsernameMsg, IsFromBot: true, DateTime.UtcNow));
            return;
        }

        // Type was already validated upstream; read it again for routing.
        var feedbackType = root.TryGetProperty("Type", out var typeProp)
            ? typeProp.GetString() ?? string.Empty
            : string.Empty;

        // Read target chat ID directly from the JSON generated by the AI
        if (!root.TryGetProperty("TargetChatId", out var chatIdProperty) ||
            !chatIdProperty.TryGetInt64(out long targetChatId))
        {
            logger.LogWarning("Feedback JSON missing or invalid 'TargetChatId' for type: {FeedbackType}", feedbackType);
            return;
        }

        // Skip routing if chat ID is not configured (0 means placeholder)
        if (targetChatId == 0)
        {
            logger.LogWarning("Chat ID not configured for feedback type: {FeedbackType}", feedbackType);
            const string unconfiguredMsg = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
            await botClient.SendMessageAsync(userChatId, unconfiguredMsg, ct);
            chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, unconfiguredMsg, IsFromBot: true, DateTime.UtcNow));
            return;
        }

        string userBaleUsername = $"\n کاربری: @{username}";

        // Route to appropriate handler based on feedback type
        switch (feedbackType)
        {
            case "Satisfaction":
                await HandleSatisfactionAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "Complaint":
                await HandleComplaintAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "DefectiveProduct":
                await HandleDefectiveProductAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "PhotoMismatch":
                await HandlePhotoMismatchAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "ReturnedPackage":
                await HandleReturnedPackageAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "Wholesale":
                await HandleWholesaleAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "NoOrderCode":
                await HandleNoOrderCodeAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "FailedPayment":
                await HandleFailedPaymentAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "DelayedDelivery":
                await HandleDelayedDeliveryAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "WrongSize":
                await HandleWrongSizeAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "UnknownQuery":
                await HandleUnknownQueryAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "InStoreBillingError":
                await HandleInStoreBillingErrorAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "InStoreComplaint":
                await HandleInStoreComplaintAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "StoreHoursQuery":
                await HandleStoreHoursQueryAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            default:
                logger.LogWarning("Unhandled feedback type: {FeedbackType}", feedbackType);
                break;
        }
    }

    private async Task HandleSatisfactionAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSatisfactionSuccess = """
            مبارکتون باشه. خوشحالیم تونستیم پاسخ اعتمادتون رو بدیم. به امید دیدار مجدد در خرید های بعدی
            """;

        await botClient.SendMessageAsync(userChatId, messageSatisfactionSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSatisfactionSuccess, IsFromBot: true, DateTime.UtcNow));

        string orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : "نامشخص";
        string description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        string satisfactionLog = $"🌸 پیام رضایت جدید ثبت شد:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"توضیحات: {description}" +
            userBaleUsername;

        await botClient.SendMessageAsync(targetChatId, satisfactionLog, CancellationToken.None);
    }

    private async Task HandleComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageComplaintSuccess = "📣 اطلاعات شما ثبت شد:\nپشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageComplaintSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageComplaintSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var date         = root.TryGetProperty("Date",         out var dtProp)   ? dtProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";

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

        complaintLog += userBaleUsername + "\n #case ";

        await botClient.SendMessageAsync(targetChatId, complaintLog, CancellationToken.None);
    }

    private async Task HandleDefectiveProductAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";
        bool hasPhoto    = GetHasPhoto(root);

        string defectiveLog = $"⚠️ گزارش محصول معیوب/خراب:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}\n" +
            $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}\n";

        var order = await LookupOrderAsync(orderCode, ct);
        defectiveLog += order + userBaleUsername + "\n #defective";

        await botClient.SendMessageAsync(targetChatId, defectiveLog, CancellationToken.None);

        // Forward the user's photo(s) to the support chat when the AI confirmed one was received
        if (hasPhoto)
        {
            var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
            if (storedMessageIds.Count > 0)
            {
                foreach (var msgId in storedMessageIds)
                    await botClient.ForwardMessageAsync(targetChatId, userChatId, msgId, CancellationToken.None);
            }
            else
            {
                logger.LogWarning("HasPhoto=true for DefectiveProduct but no stored photo found for chat {ChatId}", userChatId);
            }
        }
    }

    private async Task HandlePhotoMismatchAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";
        bool hasPhoto    = GetHasPhoto(root);

        string mismatchLog = $"📸 گزارش مغایرت عکس و محصول:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}\n" +
            $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}\n";

        var order = await LookupOrderAsync(orderCode, ct);
        mismatchLog += order + userBaleUsername + "\n #mismatch";

        await botClient.SendMessageAsync(targetChatId, mismatchLog, CancellationToken.None);

        // Forward the user's photo(s) to the support chat when the AI confirmed one was received
        if (hasPhoto)
        {
            var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
            if (storedMessageIds.Count > 0)
            {
                foreach (var msgId in storedMessageIds)
                    await botClient.ForwardMessageAsync(targetChatId, userChatId, msgId, CancellationToken.None);
            }
            else
            {
                logger.LogWarning("HasPhoto=true for PhotoMismatch but no stored photo found for chat {ChatId}", userChatId);
            }
        }
    }

    private async Task HandleReturnedPackageAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var trackingCode = root.TryGetProperty("TrackingCode", out var tcProp)   ? tcProp.GetString()   ?? "نامشخص" : "نامشخص";

        string returnedLog = $"📦 گزارش بسته برگشت خورده:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"کد رهگیری پست: {trackingCode}\n";

        var order = await LookupOrderAsync(orderCode, ct);
        returnedLog += order + userBaleUsername + "\n #returned";

        await botClient.SendMessageAsync(targetChatId, returnedLog, CancellationToken.None);
    }

    private async Task HandleWholesaleAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ درخواست عمده شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";

        string wholesaleLog = $"📦 درخواست سفارش عمده جدید:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}" +
            userBaleUsername + "\n #wholesale";

        await botClient.SendMessageAsync(targetChatId, wholesaleLog, CancellationToken.None);
    }

    private async Task HandleNoOrderCodeAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از بررسی در بله به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var orderAmount  = root.TryGetProperty("OrderAmount",  out var oaProp)   ? oaProp.GetString()   ?? "نامشخص" : "نامشخص";
        var paymentDate  = root.TryGetProperty("PaymentDate",  out var pdProp)   ? pdProp.GetString()   ?? "نامشخص" : "نامشخص";

        string noCodeLog = $"🔍 درخواست یافتن کد سفارش:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"مبلغ سفارش: {orderAmount}\n" +
            $"تاریخ پرداخت: {paymentDate}" +
            userBaleUsername + "\n #nocode";

        await botClient.SendMessageAsync(targetChatId, noCodeLog, CancellationToken.None);
    }

    private async Task HandleFailedPaymentAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از بررسی در بله به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var orderAmount  = root.TryGetProperty("OrderAmount",  out var oaProp)   ? oaProp.GetString()   ?? "نامشخص" : "نامشخص";
        var paymentDate  = root.TryGetProperty("PaymentDate",  out var pdProp)   ? pdProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";

        string failedPaymentLog = $"💳 گزارش پرداخت ناموفق:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"مبلغ: {orderAmount}\n" +
            $"تاریخ پرداخت: {paymentDate}\n" +
            $"توضیحات: {description}" +
            userBaleUsername + "\n #failedpayment";

        await botClient.SendMessageAsync(targetChatId, failedPaymentLog, CancellationToken.None);
    }

    private async Task HandleDelayedDeliveryAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما پس از پیگیری در بله به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";

        string delayedLog = $"⏰ گزارش تاخیر در تحویل (بالای ۸ روز کاری):\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n";

        var order = await LookupOrderAsync(orderCode, ct);
        delayedLog += order + userBaleUsername + "\n #delayed";

        await botClient.SendMessageAsync(targetChatId, delayedLog, CancellationToken.None);
    }

    private async Task HandleWrongSizeAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var orderCode    = root.TryGetProperty("OrderCode",    out var ocProp)   ? ocProp.GetString()   ?? "نامشخص" : "نامشخص";
        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";

        string wrongSizeLog = $"📏 گزارش سایز نامناسب:\n" +
            $"کد سفارش: {orderCode}\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"توضیحات: {description}\n";

        var order = await LookupOrderAsync(orderCode, ct);
        wrongSizeLog += order + userBaleUsername + "\n #wrongsize";

        await botClient.SendMessageAsync(targetChatId, wrongSizeLog, CancellationToken.None);
    }

    private async Task HandleUnknownQueryAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ پیام شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var fullName = root.TryGetProperty("FullName", out var fnProp) ? fnProp.GetString() : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        string unknownLog = $"❓ درخواست نامشخص:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"توضیحات: {description}" +
            userBaleUsername + "\n #unknown";

        await botClient.SendMessageAsync(targetChatId, unknownLog, CancellationToken.None);
    }

    private async Task HandleInStoreBillingErrorAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ اطلاعات شما ثبت شد. پشتیبانی ما در بله در اسرع وقت به شما پیام می‌دهد." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var branchName   = root.TryGetProperty("BranchName",   out var bnProp)   ? bnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";
        bool hasPhoto    = GetHasPhoto(root);

        string logText = $"🧾 گزارش خطای فاکتور خرید حضوری:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"شعبه: {branchName}\n" +
            $"توضیحات: {description}\n" +
            $"عکس ارسال شده: {(hasPhoto ? "بله" : "خیر")}" +
            userBaleUsername + "\n #instorebillingerror";

        await botClient.SendMessageAsync(targetChatId, logText, CancellationToken.None);

        if (hasPhoto)
        {
            var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
            if (storedMessageIds.Count > 0)
            {
                foreach (var msgId in storedMessageIds)
                    await botClient.ForwardMessageAsync(targetChatId, userChatId, msgId, CancellationToken.None);
            }
            else
            {
                logger.LogWarning("HasPhoto=true for InStoreBillingError but no stored photo found for chat {ChatId}", userChatId);
            }
        }
    }

    private async Task HandleInStoreComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ پیام شما به پشتیبان‌های ما ارسال شد و تا ۷۲ ساعت کاری پشتیبان به شما پاسخ میده." + SupportWaitNotice;
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var phoneNumber  = root.TryGetProperty("PhoneNumber",  out var phProp)   ? phProp.GetString()   ?? "نامشخص" : "نامشخص";
        var fullName     = root.TryGetProperty("FullName",     out var fnProp)   ? fnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var branchName   = root.TryGetProperty("BranchName",   out var bnProp)   ? bnProp.GetString()   ?? "نامشخص" : "نامشخص";
        var description  = root.TryGetProperty("Description",  out var dscProp)  ? dscProp.GetString()  ?? "نامشخص" : "نامشخص";

        string logText = $"🏬 گزارش شکایت از رفتار پرسنل خرید حضوری:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"شماره تماس: {phoneNumber}\n" +
            $"شعبه: {branchName}\n" +
            $"توضیحات: {description}" +
            userBaleUsername + "\n #instorecomplaint";

        await botClient.SendMessageAsync(targetChatId, logText, CancellationToken.None);
    }

    private async Task HandleStoreHoursQueryAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        string messageSuccess = "✅ پیام شما به پشتیبان‌های ما ارسال شد و به زودی ساعت کاری اون تاریخ رو بهتون اطلاع می‌دیم.";
        await botClient.SendMessageAsync(userChatId, messageSuccess, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, messageSuccess, IsFromBot: true, DateTime.UtcNow));

        var fullName = root.TryGetProperty("FullName", out var fnProp) ? fnProp.GetString() : "نامشخص";
        var description = root.TryGetProperty("Description", out var descProp) ? descProp.GetString() : "";

        string logText = $"🕒 درخواست پرسش ساعت کاری تعطیلات:\n" +
            $"نام و نام خانوادگی: {fullName}\n" +
            $"توضیحات: {description}" +
            userBaleUsername + "\n #storehoursquery";

        await botClient.SendMessageAsync(targetChatId, logText, CancellationToken.None);
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

    /// <summary>
    /// Returns <c>true</c> when a field must be considered missing, meaning the admin
    /// notification must NOT be dispatched. A field is missing when it is:
    /// <list type="bullet">
    ///   <item>absent from the JSON object,</item>
    ///   <item>null, empty, or whitespace, or</item>
    ///   <item>still a literal template placeholder such as <c>{OrderCode}</c> — the AI
    ///         copied the template without substituting a real value.</item>
    /// </list>
    /// </summary>
    private static bool IsFieldMissing(JsonElement root, string fieldName)
    {
        if (!root.TryGetProperty(fieldName, out var element))
            return true;

        if (element.ValueKind == JsonValueKind.Null)
            return true;

        var value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();

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
    /// Returns <c>true</c> when all required fields for <paramref name="feedbackType"/>
    /// are present and valid. Logs a warning for each missing field.
    /// Returns <c>true</c> (allow) when the type is unknown to avoid blocking new types
    /// that haven't been added to <see cref="RequiredFeedbackFields"/> yet.
    /// </summary>
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

    /// <summary>
    /// Reads a boolean <c>HasPhoto</c> field from the JSON, handling all three cases
    /// the AI may produce: JSON <c>true</c>/<c>false</c> literals, or the strings
    /// <c>"true"</c>/<c>"false"</c>.
    /// </summary>
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

    /// <summary>
    /// Arabic-only characters that must NEVER appear in a <c>&lt;&lt;VERIFICATION&gt;&gt;</c>
    /// block emitted by the AI. The instruction file mandates that verification text be
    /// written in Persian script only — using <c>ی</c>/<c>ک</c>/<c>ه</c> instead of the
    /// Arabic <c>ي</c>/<c>ك</c>/<c>ة</c>, with no Arabic alif variants or harakat. This
    /// keeps the user-visible support replies consistent with the rest of the bot's UI
    /// (which uses Persian script) and surfaces AI drift early via a single log line.
    /// </summary>
    private static readonly char[] ArabicOnlyCharacters =
    {
        '\u064A', // ARABIC LETTER YEH        ي  (Persian uses ی U+06CC)
        '\u0643', // ARABIC LETTER KAF        ك  (Persian uses ک U+06A9)
        '\u0629', // ARABIC LETTER TEH MARBUTA ة (Persian uses ه U+0647)
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
    /// by the AI against the rules defined in the chat-instruction file:
    /// the text must be Persian-only and contain none of <see cref="ArabicOnlyCharacters"/>.
    /// Violations are logged but never thrown — the block is stripped from the visible
    /// reply in every code path, so a malformed verification cannot reach the user.
    /// Logging it here gives operators an early signal that the AI is drifting from the
    /// instruction file and that the instruction needs to be tightened.
    /// </summary>
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

