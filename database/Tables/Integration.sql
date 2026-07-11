CREATE TABLE [dbo].[Integration]
(
    [IntegrationId] BIGINT        IDENTITY(1,1)  NOT NULL,
    [Name]          NVARCHAR(50)                 NOT NULL,
    [DisplayName]   NVARCHAR(100)                NOT NULL,
    [CreatedAtUtc]  DATETIME2(3)                 NOT NULL  CONSTRAINT [DF_Integration_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_Integration] PRIMARY KEY CLUSTERED ([IntegrationId] ASC),
    CONSTRAINT [UQ_Integration_Name] UNIQUE ([Name])
);
