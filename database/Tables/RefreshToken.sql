CREATE TABLE [dbo].[RefreshToken]
(
    [RefreshTokenId]      BIGINT        IDENTITY(1,1)  NOT NULL,
    [UserId]              BIGINT                       NOT NULL,
    [TokenHash]           CHAR(64)                     NOT NULL,
    [CreatedAtUtc]        DATETIME2(3)                 NOT NULL  CONSTRAINT [DF_RefreshToken_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
    [ExpiresAtUtc]        DATETIME2(3)                 NOT NULL,
    [RevokedAtUtc]        DATETIME2(3)                 NULL,
    [ReplacedByTokenHash] CHAR(64)                     NULL,

    CONSTRAINT [PK_RefreshToken] PRIMARY KEY CLUSTERED ([RefreshTokenId] ASC),
    CONSTRAINT [FK_RefreshToken_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([UserId]),
    CONSTRAINT [UQ_RefreshToken_TokenHash] UNIQUE ([TokenHash])
);
GO

CREATE NONCLUSTERED INDEX [IX_RefreshToken_UserId]
    ON [dbo].[RefreshToken] ([UserId]);
