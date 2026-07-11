using Api.Services.IntegrationCalls;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Api.Tests.Unit;

public class IntegrationCallRecorderTests
{
    private static IntegrationCallRecord SampleCall(string? requestBody = null, string? responseBody = null) =>
        new(IntegrationCallDirection.Outbound, "salesforce", "trace-1", null,
            "POST", "https://login.salesforce.com/services/oauth2/token",
            200, 42, requestBody, responseBody, null);

    [Fact(DisplayName = "a failing save is swallowed and logged, never thrown")]
    public async Task Record_WhenSaveFails_SwallowsAndLogs()
    {
        var service = Substitute.For<IIntegrationCallService>();
        service.SaveAsync(Arg.Any<IntegrationCallRecord>())
            .Returns<Task>(_ => throw new InvalidOperationException("db down"));
        var logger = new ListLogger();
        var recorder = new IntegrationCallRecorder(service, logger);

        var act = () => recorder.RecordAsync(SampleCall());

        await act.Should().NotThrowAsync();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "bodies are redacted before they reach the persistence service")]
    public async Task Record_RedactsBodiesBeforeSaving()
    {
        IntegrationCallRecord? saved = null;
        var service = Substitute.For<IIntegrationCallService>();
        await service.SaveAsync(Arg.Do<IntegrationCallRecord>(r => saved = r));
        var recorder = new IntegrationCallRecorder(service, new ListLogger());

        await recorder.RecordAsync(SampleCall(
            requestBody: "grant_type=x&assertion=eyJa.eyJb.sig",
            responseBody: """{"access_token":"secret","instance_url":"https://org.example"}"""));

        saved!.RequestBody.Should().Be("grant_type=x&assertion=[REDACTED]");
        saved.ResponseBody.Should().Be("""{"access_token":"[REDACTED]","instance_url":"https://org.example"}""");
    }

    private sealed class ListLogger : ILogger<IntegrationCallRecorder>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
