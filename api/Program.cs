using Api.Endpoints;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddTransient<IPageVisitService>(_ =>
    new PageVisitService(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapPageVisitEndpoints();

app.Run();

public partial class Program { }
