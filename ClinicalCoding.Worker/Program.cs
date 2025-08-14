using Azure.Storage.Queues;
using Azure.Messaging.ServiceBus;
using ClinicalCoding.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

await Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddLogging();
        services.AddDbContext<CodingDbContext>(opt =>
        {
            var conn = ctx.Configuration.GetConnectionString("DefaultConnection");
            opt.UseSqlServer(conn);
        });
        services.AddScoped<EpisodeRepository>();

        services.AddHostedService<DeadLetterWorker>();
    })
    .RunConsoleAsync();

class DeadLetterWorker(IServiceProvider services, ILogger<DeadLetterWorker> logger, IConfiguration cfg) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var provider = cfg["DLQ:Provider"] ?? "Storage";
        logger.LogInformation("DeadLetter worker using provider: {Provider}", provider);

        if (string.Equals(provider, "ServiceBus", StringComparison.OrdinalIgnoreCase))
            await RunServiceBusAsync(stoppingToken);
        else
            await RunStorageQueueAsync(stoppingToken);
    }

    private async Task RunStorageQueueAsync(CancellationToken ct)
    {
        var cs = Environment.GetEnvironmentVariable("DLQ__Storage__ConnectionString") ?? throw new InvalidOperationException("DLQ Storage cs missing");
        var name = Environment.GetEnvironmentVariable("DLQ__Storage__QueueName") ?? "deadletters";
        var q = new QueueClient(cs, name);
        q.CreateIfNotExists();
        while (!ct.IsCancellationRequested)
        {
            var msg = await q.ReceiveMessageAsync(TimeSpan.FromMinutes(1), ct);
            if (msg.Value is null) { await Task.Delay(5000, ct); continue; }

            var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(msg.Value.Body.ToString()));
            if (await ProcessAsync(payload, ct))
                await q.DeleteMessageAsync(msg.Value.MessageId, msg.Value.PopReceipt, ct);
            else
                await q.UpdateMessageAsync(msg.Value.MessageId, msg.Value.PopReceipt, msg.Value.Body, TimeSpan.FromMinutes(10), ct);
        }
    }

    private async Task RunServiceBusAsync(CancellationToken ct)
    {
        var cs = Environment.GetEnvironmentVariable("DLQ__ServiceBus__ConnectionString") ?? throw new InvalidOperationException("DLQ SB cs missing");
        var name = Environment.GetEnvironmentVariable("DLQ__ServiceBus__QueueName") ?? "deadletters";
        await using var client = new ServiceBusClient(cs);
        var proc = client.CreateProcessor(name, new ServiceBusProcessorOptions { MaxConcurrentCalls = 1, AutoCompleteMessages = false });

        proc.ProcessMessageAsync += async args =>
        {
            var payload = args.Message.Body.ToString();
            if (await ProcessAsync(payload, ct))
                await args.CompleteMessageAsync(args.Message, ct);
            else
            {
                // reschedule by abandoning; alternatively send a scheduled message
                await args.AbandonMessageAsync(args.Message, new Dictionary<string, object?> { ["retry"] = (args.Message.DeliveryCount + 1) }, ct);
            }
        };
        proc.ProcessErrorAsync += err => { logger.LogError(err.Exception, "SB processor error"); return Task.CompletedTask; };

        await proc.StartProcessingAsync(ct);
        try { await Task.Delay(Timeout.Infinite, ct); } catch { /* noop */ }
        await proc.StopProcessingAsync(ct);
    }

    private async Task<bool> ProcessAsync(string payload, CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<EpisodeRepository>();
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            var queryId = doc.RootElement.TryGetProperty("queryId", out var q) ? q.GetString() : null;
            var responder = doc.RootElement.TryGetProperty("responder", out var r) ? r.GetString() : null;
            var responseText = doc.RootElement.TryGetProperty("responseText", out var t) ? t.GetString() : payload;
            if (Guid.TryParse(queryId, out var qid))
            {
                await repo.UpdateQueryResponseAsync(qid, responder, responseText ?? "", ct);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
