using Api.Endpoints;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddTransient<IPageVisitService>(_ =>
    new PageVisitService(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty));

const string CorsPolicy = "Frontend";
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "")
              .AllowAnyMethod()
              .AllowAnyHeader()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);
app.MapHealthEndpoints();
app.MapPageVisitEndpoints();

app.Run();

public partial class Program { }
