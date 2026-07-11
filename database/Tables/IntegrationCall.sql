CREATE TABLE [dbo].[IntegrationCall]
(
    [IntegrationCallId] BIGINT         IDENTITY(1,1)  NOT NULL,
    [Direction]         NVARCHAR(10)                  NOT NULL,
    [IntegrationName]   NVARCHAR(50)                  NOT NULL,
    [CorrelationId]     NVARCHAR(64)                  NULL,
    [UserId]            BIGINT                        NULL,
    [Method]            NVARCHAR(10)                  NOT NULL,
    [Url]               NVARCHAR(2048)                NOT NULL,
    [StatusCode]        INT                           NULL,
    [DurationMs]        INT                           NOT NULL,
    [RequestBody]       NVARCHAR(MAX)                 NULL,
    [ResponseBody]      NVARCHAR(MAX)                 NULL,
    [Error]             NVARCHAR(2048)                NULL,
    [CalledAtUtc]       DATETIME2(3)                  NOT NULL  CONSTRAINT [DF_IntegrationCall_CalledAtUtc] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_IntegrationCall] PRIMARY KEY CLUSTERED ([IntegrationCallId] ASC),
    CONSTRAINT [FK_IntegrationCall_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([UserId]),
    CONSTRAINT [CK_IntegrationCall_Direction] CHECK ([Direction] IN (N'Inbound', N'Outbound'))
);
GO

CREATE NONCLUSTERED INDEX [IX_IntegrationCall_CorrelationId]
    ON [dbo].[IntegrationCall] ([CorrelationId]);
GO

CREATE NONCLUSTERED INDEX [IX_IntegrationCall_CalledAtUtc]
    ON [dbo].[IntegrationCall] ([CalledAtUtc]);
