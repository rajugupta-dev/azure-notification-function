// ============================================================
// azure-notification-function
// Azure Function that triggers push & VoIP notifications via AWS SNS
// Stack: Azure Functions v4 (.NET 8 isolated), AWS SDK, C#
// ============================================================

// --- NotificationFunction.cs ---

using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace AzureNotificationFunction
{
    public class NotificationFunction
    {
        private readonly ILogger<NotificationFunction> _logger;
        private readonly IAmazonSimpleNotificationService _snsClient;

        public NotificationFunction(ILogger<NotificationFunction> logger, IAmazonSimpleNotificationService snsClient)
        {
            _logger = logger;
            _snsClient = snsClient;
        }

        /// <summary>
        /// HTTP-triggered Azure Function that sends a push or VoIP notification via AWS SNS.
        /// POST /api/notify
        /// </summary>
        [Function("SendNotification")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "notify")] HttpRequestData req)
        {
            _logger.LogInformation("SendNotification function triggered.");

            NotificationRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<NotificationRequest>(
                    req.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid request payload.");
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid JSON payload.");
                return badRequest;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.TargetArn))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("TargetArn is required.");
                return badRequest;
            }

            try
            {
                var messagePayload = BuildSnsPayload(request);

                var publishRequest = new PublishRequest
                {
                    TargetArn = request.TargetArn,
                    Message = JsonSerializer.Serialize(messagePayload),
                    MessageStructure = "json"
                };

                var response = await _snsClient.PublishAsync(publishRequest);

                _logger.LogInformation("Notification sent. MessageId: {MessageId}", response.MessageId);

                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { messageId = response.MessageId, status = "sent" });
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SNS notification.");
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteStringAsync("Failed to send notification.");
                return error;
            }
        }

        private static Dictionary<string, string> BuildSnsPayload(NotificationRequest request)
        {
            var payload = new Dictionary<string, string>();

            if (request.Type == NotificationType.VoIP)
            {
                // VoIP push uses APNS_VOIP for iOS
                var apnsVoip = new
                {
                    aps = new
                    {
                        alert = new { title = request.Title, body = request.Body },
                        sound = "default",
                        content_available = 1
                    },
                    callId = request.CallId,
                    callerName = request.CallerName
                };
                payload["APNS_VOIP"] = JsonSerializer.Serialize(apnsVoip);
                payload["APNS_VOIP_SANDBOX"] = JsonSerializer.Serialize(apnsVoip);
            }
            else
            {
                // Standard push notification
                var apns = new
                {
                    aps = new
                    {
                        alert = new { title = request.Title, body = request.Body },
                        sound = "default",
                        badge = 1
                    }
                };
                var fcm = new
                {
                    notification = new { title = request.Title, body = request.Body },
                    data = new { type = "standard" }
                };
                payload["APNS"] = JsonSerializer.Serialize(apns);
                payload["APNS_SANDBOX"] = JsonSerializer.Serialize(apns);
                payload["GCM"] = JsonSerializer.Serialize(fcm);
                payload["default"] = request.Body ?? string.Empty;
            }

            return payload;
        }
    }

    // --- Models ---

    public class NotificationRequest
    {
        public string TargetArn { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Body { get; set; }
        public NotificationType Type { get; set; } = NotificationType.Standard;
        public string? CallId { get; set; }
        public string? CallerName { get; set; }
    }

    public enum NotificationType
    {
        Standard,
        VoIP
    }
}


// --- Program.cs (Azure Functions isolated worker setup) ---

using Amazon;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Register AWS SNS client — credentials loaded from environment variables:
        // AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION
        services.AddSingleton<IAmazonSimpleNotificationService>(_ =>
            new AmazonSimpleNotificationServiceClient(RegionEndpoint.USEast1));
    })
    .Build();

await host.RunAsync();
