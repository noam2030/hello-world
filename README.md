# HelloWorld (.NET 10) with Google Sign-In

A minimal ASP.NET Core app that lets a user sign in with Google, shows their
profile, and lists their upcoming Google Calendar events.

## 1. Create Google OAuth credentials

1. Open https://console.cloud.google.com/ and create (or pick) a project.
2. **APIs & Services → Library**: enable **Google Calendar API**.
3. **APIs & Services → OAuth consent screen**: configure it (External is fine
   for testing) and add your Google account under **Test users**.
4. **APIs & Services → Credentials → Create credentials → OAuth client ID**:
   - Application type: **Web application**
   - Authorized redirect URI: `https://localhost:5001/signin-google`
5. Copy the **Client ID** and **Client secret**.

## 2. Store the credentials locally (never commit them)

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET"
```

## 3. Run

```bash
dotnet dev-certs https --trust   # first time only
dotnet run
```

Open https://localhost:5001 and click **Sign in with Google**.

## Endpoints

| Route               | Description                                   |
|---------------------|-----------------------------------------------|
| `/`                 | Home page with sign-in / sign-out links       |
| `/signin`           | Starts the Google OAuth flow                  |
| `/signout`          | Clears the session cookie                     |
| `/me`               | JSON profile of the signed-in user            |
| `/calendar/events`  | JSON list of your next 10 calendar events     |

## Deploy to Cloud Run (GitHub Actions)

`.github/workflows/deploy-cloud-run.yml` builds the `Dockerfile`, pushes the
image to Artifact Registry and deploys it to Cloud Run on every push to `main`
(or manually via **Actions → Deploy to Cloud Run → Run workflow**).
It authenticates with **Workload Identity Federation**, so no service-account
JSON key is stored in GitHub.

### One-time GCP setup

```bash
PROJECT_ID=your-project-id
REGION=us-central1
GITHUB_REPO=noam2030/hello-world
PROJECT_NUMBER=$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')
DEPLOYER_SA=github-deployer@$PROJECT_ID.iam.gserviceaccount.com
RUNTIME_SA=hello-world-run@$PROJECT_ID.iam.gserviceaccount.com

gcloud config set project $PROJECT_ID

# APIs
gcloud services enable run.googleapis.com artifactregistry.googleapis.com \
  secretmanager.googleapis.com iamcredentials.googleapis.com sts.googleapis.com

# Docker repository for the images
gcloud artifacts repositories create hello-world \
  --repository-format=docker --location=$REGION

# Google OAuth credentials -> Secret Manager
printf '%s' 'YOUR_CLIENT_ID'     | gcloud secrets create google-oauth-client-id --data-file=-
printf '%s' 'YOUR_CLIENT_SECRET' | gcloud secrets create google-oauth-client-secret --data-file=-

# Runtime service account (what the app runs as) - only needs to read the secrets
gcloud iam service-accounts create hello-world-run
for s in google-oauth-client-id google-oauth-client-secret; do
  gcloud secrets add-iam-policy-binding $s \
    --member="serviceAccount:$RUNTIME_SA" --role=roles/secretmanager.secretAccessor
done

# Deployer service account (what GitHub Actions impersonates)
gcloud iam service-accounts create github-deployer
gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:$DEPLOYER_SA" --role=roles/run.admin
gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:$DEPLOYER_SA" --role=roles/artifactregistry.writer
gcloud iam service-accounts add-iam-policy-binding $RUNTIME_SA \
  --member="serviceAccount:$DEPLOYER_SA" --role=roles/iam.serviceAccountUser

# Workload Identity Federation: only this GitHub repo may impersonate the deployer
gcloud iam workload-identity-pools create github --location=global
gcloud iam workload-identity-pools providers create-oidc hello-world \
  --location=global --workload-identity-pool=github \
  --issuer-uri=https://token.actions.githubusercontent.com \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository" \
  --attribute-condition="assertion.repository == '$GITHUB_REPO'"
gcloud iam service-accounts add-iam-policy-binding $DEPLOYER_SA \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github/attribute.repository/$GITHUB_REPO"
```

### GitHub repository variables

In **Settings → Secrets and variables → Actions → Variables**, add:

| Variable                            | Value                                                                                          |
|-------------------------------------|------------------------------------------------------------------------------------------------|
| `GCP_PROJECT_ID`                    | your project ID                                                                                |
| `GCP_WORKLOAD_IDENTITY_PROVIDER`    | `projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github/providers/hello-world`  |
| `GCP_DEPLOYER_SERVICE_ACCOUNT`      | `github-deployer@PROJECT_ID.iam.gserviceaccount.com`                                           |
| `CLOUD_RUN_RUNTIME_SERVICE_ACCOUNT` | `hello-world-run@PROJECT_ID.iam.gserviceaccount.com`                                           |
| `GCP_REGION` *(optional)*           | defaults to `us-central1`                                                                      |
| `CLOUD_RUN_SERVICE` *(optional)*    | defaults to `hello-world`                                                                      |
| `ARTIFACT_REGISTRY_REPOSITORY` *(optional)* | defaults to `hello-world`                                                              |

### After the first deploy

Add `https://<your-service-url>/signin-google` to the OAuth client's
**Authorized redirect URIs** (the URL is shown in the workflow run summary).
The service is deployed with `--allow-unauthenticated` so browsers can reach
the sign-in page; access to user data still requires Google sign-in.

## Notes

- Scopes requested: `openid`, `profile`, `email`, `calendar.readonly`.
- Tokens are stored in the encrypted auth cookie (`SaveTokens = true`). The
  access token is refreshed automatically using the refresh token when it
  expires.
- To add more Google data (Gmail, Drive, …) add the matching
  `Google.Apis.*` package, add its scope in `Program.cs`, and enable the API in
  Google Cloud Console.
