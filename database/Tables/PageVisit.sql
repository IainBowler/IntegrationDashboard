CREATE TABLE [dbo].[PageVisit]
(
    [PageVisitId]  BIGINT         IDENTITY(1,1)  NOT NULL,
    [VisitedAtUtc] DATETIME2(3)                  NOT NULL  CONSTRAINT [DF_PageVisit_VisitedAtUtc] DEFAULT SYSUTCDATETIME(),
    [PagePath]     NVARCHAR(2048)                NOT NULL,

    CONSTRAINT [PK_PageVisit] PRIMARY KEY CLUSTERED ([PageVisitId] ASC)
);
