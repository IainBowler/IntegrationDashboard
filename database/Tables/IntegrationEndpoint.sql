CREATE TABLE [dbo].[IntegrationEndpoint]
(
    [IntegrationEndpointId] BIGINT        IDENTITY(1,1)  NOT NULL,
    [IntegrationId]         BIGINT                       NOT NULL,
    [Name]                  NVARCHAR(50)                 NOT NULL,
    [Direction]             NVARCHAR(10)                 NOT NULL,
    [CreatedAtUtc]          DATETIME2(3)                 NOT NULL  CONSTRAINT [DF_IntegrationEndpoint_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_IntegrationEndpoint] PRIMARY KEY CLUSTERED ([IntegrationEndpointId] ASC),
    CONSTRAINT [FK_IntegrationEndpoint_Integration] FOREIGN KEY ([IntegrationId]) REFERENCES [dbo].[Integration] ([IntegrationId]),
    CONSTRAINT [UQ_IntegrationEndpoint_IntegrationId_Name] UNIQUE ([IntegrationId], [Name]),
    CONSTRAINT [CK_IntegrationEndpoint_Direction] CHECK ([Direction] IN (N'Inbound', N'Outbound'))
);
