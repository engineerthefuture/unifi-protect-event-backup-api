## Step 8: Add Users to Cognito for UI Login

By default, only users created by an admin in the Cognito User Pool can log in to the UI. Self-sign-up is disabled for security.

**To add a user (allow UI login):**

### Option 1: AWS Console
1. Go to the AWS Console → Cognito → User Pools.
2. Select the User Pool created by your stack (name: `bf-<env>-<app>-cognito-userpool`).
3. Go to the "Users" tab and click "Create user".
4. Enter the user's email address and set a temporary password (or let AWS generate one).
5. Ensure the email attribute is marked as verified (or the user will need to verify on first login).
6. Click "Create user". Share the temporary password with the user.

### Option 2: AWS CLI
```sh
aws cognito-idp admin-create-user \
  --user-pool-id <POOL_ID> \
  --username <EMAIL> \
  --user-attributes Name=email,Value=<EMAIL>
```
- Replace `<POOL_ID>` with your Cognito User Pool ID (see CloudFormation outputs or AWS Console).
- Replace `<EMAIL>` with the user's email address.
- The user will receive a temporary password and must set a new password on first login.

**Only users added this way can log in to the UI.**
# Unifi Protect Event Backup API - Quickstart Guide

A step-by-step guide to deploy and configure the Unifi Protect Event Backup API in your AWS account for automated alarm event processing and video backup.

## Prerequisites

### Required Components

### Local Development Tools (Optional)

### Important Notes

## Step 1: Setup GitHub Organization and Repository

### 1.1 Create GitHub Organization (Required for AWS OIDC)

GitHub organizations are required for AWS OIDC integration. If you don't already have one:

1. Go to [GitHub Organizations](https://github.com/organizations/new)
2. Choose **Create a free organization**
3. Fill in the organization details:
   - **Organization account name**: Choose a unique name (e.g., `yourname-aws` or `yourcompany-dev`)
   - **Contact email**: Your email address
   - **This organization belongs to**: Select **My personal account**
4. Click **Next** and complete the setup
5. **Important**: Make note of your organization name - you'll need it for the OIDC configuration

> **Why Organizations?**: AWS OIDC trust policies require GitHub organization context for security. Personal repositories cannot establish OIDC connections to AWS.

### 1.2 Fork the Repository to Your Organization

1. Visit the [original repository](https://github.com/engineerthefuture/unifi-protect-event-backup-api)
2. Click **Fork** 
3. **Important**: Under "Owner", select your **organization** (not your personal account)
4. Keep the repository name as `unifi-protect-event-backup-api`
5. Click **Create fork**
6. Clone your forked repository locally:
   ```bash
   git clone https://github.com/YOUR_ORGANIZATION/unifi-protect-event-backup-api.git
   cd unifi-protect-event-backup-api
   ```

### 1.3 Configure GitHub Repository Settings

#### Repository Variables (Settings → Secrets and variables → Actions → Variables)
Set these **Repository Variables**:

| Variable | Value | Description |
|----------|-------|-------------|
| `AWS_ACCOUNT_ID` | `123456789012` | Your 12-digit AWS Account ID |
| `OIDC_ROLE_NAME` | `GitHubActionsRole` | IAM role name for GitHub Actions |
| `OWNER_NAME` | `Your Name` | Resource owner for tagging |
| `APP_NAME` | `unifi-protect-event-backup-api` | Application name |
| `APP_DESCRIPTION` | `Unifi webhook alarm event processing and backup API` | Description |

#### Repository Secrets (Settings → Secrets and variables → Actions → Secrets)
Set these **Repository Secrets**:

| Secret | Value | Description |
|--------|-------|-------------|
| `UNIFI_HOST` | `udm.local` or `192.168.1.1` | Your Unifi Dream Machine hostname/IP |
| `UNIFI_USERNAME` | `your-unifi-username` | Unifi Protect username |
| `UNIFI_PASSWORD` | `your-unifi-password` | Unifi Protect password |

> **Security Note**: These secrets are encrypted by GitHub and only accessible to your Actions workflows.

## Step 2: Setup AWS OIDC Authentication

### 2.1 Create OIDC Identity Provider

Run this AWS CLI command to create the GitHub OIDC provider:

```bash
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com \
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
```

### 2.2 Create IAM Role for GitHub Actions

Create a file `github-actions-trust-policy.json`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::YOUR_ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": "repo:YOUR_ORGANIZATION/unifi-protect-event-backup-api:*"
        }
      }
    }
  ]
}
```

**Replace placeholders**:

Create the IAM role:

```bash
aws iam create-role \
  --role-name GitHubActionsRole \
  --assume-role-policy-document file://github-actions-trust-policy.json
