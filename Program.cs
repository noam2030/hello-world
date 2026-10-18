using System.Security.Claims;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

var builder = WebApplication.CreateBuilder(args);

var clientId = builder.Configuration["Authentication:Google:ClientId"];
var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    throw new InvalidOperationException(
        "Google OAuth credentials are missing. Set Authentication:Google:ClientId and " +
        "Authentication:Google:ClientSecret via `dotnet user-secrets set ...`. See README.md.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/signin";
        options.LogoutPath = "/signout";
    })
    .AddGoogle(options =>
    {
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
        // Keep access/refresh tokens in the auth cookie so we can call Google APIs later.
        options.SaveTokens = true;
        // Ask for a refresh token so the session survives the 1-hour access token expiry.
        options.AccessType = "offline";
        options.Scope.Add(CalendarService.Scope.CalendarReadonly);
        options.ClaimActions.MapJsonKey("picture", "picture");
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext ctx) =>
{
    var user = ctx.User;
    var body = user.Identity?.IsAuthenticated == true
        ? $"""
          <p>Signed in as <strong>{user.FindFirstValue(ClaimTypes.Name)}</strong>
             ({user.FindFirstValue(ClaimTypes.Email)})</p>
          <ul>
            <li><a href="/me">/me</a> - your Google profile</li>
            <li><a href="/calendar/events">/calendar/events</a> - your next 10 calendar events</li>
            <li><a href="/signout">Sign out</a></li>
          </ul>
          """
        : """<p><a href="/signin">Sign in with Google</a></p>""";

    return Results.Content($"<!doctype html><html><body><h1>hello guest!!</h1>{body}</body></html>",
        "text/html");
});

app.MapGet("/signin", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" },
        [GoogleDefaults.AuthenticationScheme]));

app.MapGet("/signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.MapGet("/me", (ClaimsPrincipal user) => Results.Json(new
{
    id = user.FindFirstValue(ClaimTypes.NameIdentifier),
    name = user.FindFirstValue(ClaimTypes.Name),
    givenName = user.FindFirstValue(ClaimTypes.GivenName),
    familyName = user.FindFirstValue(ClaimTypes.Surname),
    email = user.FindFirstValue(ClaimTypes.Email),
    picture = user.FindFirstValue("picture"),
})).RequireAuthorization();

app.MapGet("/calendar/events", async (HttpContext ctx, CancellationToken ct) =>
{
    var credential = await BuildCredentialAsync(ctx, clientId, clientSecret);
    if (credential is null)
    {
        return Results.Problem("No Google access token in session. Sign out and sign in again.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    using var calendar = new CalendarService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "HelloWorld",
    });

    var request = calendar.Events.List("primary");
    request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow;
    request.MaxResults = 10;
    request.SingleEvents = true;
    request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

    var events = await request.ExecuteAsync(ct);

    return Results.Json(events.Items.Select(e => new
    {
        e.Id,
        e.Summary,
        e.Location,
        Start = e.Start?.DateTimeDateTimeOffset?.ToString("o") ?? e.Start?.Date,
        End = e.End?.DateTimeDateTimeOffset?.ToString("o") ?? e.End?.Date,
        e.HtmlLink,
    }));
}).RequireAuthorization();

app.Run();

// Rebuilds a Google credential from the tokens saved in the auth cookie.
// UserCredential refreshes the access token automatically when it has expired.
static async Task<UserCredential?> BuildCredentialAsync(HttpContext ctx, string clientId, string clientSecret)
{
    var accessToken = await ctx.GetTokenAsync("access_token");
    if (string.IsNullOrEmpty(accessToken))
    {
        return null;
    }

    var refreshToken = await ctx.GetTokenAsync("refresh_token");
    var expiresAt = await ctx.GetTokenAsync("expires_at");
    var expiresIn = DateTimeOffset.TryParse(expiresAt, out var at)
        ? (long)Math.Max(0, (at - DateTimeOffset.UtcNow).TotalSeconds)
        : 0;

    var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
        Scopes = [CalendarService.Scope.CalendarReadonly],
    });

    var token = new TokenResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresInSeconds = expiresIn,
        IssuedUtc = DateTime.UtcNow,
    };

    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "user";
    return new UserCredential(flow, userId, token);
}
