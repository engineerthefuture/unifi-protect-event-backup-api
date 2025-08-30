# Unifi Protect Event Backup API


[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![AWS Lambda](https://img.shields.io/badge/AWS-Lambda-FF9900?style=flat-square&logo=amazon-aws)](https://aws.amazon.com/lambda/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Tests](https://img.shields.io/badge/Tests-76_passing-brightgreen?style=flat-square)]()
[![Coverage](https://img.shields.io/badge/Coverage-High-brightgreen?style=flat-square)]()

An enterprise-grade AWS Lambda function that receives and processes webhook events from Unifi Dream Machine Protect systems, storing alarm event data in S3 for backup and analysis. The system includes automated video download capabilities using browser automation with comprehensive fault tolerance and retry mechanisms.

## 🚀 Quick Start

**New to this project?** Start with the [Quickstart Guide](docs/QUICKSTART.md) for complete setup instructions.

**Already familiar?** Here's the essentials:
1. **Fork & Configure**: Set up GitHub repository variables and secrets
2. **Deploy**: Push to main branch for automated deployment
3. **Configure Unifi**: Add webhook endpoint to your Unifi Protect system
4. **Monitor**: Check CloudWatch logs and S3 storage

## 📚 Table of Contents

- [Overview](#-overview)
- [Features](#-features)
- [Video Download Capabilities](#-video-download-capabilities)
- [Architecture](#-architecture)
- [API Endpoints](#-api-endpoints)
- [Setup and Deployment](#-setup-and-deployment)
- [Testing](#-testing)
- [Security and Access Control](#-security-and-access-control)
- [Monitoring and Logs](#-monitoring-and-logs)
- [Data Structure](#-data-structure)
- [Development](#-development)
- [Troubleshooting](#-troubleshooting)
- [Contributing](#-contributing)

## 📋 Overview

This serverless application provides a comprehensive backup and retrieval system for Unifi Protect alarm events and associated video content. When motion detection, intrusion alerts, or other configured events occur in your Unifi Protect system, webhooks are sent to this Lambda function which processes and stores both the event data and downloads the corresponding video files to Amazon S3.

### ⚡ Key Benefits
- **Zero Infrastructure Management**: Fully serverless AWS architecture
- **Fault Tolerant**: Dead Letter Queue with automatic retry mechanisms
- **High Availability**: Multi-environment support with automated CI/CD
- **Secure**: End-to-end encryption with IAM role-based access control
- **Cost Effective**: Pay-per-use serverless model with automatic lifecycle policies
- **Scalable**: Handles traffic spikes with SQS queuing and auto-scaling

## ✨ Features

### Core Functionality
- **Webhook Processing**: Receives real-time alarm events from Unifi Dream Machine
- **Asynchronous Processing**: SQS-based delayed processing for improved reliability
- **Dead Letter Queue**: Automatic retry mechanism for failed video downloads with rich failure metadata
- **Automated Video Download**: Browser automation for video retrieval using HeadlessChromium optimized for AWS Lambda
- **Data Storage**: Stores event data and videos in S3 with organized folder structure
- **Device Mapping**: Maps device MAC addresses to human-readable names via a single DeviceMetadata JSON configuration (set as a GitHub repository variable and passed to CloudFormation)
- **Configurable UI Automation**: Device-specific UI coordinates are now managed in DeviceMetadata JSON, not environment variables
- **Event Retrieval**: RESTful API for retrieving stored alarm events and video presigned URLs

### Technical Features
- **Multi-Environment Deployment**: Separate development and production environments
- **CORS Support**: Cross-origin resource sharing for web client integration
- **Comprehensive Error Handling**: Detailed logging and error management with DLQ retry mechanisms
- **Scalable Architecture**: Serverless design that scales automatically
- **Configurable UI Automation**: Environment variable-based coordinate configuration for browser interactions
- **Enterprise Test Coverage**: 78 unit tests with line, branch, and method coverage analysis plus complexity metrics

## 🎥 Video Download Capabilities

### 🔄 Automated Video Retrieval Process

The system includes asynchronous browser automation to download video content directly from Unifi Protect:

| Step | Process | Technology |
|------|---------|------------|
| 1 | **Webhook Receipt** | API Gateway receives alarm event and immediately returns success |
| 2 | **Event Queueing** | Lambda queues event for delayed processing (default: 2 minutes) |
| 3 | **Delayed Processing** | SQS triggers Lambda after delay to ensure video availability |
| 4 | **Browser Launch** | HeadlessChromium launches optimized for AWS Lambda environment |
| 5 | **Authentication** | Automated login to Unifi Protect using stored credentials |
| 6 | **Navigation** | Programmatic navigation to specific event pages using configurable coordinates |
| 7 | **Video Extraction** | Direct blob URL access and video content download |
| 8 | **S3 Storage** | Organized storage in S3 with date-based folder structure |

### 🎯 Benefits of Delayed Processing

- **Improved Success Rate**: 2-minute delay ensures videos are fully processed in Unifi Protect
- **Better Performance**: Immediate webhook response prevents timeouts and client retries
- **Enhanced Reliability**: Reduces failed downloads due to video availability timing
- **Scalable Processing**: SQS handles traffic spikes and provides automatic retries

### 🏗️ Technical Implementation

- **AWS Lambda Optimization**: Uses HeadlessChromium.Puppeteer.Lambda.Dotnet for Lambda-optimized browser automation
- **Configurable UI Interaction**: Click coordinates for archive button configurable via environment variables
- **Enhanced Download Configuration**: Chrome DevTools Protocol (CDP) integration for reliable download handling
- **Comprehensive Error Handling**: Detailed logging and retry mechanisms for browser automation
- **Performance Monitoring**: Three diagnostic screenshots captured at key stages (login, page load, archive click) for debugging

### 📁 Storage Organization

```
S3 Bucket Structure:
├── events/
│   └── YYYY-MM-DD/
│       ├── {eventId}_{deviceMac}_{timestamp}.json
│       └── {eventId}_{deviceMac}_{timestamp}.mp4
└── screenshots/
    └── YYYY-MM-DD/
        ├── {eventId}_{deviceMac}_{timestamp}_login-screenshot.png
        ├── {eventId}_{deviceMac}_{timestamp}_pageload-screenshot.png
        └── {eventId}_{deviceMac}_{timestamp}_afterarchivebuttonclick-screenshot.png
```

### ⏰ Data Retention Policy

The S3 storage bucket is configured with an automatic lifecycle policy that deletes all objects after **30 days**. This policy provides:

- ✅ **Automatic Cleanup**: Event data and video files are automatically removed after 30 days
- 💰 **Cost Management**: Prevents unlimited storage growth and associated costs
- 📋 **Compliance**: Maintains a consistent 30-day retention period for all alarm events
- 🔧 **Maintenance-Free**: No manual intervention required for data cleanup

The lifecycle rule applies to both JSON event files and MP4 video files, ensuring the bucket remains within reasonable storage limits while preserving recent events for analysis and review.

## 🏛️ Architecture

```mermaid
flowchart TD
  subgraph User
    BROWSER[User's Browser]
  ```mermaid
  flowchart TD
    subgraph User
      BROWSER["User's Browser"]
    end

    subgraph "UI Hosting & Authentication"
      S3UI["S3 UI Bucket"]
      CF["CloudFront Distribution"]
      EDGE["Lambda@Edge Auth"]
      COGNITO["Cognito Hosted UI"]
    end

    subgraph "API & Processing"
      API["API Gateway"]
      LAMBDA["Lambda Function"]
      SQS["SQS Queue"]
      DLQ["DLQ"]
      SECRETS["AWS Secrets Manager"]
      S3DATA["S3 Event/Video Bucket"]
    end

    %% UI Flow
    BROWSER -- UI Request --> CF
    CF -- invokes --> EDGE
    EDGE -- valid token? --> S3UI
    EDGE -- not authenticated --> COGNITO
    COGNITO -- login/callback --> EDGE
    S3UI -- UI static files --> BROWSER

    %% API Flow
    BROWSER -- API Request --> API
    API -- Lambda Proxy --> LAMBDA
    LAMBDA -- S3 Event/Video --> S3DATA
    LAMBDA -- SQS Message --> SQS
    SQS -- Retry/Fail --> DLQ
    LAMBDA -- Secrets --> SECRETS
    LAMBDA -- Response --> API
    API -- Data --> BROWSER

    %% Webhook/Event Flow
    UDM["Unifi Dream Machine"] -. Webhook .-> API
    API -. Immediate Response .-> UDM
    LAMBDA -. Device Mapping .-> S3DATA
    LAMBDA -. Video Download .-> S3DATA
    LAMBDA -. Store Event .-> S3DATA
    LAMBDA -. Store Screenshot .-> S3DATA

    %% Monitoring & Security
    LAMBDA -. Logs .-> CW["CloudWatch Logs"]
    LAMBDA -. Metrics .-> METRICS["CloudWatch Metrics"]
    LAMBDA -. Tracing .-> XRAY["X-Ray"]
    LAMBDA -. IAM Role .-> IAM["IAM"]
    S3DATA -. Encryption .-> KMS["KMS"]
    SECRETS -. Encryption .-> KMS

    %% Admin
    ADMIN["Admin"] -. User Management .-> COGNITO
  ```
    PROD_DEPLOY -.->|bf-prod-*| S3
    
    %% Styling
    classDef aws fill:#ff9900,stroke:#232f3e,stroke-width:2px,color:#fff
    classDef security fill:#d32f2f,stroke:#fff,stroke-width:2px,color:#fff
    classDef processing fill:#1976d2,stroke:#fff,stroke-width:2px,color:#fff
    classDef storage fill:#388e3c,stroke:#fff,stroke-width:2px,color:#fff
    
    class API,AUTH,CORS aws
    class IAM,KMS,SECRETS security
    class QUEUE,DLQ,ESM,HANDLER,DELAYED processing
    class S3,EVENTS,VIDEOS storage
```

### 🔑 Key Architectural Components

#### 🔄 **Dead Letter Queue (DLQ) Integration & Failure Email Notification**
- **Automatic Retry Mechanism**: Failed video downloads automatically send original alarm messages to a dedicated DLQ
- **Rich Failure Metadata**: DLQ messages include `FailureReason`, `OriginalTimestamp`, and `RetryAttempt` attributes
- **Failure Email Notification**: When a video download fails and a message is sent to the DLQ, the system automatically sends a failure notification email to the configured recipient(s). The email includes details about the failed event, the reason for failure, the DLQ message ID, and the retry attempt number.
- **Configuration**: Email recipients and sending method (e.g., AWS SES) are configured via environment variables or application settings. See the deployment documentation for setup details.
- **Exact Message Preservation**: Original alarm event preserved in DLQ for perfect retry scenarios
- **Specific Error Handling**: "No video files were downloaded" errors trigger DLQ processing and email notification
- **Manual Retry Support**: DLQ messages can be manually re-queued to main processing queue
- **Fault Tolerance**: Ensures no alarm events are lost due to temporary video availability issues

#### 📊 **SQS Delayed Processing Architecture**
- **Immediate Webhook Response**: API Gateway responds instantly (HTTP 200) after queuing the event
- **Configurable Delay**: Default 2-minute delay ensures video availability before processing
- **Message-Level Delay**: Each SQS message includes `DelaySeconds` for precise timing control
- **Auto-scaling**: Event Source Mapping automatically scales Lambda concurrency based on queue depth
- **Error Handling**: Dead Letter Queue captures failed messages after 3 retry attempts
- **Long Polling**: 20-second ReceiveMessageWaitTimeSeconds reduces API calls and improves efficiency

#### 🔐 **AWS Secrets Manager Integration**
- **Secure Credential Storage**: Unifi Protect credentials encrypted at rest using AWS KMS
- **Runtime Retrieval**: Lambda function retrieves credentials dynamically with caching for performance
- **Least Privilege Access**: IAM policies grant only `secretsmanager:GetSecretValue` permission
- **Credential Structure**: JSON format with `hostname`, `username`, and `password` fields
- **Rotation Ready**: Supports AWS Secrets Manager automatic credential rotation capabilities

#### 📁 **Enhanced File Organization**
- **EventId-Based Naming**: Files prefixed with `{eventId}_{deviceMac}_{timestamp}` for direct lookup
- **S3 Prefix Search**: O(1) event retrieval using S3 prefix matching instead of JSON parsing
- **Date-Based Folders**: Events organized in `YYYY-MM-DD/` folders for logical browsing
- **Dual Storage**: Event JSON and corresponding MP4 video files stored with matching keys

#### 🛡️ **Security & Compliance**
- **End-to-End Encryption**: S3 AES256 encryption, Secrets Manager KMS encryption
- **IAM Role-Based Access**: Least privilege permissions for Lambda execution
- **API Key Authentication**: API Gateway requires valid API key for all requests
- **CORS Support**: Configurable Cross-Origin Resource Sharing for web clients
- **Audit Trail**: All operations logged to CloudWatch with detailed execution context

### 🔄 Enhanced Data Flow

| Step | Process | Component |
|------|---------|-----------|
| 1 | **Event Detection** | Unifi cameras detect motion/intrusion events |
| 2 | **Webhook Trigger** | Unifi Dream Machine sends webhook to API Gateway |
| 3 | **Authentication** | API Gateway validates API key |
| 4 | **Event Queueing** | Lambda function validates JSON and queues event in SQS with delay |
| 5 | **Immediate Response** | API returns success immediately without blocking |
| 6 | **Delayed Processing** | After 2-minute delay, SQS triggers Lambda for processing |
| 7 | **Device Mapping** | Lambda maps device MAC addresses to human-readable names |
| 8 | **Event Storage** | Events stored in S3 with date-organized folder structure |
| 9 | **Video Download** | HeadlessChromium launches optimized browser for video retrieval |
| 10 | **Browser Automation** | Authenticates with Unifi Protect and navigates to event using configurable coordinates |
| 11 | **Video Extraction** | Extracts blob URL and downloads video content as MP4 |
| 12 | **Video Storage** | MP4 files stored in S3 under organized date-based folders |
| 13 | **Screenshot Capture** | Diagnostic screenshots captured at key automation stages for debugging |
| 14 | **Error Handling** | Failed video downloads trigger DLQ with original message and failure metadata |
| 15 | **Retry Capability** | DLQ messages can be manually re-queued for retry processing |
| 16 | **Monitoring** | All operations logged to CloudWatch for observability |
| 17 | **Retrieval** | GET endpoints allow querying events and generating video presigned URLs |

## 🗂️ File Structure

```
src/
├── Configuration/
│   └── AppConfiguration.cs              # Centralized configuration management
├── Infrastructure/
│   └── ServiceFactory.cs                # Dependency injection and service composition
├── Models/
│   └── UnifiCredentials.cs              # Data models for Unifi Protect credentials
├── Services/
│   ├── IAlarmProcessingService.cs        # Interface for alarm event processing
│   ├── ICredentialsService.cs           # Interface for credential management
│   ├── IRequestRouter.cs                # Interface for HTTP request routing
│   ├── IResponseHelper.cs               # Interface for HTTP response generation
│   ├── IS3StorageService.cs             # Interface for S3 storage operations
│   ├── ISqsService.cs                   # Interface for SQS message handling
│   ├── IUnifiProtectService.cs          # Interface for Unifi Protect interactions
│   └── Implementations/
│       ├── AlarmProcessingService.cs    # Core alarm event processing logic
│       ├── CredentialsService.cs        # AWS Secrets Manager integration
│       ├── RequestRouter.cs             # HTTP request routing and validation
│       ├── ResponseHelper.cs            # HTTP response formatting
│       ├── S3StorageService.cs          # Amazon S3 storage operations
│       ├── SqsService.cs                # Amazon SQS message processing
│       └── UnifiProtectService.cs       # Unifi Protect video download automation
├── AssemblyInfo.cs                      # Assembly metadata and attributes
├── Event.cs                             # Event data models and structures
├── UnifiWebhookEventHandler.cs          # Main Lambda entry point
```

## 🌐 API Endpoints

### 🌍 Custom Domain Support

The API Gateway can be configured with a custom domain name for more professional endpoints with automatic SSL certificate creation and DNS setup.

**Default URL Format:**
```
https://{api-id}.execute-api.{region}.amazonaws.com/{stage}/alarmevent
```

**Custom Domain Format:**
```
https://api.example.com/{stage}/alarmevent
```

#### ✅ Automatic Setup Features
- ✅ **SSL Certificate Creation**: Automatically creates and validates certificates (mandatory)
- ✅ **DNS Configuration**: Sets up Route53 A and AAAA records
- ✅ **Domain Validation**: Handles certificate validation via DNS  
- ✅ **Multi-Environment Support**: Different subdomains per environment
- ✅ **GitHub Actions Integration**: Environment variables for automated deployment

#### Configuration Requirements

**Required Parameters:**
-- `DomainName`: Your custom domain (e.g., `api.example.com`)
- `HostedZoneId`: Route53 Hosted Zone ID for your domain

**GitHub Repository Variables:**
- `HOSTED_ZONE_ID`: Route53 Hosted Zone ID
- `PROD_DOMAIN_NAME`: Production domain (e.g., `api.example.com`)
- `DEV_DOMAIN_NAME`: Development domain (e.g., `api-dev.example.com`)

See [DEPLOYMENT.md](docs/DEPLOYMENT.md) for detailed setup instructions.

### 🎯 Core Endpoints

#### 1. � Event Summary - `GET /{stage}/summary`
Returns the latest event and event count for each camera in the last 24 hours, plus a total event count. Each camera's latest event includes a presigned video link.
- **Purpose**: Get a summary of recent activity for all cameras
- **Authentication**: API Key required
- **Parameters**: None required
- **Response**: JSON array with one entry per camera, each containing:
  - `cameraName`: Name of the camera
  - `eventCount`: Number of events in the last 24 hours
  - `latestEvent`: The most recent event object
  - `videoUrl`: Presigned S3 URL for the latest event's video
  - `totalCount`: Total number of events in the last 24 hours (across all cameras)

#### 2. �📨 Webhook Receiver - `POST /{stage}/alarmevent`
Receives and queues alarm events from Unifi Protect systems for delayed processing
- **Purpose**: Validate webhook data and queue for processing after configurable delay
- **Authentication**: API Key required
- **Request**: JSON webhook payload from Unifi Dream Machine
- **Response**: Immediate success with queue information (eventId, processing delay, estimated completion time)
- **Processing**: Events queued in SQS with 2-minute delay for improved video download reliability

#### 3. 🔍 Event Retrieval - `GET /{stage}/?eventId={eventId}`
Retrieves stored alarm event data and video by event ID
- **Purpose**: Fetch specific alarm event JSON data and video download URL using the Unifi Protect event ID
- **Authentication**: API Key required
- **Parameters**: `eventId` - Event identifier (format: Unifi Protect event ID used as filename prefix: `{eventId}_{deviceMac}_{timestamp}.json`)
- **Response**: Complete alarm event JSON object with presigned video download URL
- **Optimization**: Uses eventId as filename prefix for direct file lookup without JSON parsing

#### 4. 📹 Latest Video Access - `GET /{stage}/latestvideo`
Provides presigned URL for downloading the most recent video file from all stored events
- **Purpose**: Get secure download URL for the latest MP4 video file
- **Authentication**: API Key required
- **Parameters**: None required
- **Response**: JSON with presigned S3 URL and metadata (URL expires in 1 hour)
- **File Naming**: Suggested filename `latest_video_{YYYY-MM-DD_HH-mm-ss}.mp4`
- **Optimization**: Efficiently searches from today's date folder backwards, day by day
- **Payload Limit Solution**: Uses presigned URLs to handle large video files (>6MB) that exceed API Gateway limits

#### 5. 📊 Camera Metadata - `GET /{stage}/metadata`
Fetches and stores current camera metadata from the Unifi Protect API
- **Purpose**: Retrieve camera configuration, names, and technical specifications from your Unifi Protect system
- **Authentication**: API Key required
- **Parameters**: None required
- **Response**: Complete camera metadata JSON with device information, names, MAC addresses, and configuration details
- **Storage**: Automatically stores fetched metadata as `metadata/cameras.json` in S3 for future reference
- **API Path**: Configurable via `UNIFI_API_METADATA_PATH` repository secret (defaults to `/proxy/protect/api/cameras`)
- **Use Cases**: Camera discovery, device mapping, configuration validation, system inventory

### 📖 OpenAPI 3.0 Specification


**Complete API Documentation:**
- [docs/openapi.yaml](docs/openapi.yaml) (OpenAPI 3.0 spec)
- [Swagger UI (OpenAPI) – GitHub Pages](https://engineerthefuture.github.io/unifi-protect-event-backup-api/)

The full OpenAPI 3.0 specification is available in the [`docs/openapi.yaml`](docs/openapi.yaml) file and includes:

- 📝 **Complete endpoint documentation** with detailed request/response schemas
- 🎯 **Interactive examples** for all supported event types (motion, person, vehicle detection)
- ⚠️ **Comprehensive error handling** documentation with specific error codes
- 🔐 **Authentication and security** requirements
- ✅ **Validation patterns** for MAC addresses, timestamps, and event keys
- 🔧 **Client code generation support** for multiple programming languages

### 🛠️ Using the OpenAPI Specification

#### **1. View Interactive Documentation**
```bash
# Swagger UI
npx swagger-ui-serve docs/openapi.yaml

# Redoc
npx redoc-cli serve docs/openapi.yaml
```

#### **2. Generate Client SDKs**
```bash
# TypeScript/JavaScript client
npx @openapitools/openapi-generator-cli generate \
  -i docs/openapi.yaml \
  -g typescript-axios \
  -o ./generated-client

# Python client
openapi-generator-cli generate \
  -i docs/openapi.yaml \
  -g python \
  -o ./python-client
```

#### **3. API Testing**
- Import `docs/openapi.yaml` into **Postman**, **Insomnia**, or **Bruno**
- Use with **curl** for command-line testing
- Create mock servers with **Prism**: `npx @stoplight/prism mock docs/openapi.yaml`

#### **4. Validation**
```bash
# Validate specification
npx swagger-parser validate docs/openapi.yaml

# Lint for best practices
npx spectral lint docs/openapi.yaml
```


### 📋 Quick Reference

#### 📈 GET /summary
Returns a summary of the latest event and event count for each camera in the last 24 hours, plus a total event count. Each camera's latest event includes a presigned video link.

**Parameters**: None required

**Response**: JSON array, one entry per camera:
- `cameraName`: Name of the camera
- `eventCount`: Number of events in the last 24 hours
- `latestEvent`: The most recent event object
- `videoUrl`: Presigned S3 URL for the latest event's video
- `totalCount`: Total number of events in the last 24 hours (across all cameras)

**Example Response**:
```json
[
  {
    "cameraName": "Front Door Camera",
    "eventCount": 5,
    "latestEvent": {
      "eventId": "67b389ab005ec703e40075a5",
      "timestamp": 1739819436108,
      "eventType": "motion",
      "deviceName": "Front Door Camera"
    },
    "videoUrl": "https://s3.amazonaws.com/bucket/2025-01-17/28704E113F33_1739819436108.mp4?X-Amz-Signature=..."
  },
  {
    "cameraName": "Backyard Camera",
    "eventCount": 2,
    "latestEvent": {
      "eventId": "aabbccddeeff001122334455",
      "timestamp": 1739819436123,
      "eventType": "person",
      "deviceName": "Backyard Camera"
    },
    "videoUrl": "https://s3.amazonaws.com/bucket/2025-01-17/112233445566_1739819436123.mp4?X-Amz-Signature=..."
  }
]
```

#### 📨 POST /alarmevent
Processes incoming alarm webhook events from Unifi Protect.

**Request Body**: JSON alarm event from Unifi Protect
**Response**: Success confirmation with event details

#### 🔍 GET /?eventId={id}
Downloads a video file by searching for the specified Unifi Protect event ID and returning a presigned URL.

**Parameters:**
- `eventId`: Unifi Protect event identifier (24-character hexadecimal string)

**Response**: JSON object containing:
- `downloadUrl`: Presigned S3 URL for direct video download (expires in 1 hour)
- `filename`: Suggested filename (`event_{eventId}_{YYYY-MM-DD_HH-mm-ss}.mp4`)
- `videoKey`: S3 object key for the video file
- `eventKey`: S3 object key for the corresponding event JSON data
- `eventId`: The searched Unifi Protect event identifier
- `timestamp`: Unix timestamp when the event occurred
- `eventDate`: Human-readable event date and time
- `expiresAt`: When the download URL expires
- `eventData`: Complete alarm event details including device name, trigger type, zones, and timestamps
- `message`: Instructions for using the download URL

**How it works:**
1. Searches through date-organized S3 folders (YYYY-MM-DD) for JSON event files
2. Parses each event file to find the matching eventId in trigger data
3. Locates the corresponding video file (.mp4)
4. Generates a secure, time-limited presigned URL for direct S3 download
5. Returns complete event context and metadata along with download URL

**Example Response for eventId**:
```json
{
  "downloadUrl": "https://s3.amazonaws.com/bucket/2025-01-17/28704E113F33_1739819436108.mp4?X-Amz-Signature=...",
  "filename": "event_67b389ab005ec703e40075a5_2025-01-17_20-43-56.mp4",
  "videoKey": "2025-01-17/28704E113F33_1739819436108.mp4",
  "eventKey": "2025-01-17/28704E113F33_1739819436108.json",
  "eventId": "67b389ab005ec703e40075a5",
  "timestamp": 1739819436108,
  "eventDate": "2025-01-17 20:43:56",
  "expiresAt": "2025-01-17 21:43:56 UTC",
  "eventData": {
    "name": "Motion Detection Alert",
    "timestamp": 1739819436108,
    "triggers": [
      {
        "key": "motion",
        "device": "28704E113F33",
        "eventId": "67b389ab005ec703e40075a5",
        "deviceName": "Backyard West"
      }
    ]
  },
  "message": "Use the downloadUrl to download the video file directly. URL expires in 1 hour."
}
```

#### 📹 GET /latestvideo
Returns a presigned URL for downloading the most recent video file from all stored events.

**Parameters**: None required

**Response**: JSON object containing:
- `downloadUrl`: Presigned S3 URL for direct video download (expires in 1 hour)
- `filename`: Suggested filename (`latest_video_{YYYY-MM-DD_HH-mm-ss}.mp4`)
- `videoKey`: S3 object key for the video file
- `eventKey`: S3 object key for the corresponding event JSON data
- `timestamp`: Unix timestamp when the event occurred
- `eventDate`: Human-readable event date and time
- `expiresAt`: When the download URL expires
- `eventData`: Complete alarm event details including device name, trigger type, zones, and timestamps
- `message`: Instructions for using the download URL

**Why Presigned URL?**: Video files typically exceed API Gateway's 6MB payload limit, so this endpoint returns a secure, time-limited URL for direct S3 download instead of the video data itself.

**Example Response**:
```json
{
  "downloadUrl": "https://s3.amazonaws.com/bucket/2024-01-15/video_1705316234.mp4?X-Amz-Signature=...",
  "filename": "latest_video_2024-01-15_10-30-34.mp4",
  "videoKey": "2024-01-15/video_1705316234.mp4",
  "eventKey": "2024-01-15/event_1705316234.json",
  "timestamp": 1705316234,
  "eventDate": "2024-01-15 10:30:34 UTC",
  "expiresAt": "2024-01-15 11:30:34 UTC",
  "eventData": {
    "deviceName": "Front Door Camera",
    "triggers": ["MOTION", "PERSON"],
    "zones": ["Driveway", "Walkway"],
    "score": 95,
    "recordingStartTime": 1705316234
  },
  "message": "Use the downloadUrl to download the video file directly"
}
```

#### 📊 GET /metadata
Fetches current camera metadata from your Unifi Protect system and stores it in S3.

**Parameters**: None required

**Response**: JSON object containing:
- `message`: Success confirmation message
- `metadata`: Complete camera metadata object with device information

**What it does**:
1. Connects to your Unifi Protect API using stored credentials
2. Fetches complete camera metadata including device names, MAC addresses, and configuration
3. Stores the metadata as `metadata/cameras.json` in your S3 bucket for future reference
4. Returns the fetched metadata in the API response

**Configuration**: The API endpoint path is configurable via the `UNIFI_API_METADATA_PATH` repository secret, which defaults to `/proxy/protect/api/cameras` if not specified.

**Example Response**:
```json
{
  "message": "Camera metadata fetched and stored successfully.",
  "metadata": {
    "cameras": [
      {
        "id": "67b389ab005ec703e40075a5",
        "name": "Front Door Camera",
        "mac": "28:70:4E:11:3F:64",
        "type": "UVC-G4-Doorbell",
        "state": "CONNECTED",
        "isRecording": true,
        "channels": [
          {
            "id": 0,
            "width": 1600,
            "height": 1200,
            "fps": 15
          }
        ]
      }
    ]
  }
}
```

## 🌐 Web UI & Edge Authentication

### Lambda@Edge Authentication with Cognito
- All access to the UI and event backup resources is protected by AWS Lambda@Edge, which validates Cognito JWTs on every request.
- Unauthenticated users are redirected to the Cognito Hosted UI for login.
- Only users created by an admin in Cognito can log in (self-sign-up is disabled).
- After login, the Cognito access token is set as a cookie via a secure callback page (`/auth-callback.html`).
- The Lambda@Edge function only allows requests with a valid Cognito access token.

### UI for Viewing and Managing Event Backup
- The static web UI is deployed to S3 and served via CloudFront, protected by Lambda@Edge authentication.
- After authentication, users can:
  - View a list of backed-up Unifi Protect events
  - Download associated video files (with presigned S3 URLs)
  - Search/filter events by date, device, or event type
- The UI is fully serverless and requires no backend server—API calls are authenticated via Cognito JWTs.

#### How It Works
1. User visits the UI (CloudFront or custom domain)
2. Lambda@Edge checks for a valid Cognito access token cookie
3. If not present, user is redirected to Cognito login
4. After login, Cognito redirects to `/auth-callback.html`, which sets the cookie and redirects to the UI
5. Authenticated users can view and manage event backup data


#### Admin User Management

**Only users created by an admin in the Cognito User Pool can log in to the UI. Self-sign-up is disabled for security.**

**How to Add a User to Cognito (Allow UI Login):**

1. **Via AWS Console:**
  - Go to the Cognito service in the AWS Console.
  - Select the User Pool created by your stack (name will be `bf-<env>-<app>-cognito-userpool`).
  - Go to the "Users" tab and click "Create user".
  - Enter the user's email address and set a temporary password (or let AWS generate one).
  - Ensure the email attribute is marked as verified (or the user will need to verify on first login).
  - Click "Create user". Share the temporary password with the user.

2. **Via AWS CLI:**
  ```sh
  aws cognito-idp admin-create-user \
    --user-pool-id <POOL_ID> \
    --username <EMAIL> \
    --user-attributes Name=email,Value=<EMAIL>
  ```
  - Replace `<POOL_ID>` with your Cognito User Pool ID (see CloudFormation outputs or AWS Console).
  - Replace `<EMAIL>` with the user's email address.
  - The user will receive a temporary password and must set a new password on first login.

**Note:** Only users added this way will be able to log in to the UI.

---

