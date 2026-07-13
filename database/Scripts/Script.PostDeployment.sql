-- Post-deployment: idempotent seed of integration metadata + backfill of
-- IntegrationCall.IntegrationEndpointId. Runs on EVERY publish (Azure deploy,
-- e2e sqlpackage publish, and the Testcontainers DacFx deploy in api.tests),
-- so it must be re-runnable: seeds are guarded by NOT EXISTS and the backfill
-- only touches NULL rows.

-- Seed integrations
IF NOT EXISTS (SELECT 1 FROM [dbo].[Integration] WHERE [Name] = N'salesforce')
    INSERT INTO [dbo].[Integration] ([Name], [DisplayName]) VALUES (N'salesforce', N'Salesforce');

-- Seed endpoints
INSERT INTO [dbo].[IntegrationEndpoint] ([IntegrationId], [Name], [Direction])
SELECT i.[IntegrationId], s.[Name], s.[Direction]
FROM [dbo].[Integration] i
CROSS APPLY (VALUES
        (N'auth',        N'Inbound'),
        (N'accounts',    N'Inbound'),
        (N'leads',       N'Inbound'),
        (N'token',       N'Outbound'),
        (N'query',       N'Outbound'),
        (N'create-lead', N'Outbound')) AS s([Name], [Direction])
WHERE i.[Name] = N'salesforce'
  AND NOT EXISTS (SELECT 1 FROM [dbo].[IntegrationEndpoint] e
                  WHERE e.[IntegrationId] = i.[IntegrationId] AND e.[Name] = s.[Name]);

-- Backfill audit rows recorded before the endpoint tables existed (or during
-- a deploy gap), matched by URL pattern. NULL rows only => idempotent.
UPDATE c
SET c.[IntegrationEndpointId] = e.[IntegrationEndpointId]
FROM [dbo].[IntegrationCall] c
JOIN [dbo].[Integration] i ON i.[Name] = c.[IntegrationName]
JOIN [dbo].[IntegrationEndpoint] e ON e.[IntegrationId] = i.[IntegrationId]
    AND e.[Direction] = c.[Direction]
    AND (
        (e.[Name] = N'auth'        AND c.[Direction] = N'Inbound'  AND c.[Url] LIKE N'%/api/integrations/salesforce/auth%') OR
        (e.[Name] = N'accounts'    AND c.[Direction] = N'Inbound'  AND c.[Url] LIKE N'%/api/integrations/salesforce/accounts%') OR
        (e.[Name] = N'leads'       AND c.[Direction] = N'Inbound'  AND c.[Url] LIKE N'%/api/integrations/salesforce/leads%') OR
        (e.[Name] = N'token'       AND c.[Direction] = N'Outbound' AND c.[Url] LIKE N'%/services/oauth2/token%') OR
        (e.[Name] = N'query'       AND c.[Direction] = N'Outbound' AND c.[Url] LIKE N'%/services/data/%/query%') OR
        (e.[Name] = N'create-lead' AND c.[Direction] = N'Outbound' AND c.[Url] LIKE N'%/services/data/%/sobjects/Lead%')
    )
WHERE c.[IntegrationEndpointId] IS NULL;