```

### 2.3 Attach Required Policies

Attach the necessary permissions:

```bash
# CloudFormation permissions
aws iam attach-role-policy \
  --role-name GitHubActionsRole \
  --policy-arn arn:aws:iam::aws:policy/CloudFormationFullAccess

# S3 permissions for deployments
aws iam attach-role-policy \
  --role-name GitHubActionsRole \
  --policy-arn arn:aws:iam::aws:policy/AmazonS3FullAccess

# Lambda and IAM permissions
aws iam attach-role-policy \
  --role-name GitHubActionsRole \
  --policy-arn arn:aws:iam::aws:policy/AWSLambda_FullAccess

aws iam attach-role-policy \
  --role-name GitHubActionsRole \
  --policy-arn arn:aws:iam::aws:policy/IAMFullAccess

# API Gateway permissions
aws iam attach-role-policy \
  --role-name GitHubActionsRole \
  --policy-arn arn:aws:iam::aws:policy/AmazonAPIGatewayAdministrator

# Secrets Manager permissions
aws iam attach-role-policy \
  --role-name GitHubActionsRole \
  --policy-arn arn:aws:iam::aws:policy/SecretsManagerReadWrite
```

## Step 3: Deploy Infrastructure

### 3.1 Automatic Deployment (Recommended)

#### For Production Environment:
1. Push changes to the `main` branch:
   ```bash
   git add .
   git commit -m "Configure deployment settings"
   git push origin main
   ```

#### For Development Environment:
1. Create and push to a feature branch:
   ```bash
   git checkout -b feature/initial-setup
   git add .
   git commit -m "Configure deployment settings"
   git push origin feature/initial-setup
   ```

### 3.2 Monitor Deployment

1. Go to your repository's **Actions** tab
2. Watch the "Build, Test, and Deploy CloudFormation Stack" workflow
3. Deployment typically takes 5-10 minutes
4. Verify completion with green checkmarks

### 3.3 Manual Deployment (Alternative)

If you prefer manual deployment:

```bash
# Build the project
dotnet build --configuration Release

# Deploy CloudFormation stack
aws cloudformation deploy \
  --template-file templates/cf-stack-cs.yaml \
  --stack-name bf-prod-unifi-protect-event-backup-api \
  --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM \
  --parameter-overrides \
    OwnerName="Your Name" \
    AppName="unifi-protect-event-backup-api" \
    EnvPrefix="prod" \
    BucketName="bf-prod-s3-unifi-protect-event-backup-api" \
    BucketNameDeployment="bf-prod-s3-deployments" \
    FunctionName="bf-prod-lambda-unifi-protect-event-backup-api" \
    RoleName="bf-prod-lambda-unifi-protect-event-backup-api-role" \
    UnifiHost="YOUR_UNIFI_HOST" \
    UnifiUsername="YOUR_UNIFI_USERNAME" \
    UnifiPassword="YOUR_UNIFI_PASSWORD"

# Deploy Lambda function
dotnet lambda deploy-function \
  --function-name bf-prod-lambda-unifi-protect-event-backup-api
```

## Step 4: Retrieve API Configuration

### 4.1 Get API Gateway Endpoint

```bash
aws cloudformation describe-stacks \
  --stack-name bf-prod-unifi-protect-event-backup-api \
  --query 'Stacks[0].Outputs[?OutputKey==`POSTUnfiWebhookAlarmEventEndpoint`].OutputValue' \
  --output text
```

**Example Output**: `https://abc123def4.execute-api.us-east-1.amazonaws.com/prod/alarmevent`

### 4.2 Get API Key

```bash
aws apigateway get-api-keys \
  --query 'items[?name==`bf-prod-lambda-unifi-protect-event-backup-api-ApiKey`].value' \
  --include-values \
  --output text
```

**Example Output**: `a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6`

## Step 5: Configure Unifi Dream Machine

### 5.1 Access Unifi Protect Settings

1. Open Unifi Protect web interface (usually `https://your-udm-ip`)
2. Navigate to **Settings** → **Advanced** → **Webhooks**
3. Click **Add Webhook**

### 5.2 Webhook Configuration

Configure the webhook with these settings:

