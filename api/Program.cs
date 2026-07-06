using System.Text;
using Api.Endpoints;
using Api.Options;
using Api.Services;
using Api.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddTransient<IPageVisitService>(_ =>
    new PageVisitService(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<OktaOptions>(builder.Configuration.GetSection("Auth:Okta"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IOneTimeCodeStore, MemoryOneTimeCodeStore>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddHttpClient<OktaAuthProvider>();
builder.Services.AddTransient<IExternalAuthProvider>(sp => sp.GetRequiredService<OktaAuthProvider>());
// e2e-only fake IdP: both conditions must hold so it can never exist in production
if (builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Auth:EnableTestProvider"))
{
    builder.Services.AddTransient<IExternalAuthProvider, TestAuthProvider>();
}
builder.Services.AddTransient<IUserService>(_ =>
    new UserService(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty));
builder.Services.AddTransient<IRefreshTokenService>(sp =>
    new RefreshTokenService(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty,
        sp.GetRequiredService<IOptions<AuthOptions>>().Value.RefreshTokenDays));
builder.Services.AddTransient<IAuthFlowService, AuthFlowService>();

var signingKey = builder.Configuration["Jwt:SigningKey"] ?? "";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep raw claim names ("sub", not the SOAP-era URIs)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            // A missing key means protected endpoints 401 rather than the app
            // failing to boot (public endpoints keep working).
            IssuerSigningKey = signingKey.Length > 0
                ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
                : null,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

const string CorsPolicy = "Frontend";
var allowedOrigins = (builder.Configuration["AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthEndpoints();
app.MapPageVisitEndpoints();
app.MapAuthEndpoints();

app.Run();

public partial class Program { }
