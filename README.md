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

## Notes

- Scopes requested: `openid`, `profile`, `email`, `calendar.readonly`.
- Tokens are stored in the encrypted auth cookie (`SaveTokens = true`). The
  access token is refreshed automatically using the refresh token when it
  expires.
- To add more Google data (Gmail, Drive, …) add the matching
  `Google.Apis.*` package, add its scope in `Program.cs`, and enable the API in
  Google Cloud Console.
