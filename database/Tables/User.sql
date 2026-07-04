CREATE TABLE [dbo].[User]
(
    [UserId]            BIGINT         IDENTITY(1,1)  NOT NULL,
    [Provider]          NVARCHAR(50)                  NOT NULL,
    [ExternalSubjectId] NVARCHAR(256)                 NOT NULL,
    [Email]             NVARCHAR(320)                 NULL,
    [DisplayName]       NVARCHAR(256)                 NULL,
    [CreatedAtUtc]      DATETIME2(3)                  NOT NULL  CONSTRAINT [DF_User_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
    [LastLoginAtUtc]    DATETIME2(3)                  NOT NULL  CONSTRAINT [DF_User_LastLoginAtUtc] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([UserId] ASC),
    CONSTRAINT [UQ_User_Provider_ExternalSubjectId] UNIQUE ([Provider], [ExternalSubjectId])
);
