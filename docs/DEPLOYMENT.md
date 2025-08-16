# Multi-Environment Deployment

This project now supports deploying to multiple environments:

## Environments

- **Production (prod)**: Deploys when pushing to `main` branch
- **Development (dev)**: Deploys when pushing to any other branch (`feature/*`, `bugfix/*`, `hotfix/*`, `develop`)

## Environment Configuration

### Production Environment
- Stack Name: `bf-prod-unifi-protect-event-backup-api`
- S3 Deployment Bucket: `bf-prod-s3-deployments`
- S3 Storage Bucket: `bf-prod-s3-unifi-protect-event-backup-api`
- Lambda Function: `bf-prod-lambda-unifi-protect-event-backup-api`

### Development Environment
- Stack Name: `bf-dev-unifi-protect-event-backup-api`
- S3 Deployment Bucket: `bf-dev-s3-deployments`
- S3 Storage Bucket: `bf-dev-s3-unifi-protect-event-backup-api`
- Lambda Function: `bf-dev-lambda-unifi-protect-event-backup-api`

## Required GitHub Secrets and Variables

### Variables (Repository Settings > Secrets and variables > Actions > Variables)
- `AWS_ACCOUNT_ID`: Your AWS Account ID
- `OIDC_ROLE_NAME`: Name of the OIDC role for GitHub Actions
- `OWNER_NAME`: Owner name for resource tagging
- `APP_NAME`: Application name for resource tagging
- `APP_DESCRIPTION`: Application description for resource tagging

### Secrets (Repository Settings > Secrets and variables > Actions > Secrets)
- `UNIFI_HOST`: Unifi Protect hostname or IP address
- `UNIFI_USERNAME`: Username for Unifi Protect authentication
- `UNIFI_PASSWORD`: Password for Unifi Protect authentication

## Deployment Process

1. **Automatic Deployment**: 
   - Push to `main` branch triggers production deployment
   - Push to any other branch triggers development deployment

2. **Manual Deployment**:
   - Go to Actions tab in GitHub
   - Select "Build, Test, and Deploy CloudFormation Stack for Unifi Webhook Event Receiver"
   - Click "Run workflow" and select the branch to deploy

## Testing

Before deployment, all unit tests must pass. The deployment will be skipped if any tests fail.

## Branch Naming Conventions

Supported development branch patterns:
- `feature/*` - Feature development branches
- `bugfix/*` - Bug fix branches  
- `hotfix/*` - Hotfix branches
- `develop` - Main development branch

Any branch not matching these patterns or `main` will still deploy to dev environment.
