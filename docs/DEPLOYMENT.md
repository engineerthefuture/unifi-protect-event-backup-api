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

## Custom Domain Configuration

The API Gateway can be configured with a custom domain name with automatic SSL certificate creation and DNS setup. Certificate creation is mandatory when using custom domains - the system will automatically handle SSL certificate creation, validation, and DNS configuration.

### Prerequisites

1. **Domain Name**: You must own the domain (e.g., `example.com`)
2. **Route53 Hosted Zone**: Your domain must be managed by Route53

### Configuration

**Required Parameters:**
- `DomainName`: Your desired subdomain (e.g., `api.example.com`)
- `HostedZoneId`: Your Route53 Hosted Zone ID

**Example Configuration:**
```json
{
  "ParameterKey": "DomainName",
   "ParameterValue": "api.example.com"
},
{
  "ParameterKey": "HostedZoneId", 
  "ParameterValue": "Z1D633PJN98FT9"
}
```

### GitHub Actions Environment Variables

For automated deployments, set these repository variables:

**Repository Variables** (Settings > Secrets and variables > Actions > Variables):
- `HOSTED_ZONE_ID`: Your Route53 Hosted Zone ID (e.g., `Z1D633PJN98FT9`)
- `PROD_DOMAIN_NAME`: Production domain (e.g., `api.example.com`)
- `DEV_DOMAIN_NAME`: Development domain (e.g., `api-dev.example.com`)

### Setup Steps

1. **Find Your Hosted Zone ID**:
   ```bash
   aws route53 list-hosted-zones --query "HostedZones[?Name=='example.com.'].Id" --output text
   ```

2. **Set Repository Variables**:
   - Go to your GitHub repository
   - Navigate to Settings > Secrets and variables > Actions > Variables
   - Add the required variables listed above

3. **Deploy**:
   - Push to `main` branch for production deployment
   - Push to any other branch for development deployment
   - The GitHub Actions workflow will automatically use the appropriate domain

### What Gets Automatically Created

The CloudFormation template automatically creates:
- ✅ **SSL Certificate** (in `us-east-1` region with DNS validation)
- ✅ **DNS Validation Records** (for certificate verification)
- ✅ **API Gateway Custom Domain**
- ✅ **Route53 A and AAAA Records** (pointing to the API Gateway)

### Environment-Specific Setup

**Production Environment** (`main` branch):
- Uses `${{ vars.PROD_DOMAIN_NAME }}`
- Example: `api.example.com`
- Endpoints: `https://api.example.com/prod/*`

**Development Environment** (other branches):
- Uses `${{ vars.DEV_DOMAIN_NAME }}`
- Example: `api-dev.example.com`
- Endpoints: `https://api-dev.example.com/dev/*`

### Manual Deployment Example

```bash
aws cloudformation deploy \
  --template-file templates/cf-stack-cs.yaml \
  --stack-name my-api-stack \
  --parameter-overrides \
   DomainName=api.example.com \
    HostedZoneId=Z1D633PJN98FT9 \
    # ... other parameters
```

### Timeline

- **Certificate Creation**: 5-10 minutes (DNS validation)
- **DNS Propagation**: 5-60 minutes globally
- **Total Setup**: Usually complete in 10-15 minutes

### Troubleshooting

**Certificate Validation Issues:**
- Ensure your domain is managed by the specified Route53 Hosted Zone
- Check that the domain name exactly matches the hosted zone
- Verify you have permissions to create Route53 records

**DNS Propagation:**
- DNS changes may take 5-60 minutes to propagate globally
- Use `dig` or `nslookup` to verify DNS resolution:
  ```bash
   dig api.example.com
  ```
