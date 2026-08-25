using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PineAI.Backup;

public class Worker(
    ILogger<Worker> logger,
    IOptions<BackupSettings> options,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : BackgroundService
{
    private const long MaxBaleDocumentSizeBytes = 50 * 1024 * 1024;

    private readonly BackupSettings _settings = options.Value;
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Backup worker started. Interval: {Hours} hour(s).", _settings.IntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            try
            {
                await RunBackupCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during the backup cycle.");
            }

            await Task.Delay(TimeSpan.FromHours(_settings.IntervalHours), stoppingToken);
        }
    }

    private async Task RunBackupCycleAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var bakFileName = $"{_settings.DatabaseName}_{timestamp}.bak";
        var zipFileName = $"{_settings.DatabaseName}_{timestamp}.zip";

        Directory.CreateDirectory(_settings.LocalTempPath);

        var bakFilePath = Path.Combine(_settings.LocalTempPath, bakFileName);
        var zipFilePath = Path.Combine(_settings.LocalTempPath, zipFileName);

        try
        {
            logger.LogInformation("Starting database backup: {Database}", _settings.DatabaseName);
            await BackupDatabaseAsync(bakFilePath, cancellationToken);
            logger.LogInformation("Database backup completed: {File}", bakFilePath);

            logger.LogInformation("Compressing backup file...");
            ZipBackupFile(bakFilePath, zipFilePath);
            logger.LogInformation("Compressed to: {File}", zipFilePath);

            logger.LogInformation("Sending backup to Bale chat: {ChatId}", _settings.Bale.ChatId);
            await SendToBaleAsync(zipFilePath, zipFileName, cancellationToken);
            logger.LogInformation("Upload complete: {File}", zipFileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during the backup cycle.");
        }
        finally
        {
            TryDeleteFile(bakFilePath);

            TryDeleteFile(zipFilePath);
        }
    }

    private async Task BackupDatabaseAsync(string bakFilePath, CancellationToken cancellationToken)
    {
        var sql = $"BACKUP DATABASE [{_settings.DatabaseName}] TO DISK = @path WITH FORMAT, INIT, COMPRESSION";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 3600
        };
        command.Parameters.AddWithValue("@path", bakFilePath);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ZipBackupFile(string bakFilePath, string zipFilePath)
    {
        using var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(bakFilePath, Path.GetFileName(bakFilePath), CompressionLevel.Optimal);
    }

    private async Task SendToBaleAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(localFilePath);
        if (fileInfo.Length > MaxBaleDocumentSizeBytes)
        {
            throw new InvalidOperationException(
                $"Backup file '{remoteFileName}' is {fileInfo.Length} bytes, which exceeds the {MaxBaleDocumentSizeBytes} byte limit for Bale documents.");
        }

        var client = httpClientFactory.CreateClient();
        var requestUrl = $"https://tapi.bale.ai/bot{_settings.Bale.BotToken}/sendDocument";

        using var content = new MultipartFormDataContent
        {
            { new StringContent(_settings.Bale.ChatId), "chat_id" }
        };

        await using var fileStream = File.OpenRead(localFilePath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "document", remoteFileName);

        using var response = await client.PostAsync(requestUrl, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("ok", out var okProperty) || !okProperty.GetBoolean())
        {
            var description = document.RootElement.TryGetProperty("description", out var descriptionProperty)
                ? descriptionProperty.GetString()
                : "unknown error";
            throw new InvalidOperationException($"Bale sendDocument failed: {description}");
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temp file: {Path}", path);
        }
    }
}