| Field | Value | Example |
|-------|-------|---------|
| **Name** | `AWS Event Backup` | `AWS Event Backup` |
| **URL** | Your API Gateway endpoint from Step 4.1 | `https://abc123def4.execute-api.us-east-1.amazonaws.com/prod/alarmevent` |
| **HTTP Method** | `POST` | `POST` |
| **Authentication** | Custom Header | Custom Header |
| **Header Name** | `X-API-Key` | `X-API-Key` |
| **Header Value** | Your API Key from Step 4.2 | `a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6` |

### 5.3 Select Event Types

Choose which events to send:

### 5.4 Test Webhook

1. Click **Test** to verify connectivity
2. You should see a success response
3. Save the webhook configuration

## Step 6: Verify Setup

### 6.1 Trigger Test Event

1. Walk in front of one of your cameras to trigger motion detection
2. Wait 2-3 minutes for processing (videos need time to be available)

### 6.2 Check AWS Resources

#### CloudWatch Logs
```bash
aws logs tail /aws/lambda/bf-prod-lambda-unifi-protect-event-backup-api --follow
```

#### S3 Storage
```bash
aws s3 ls s3://bf-prod-s3-unifi-protect-event-backup-api/ --recursive
```

Expected structure:
```
2025-01-20/
├── eventId_deviceMac_timestamp.json
└── videos/
    └── eventId_deviceMac_timestamp.mp4
```

### 6.3 Test API Endpoints

#### Get Latest Video:
```bash
curl -H "X-API-Key: YOUR_API_KEY" \
  "https://YOUR_API_ENDPOINT/latestvideo"
```

#### Get Specific Event:
```bash
curl -H "X-API-Key: YOUR_API_KEY" \
  "https://YOUR_API_ENDPOINT/?eventId=YOUR_EVENT_ID"
```

## Step 7: Customize Device Names (Optional)

### 7.1 Find Device MAC Addresses

Check CloudWatch logs or S3 files to find your camera MAC addresses (format: `28704E113F64`)

### 7.2 Update CloudFormation Template

Edit `templates/cf-stack-cs.yaml` and modify the device mappings around line 450:

```yaml
Environment:
  Variables:
    # ... other variables ...
    DevicePrefix: "DeviceMac"
    DeviceMac28704E113F64: "Front Door Camera"    # Replace with your MAC and name
    DeviceMacF4E2C67A2FE8: "Backyard Camera"     # Replace with your MAC and name
    DeviceMac28704E113C44: "Side Gate Camera"    # Replace with your MAC and name
    # Add more devices as needed
```

### 7.3 Redeploy

Commit and push changes to update the device mappings:

```bash
git add templates/cf-stack-cs.yaml
git commit -m "Update device name mappings"
git push origin main
```

## Troubleshooting

### Common Issues

#### 1. GitHub Actions Deployment Fails

#### 2. Webhook Test Fails

#### 3. No Videos Downloaded

#### 4. S3 Access Denied

### Debug Commands

```bash
# Check CloudFormation stack status
aws cloudformation describe-stacks \
  --stack-name bf-prod-unifi-protect-event-backup-api

# View Lambda function logs
aws logs describe-log-groups --log-group-name-prefix /aws/lambda/bf-prod

# Test Lambda function directly
aws lambda invoke \
  --function-name bf-prod-lambda-unifi-protect-event-backup-api \
  --payload '{}' \
  response.json

# Check S3 bucket permissions
aws s3api get-bucket-policy \
  --bucket bf-prod-s3-unifi-protect-event-backup-api
```

### Getting Help


## Next Steps

### Security Hardening

### Monitoring & Alerting

### Advanced Features

## Cost Estimation

**Monthly AWS Costs** (approximate):

| Service | Usage | Cost |
|---------|-------|------|
| **Lambda** | 1,000 executions/month | $0.00* |
| **API Gateway** | 1,000 requests/month | $0.00* |
| **S3 Storage** | 10 GB storage | $0.00* |
| **S3 Requests** | 1,000 PUT/GET requests | $0.00* |
| **Secrets Manager** | 1 secret | $0.00* |
| **CloudWatch Logs** | Basic logging | $0.00* |
| **Data Transfer** | 1 GB outbound | $0.00* |
| **Total** | | **~$0.00/month** |


> **Note**: All of the above usage levels fall within the AWS Free Tier limits for the first 12 months for new accounts (and some services remain free beyond that). Actual costs may vary based on region, usage patterns, and video file sizes. Enable AWS Cost Explorer for detailed monitoring. See [AWS Free Tier](https://aws.amazon.com/free/) for details.


**Congratulations!** 🎉 Your Unifi Protect Event Backup API is now deployed and configured. Your alarm events and videos will be automatically backed up to AWS S3 with secure, scalable, and reliable infrastructure.
