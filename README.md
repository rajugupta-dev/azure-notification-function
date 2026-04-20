# azure-notification-function

An **Azure Function** (HTTP-triggered, .NET 8 isolated worker) that sends push and VoIP notifications via **AWS SNS**.

Inspired by real notification pipeline work at GlobalLogic (Kastle Systems project) serving enterprise clients like CBRE and JLL.

---

## What It Does

Accepts an HTTP POST request with a notification payload and publishes it to an AWS SNS endpoint. Supports two notification types:

- **Standard push** — sends to both iOS (APNS) and Android (FCM/GCM)
- **VoIP push** — sends to iOS via APNS_VOIP for incoming call alerts (used in access control intercom systems)

---

## How It Works

```
HTTP POST /api/notify
        │
        ▼
Azure Function (SendNotification)
        │
        ├─ Deserializes request payload
        ├─ Builds SNS message structure
        │     ├─ Standard: APNS + APNS_SANDBOX + GCM payloads
        │     └─ VoIP:     APNS_VOIP + APNS_VOIP_SANDBOX payloads
        │
        ▼
AWS SNS PublishAsync()
        │
        ▼
Device (iOS / Android)
```

---

## Sample Request

**POST** `/api/notify`
```json
{
  "targetArn": "arn:aws:sns:us-east-1:123456789:endpoint/APNS/MyApp/abc123",
  "title": "Visitor at Front Door",
  "body": "John Doe is requesting access.",
  "type": 0
}
```

**Response 200 OK**
```json
{
  "messageId": "abc-123-def-456",
  "status": "sent"
}
```

**VoIP call notification:**
```json
{
  "targetArn": "arn:aws:sns:us-east-1:123456789:endpoint/APNS_VOIP/MyApp/xyz789",
  "title": "Incoming Call",
  "body": "Call from lobby intercom",
  "type": 1,
  "callId": "call-001",
  "callerName": "Front Desk"
}
```

---

## Tech Stack

- **Azure Functions v4** (.NET 8 isolated worker)
- **AWS SDK for .NET** — `AWSSDK.SimpleNotificationService`
- **Microsoft.Azure.Functions.Worker** — HTTP trigger
- **Dependency Injection** — SNS client registered as singleton

---

## Key Design Decisions

- **MessageStructure = "json"** — allows different payloads per platform (iOS vs Android) in a single SNS publish call
- **VoIP vs Standard separation** — VoIP uses `APNS_VOIP` which bypasses Do Not Disturb on iOS, critical for security intercom alerts
- **Singleton SNS client** — avoids re-creating the AWS client on every function invocation

---

## Environment Variables Required

| Variable | Description |
|----------|-------------|
| `AWS_ACCESS_KEY_ID` | AWS access key |
| `AWS_SECRET_ACCESS_KEY` | AWS secret key |
| `AWS_REGION` | e.g. `us-east-1` |

Set these in `local.settings.json` for local dev or Azure App Configuration for production.

---

## How to Run Locally

```bash
# 1. Clone the repo
git clone https://github.com/rajugupta-dev/azure-notification-function

# 2. Add local.settings.json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AWS_ACCESS_KEY_ID": "your-key",
    "AWS_SECRET_ACCESS_KEY": "your-secret",
    "AWS_REGION": "us-east-1"
  }
}

# 3. Run the function
func start
```

---

## Related Repos

- [dotnet-access-control-api](https://github.com/rajugupta-dev/dotnet-access-control-api) — The API this function integrates with
- [api-unit-test-samples](https://github.com/rajugupta-dev/api-unit-test-samples) — Unit testing patterns used across projects
