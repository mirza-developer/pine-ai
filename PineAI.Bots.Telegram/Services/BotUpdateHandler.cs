using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Shared.Messages;
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
    /// <summary>Platform name used in user-facing support messages.</summary>
    private const string PlatformName = "تلگرام";

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
            await botClient.SendMessage(chatId, BotSharedMessages.PenaltyLocked, cancellationToken: ct);
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
            await SendAndEnqueueBotReplyAsync(chatId, username, BotSharedMessages.PenaltyApplied, ct);
            return;
        }

        sessionStore.SetSession(chatId, response.SerializedSession);

        var orderCodes = new List<string>();
        var visibleText = ResponseBlockTools.StripOrderCodeBlocks(textAfterPenalty, orderCodes);
        visibleText = ResponseBlockTools.StripFeedbackBlocks(visibleText, out var feedbackJson);
        visibleText = ResponseBlockTools.StripVerificationBlocks(visibleText, out var aiVerificationText);
        FeedbackValidator.ValidateAiVerificationText(aiVerificationText, logger);

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

            if (!FeedbackValidator.ValidateFeedbackJson(feedbackType, root, logger))
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
            var unconfiguredMsg = BotSharedMessages.SupportAcknowledgement(PlatformName);
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
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SatisfactionSuccess, ct);
        var log = FeedbackLogBuilder.BuildSatisfactionLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.ComplaintSuccess(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupComplaintOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildComplaintLog(root, userTelegramUsername, orderInfo);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleDefectiveProductAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        bool hasPhoto = FeedbackValidator.GetHasPhoto(root);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildDefectiveProductLog(root, userTelegramUsername, hasPhoto, orderInfo);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
        await ForwardStoredPhotosAsync(userChatId, targetChatId, hasPhoto, "DefectiveProduct", ct);
    }

    private async Task HandlePhotoMismatchAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        bool hasPhoto = FeedbackValidator.GetHasPhoto(root);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildPhotoMismatchLog(root, userTelegramUsername, hasPhoto, orderInfo);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
        await ForwardStoredPhotosAsync(userChatId, targetChatId, hasPhoto, "PhotoMismatch", ct);
    }

    private async Task HandleReturnedPackageAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildReturnedPackageLog(root, userTelegramUsername, orderInfo);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleWholesaleAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.WholesaleSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildWholesaleLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleNoOrderCodeAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.ReviewSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildNoOrderCodeLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleFailedPaymentAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.ReviewSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildFailedPaymentLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleDelayedDeliveryAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.FollowUpSuccess(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildDelayedDeliveryLog(root, userTelegramUsername, orderInfo);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleWrongSizeAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildWrongSizeLog(root, userTelegramUsername, orderInfo);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleUnknownQueryAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.MessageReceivedSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildUnknownQueryLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleInStoreBillingErrorAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        bool hasPhoto = FeedbackValidator.GetHasPhoto(root);
        var log = FeedbackLogBuilder.BuildInStoreBillingErrorLog(root, userTelegramUsername, hasPhoto);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
        await ForwardStoredPhotosAsync(userChatId, targetChatId, hasPhoto, "InStoreBillingError", ct);
    }

    private async Task HandleInStoreComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.InStoreComplaintSuccess, ct);
        var log = FeedbackLogBuilder.BuildInStoreComplaintLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    private async Task HandleStoreHoursQueryAsync(long userChatId, long targetChatId, JsonElement root, string userTelegramUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.StoreHoursQuerySuccess, ct);
        var log = FeedbackLogBuilder.BuildStoreHoursQueryLog(root, userTelegramUsername);
        await botClient.SendMessage(targetChatId, log, cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Forwards stored photos for a user to the target admin chat when the AI indicated
    /// a photo was received.  Logs a warning when no photo is found in the store.
    /// </summary>
    private async Task ForwardStoredPhotosAsync(
        long userChatId, long targetChatId, bool hasPhoto, string feedbackType, CancellationToken ct)
    {
        if (!hasPhoto)
            return;

        var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
        if (storedMessageIds.Count > 0)
        {
            foreach (var msgId in storedMessageIds)
                await botClient.ForwardMessage(targetChatId, userChatId, (int)msgId, cancellationToken: CancellationToken.None);
        }
        else
        {
            logger.LogWarning(
                "HasPhoto=true for {FeedbackType} but no stored photo found for chat {ChatId}",
                feedbackType, userChatId);
        }
    }
}
