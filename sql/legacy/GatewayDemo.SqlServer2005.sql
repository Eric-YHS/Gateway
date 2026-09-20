IF OBJECT_ID(N'dbo.GatewayAuditLogs', N'U') IS NOT NULL DROP TABLE dbo.GatewayAuditLogs;
IF OBJECT_ID(N'dbo.GatewayAppReplayNonces', N'U') IS NOT NULL DROP TABLE dbo.GatewayAppReplayNonces;
IF OBJECT_ID(N'dbo.GatewayAppCredentials', N'U') IS NOT NULL DROP TABLE dbo.GatewayAppCredentials;
IF OBJECT_ID(N'dbo.GatewayBrowserCredentials', N'U') IS NOT NULL DROP TABLE dbo.GatewayBrowserCredentials;
IF OBJECT_ID(N'dbo.GatewayDeviceAuthorizationSites', N'U') IS NOT NULL DROP TABLE dbo.GatewayDeviceAuthorizationSites;
IF OBJECT_ID(N'dbo.GatewayDeviceAuthorizations', N'U') IS NOT NULL DROP TABLE dbo.GatewayDeviceAuthorizations;
IF OBJECT_ID(N'dbo.GatewayAccessRequestSites', N'U') IS NOT NULL DROP TABLE dbo.GatewayAccessRequestSites;
IF OBJECT_ID(N'dbo.GatewayAccessRequests', N'U') IS NOT NULL DROP TABLE dbo.GatewayAccessRequests;
IF OBJECT_ID(N'dbo.GatewayManagedDevices', N'U') IS NOT NULL DROP TABLE dbo.GatewayManagedDevices;
GO

CREATE TABLE dbo.GatewayManagedDevices (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DeviceId NVARCHAR(64) NOT NULL,
    DeviceCode NVARCHAR(32) NOT NULL,
    TrustState NVARCHAR(16) NOT NULL,
    ChallengeReason NVARCHAR(200) NULL,
    SignalHash NVARCHAR(32) NOT NULL,
    UserAgent NVARCHAR(400) NOT NULL,
    RegisteredIp NVARCHAR(64) NOT NULL,
    LastSeenIp NVARCHAR(64) NULL,
    HasAppCredential BIT NOT NULL CONSTRAINT DF_GatewayManagedDevices_HasAppCredential DEFAULT (0),
    CreatedAtUtc DATETIME NOT NULL,
    LastSeenAtUtc DATETIME NULL,
    ChallengedAtUtc DATETIME NULL
);
GO

CREATE UNIQUE INDEX IX_GatewayManagedDevices_DeviceId
    ON dbo.GatewayManagedDevices (DeviceId);
GO

CREATE TABLE dbo.GatewayBrowserCredentials (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DeviceId NVARCHAR(64) NOT NULL,
    TokenHash NVARCHAR(128) NOT NULL,
    CreatedAtUtc DATETIME NOT NULL
);
GO

CREATE UNIQUE INDEX IX_GatewayBrowserCredentials_DeviceId
    ON dbo.GatewayBrowserCredentials (DeviceId);
GO

CREATE UNIQUE INDEX IX_GatewayBrowserCredentials_TokenHash
    ON dbo.GatewayBrowserCredentials (TokenHash);
GO

CREATE TABLE dbo.GatewayAppCredentials (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DeviceId NVARCHAR(64) NOT NULL,
    CredentialKey NVARCHAR(64) NOT NULL,
    ProtectedSecret NVARCHAR(300) NOT NULL,
    CreatedAtUtc DATETIME NOT NULL,
    LastRotatedAtUtc DATETIME NULL
);
GO

CREATE UNIQUE INDEX IX_GatewayAppCredentials_DeviceId
    ON dbo.GatewayAppCredentials (DeviceId);
GO

CREATE UNIQUE INDEX IX_GatewayAppCredentials_CredentialKey
    ON dbo.GatewayAppCredentials (CredentialKey);
GO

CREATE TABLE dbo.GatewayAccessRequests (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DeviceId NVARCHAR(64) NOT NULL,
    DeviceCode NVARCHAR(32) NOT NULL,
    SignalHash NVARCHAR(32) NOT NULL,
    CompanyName NVARCHAR(100) NOT NULL,
    ApplicantName NVARCHAR(60) NOT NULL,
    Phone NVARCHAR(30) NOT NULL,
    Reason NVARCHAR(200) NULL,
    TargetPath NVARCHAR(260) NOT NULL,
    ClientIp NVARCHAR(64) NOT NULL,
    Status NVARCHAR(16) NOT NULL,
    ReviewNote NVARCHAR(200) NULL,
    CreatedAtUtc DATETIME NOT NULL,
    UpdatedAtUtc DATETIME NOT NULL,
    ReviewedAtUtc DATETIME NULL
);
GO

CREATE INDEX IX_GatewayAccessRequests_DeviceId_Status
    ON dbo.GatewayAccessRequests (DeviceId, Status);
GO

CREATE TABLE dbo.GatewayAccessRequestSites (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    RequestId UNIQUEIDENTIFIER NOT NULL,
    SiteKey NVARCHAR(32) NOT NULL,
    CONSTRAINT FK_GatewayAccessRequestSites_RequestId
        FOREIGN KEY (RequestId) REFERENCES dbo.GatewayAccessRequests (Id) ON DELETE CASCADE
);
GO

CREATE UNIQUE INDEX IX_GatewayAccessRequestSites_RequestId_SiteKey
    ON dbo.GatewayAccessRequestSites (RequestId, SiteKey);
GO

CREATE TABLE dbo.GatewayDeviceAuthorizations (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DeviceId NVARCHAR(64) NOT NULL,
    DeviceCode NVARCHAR(32) NOT NULL,
    CompanyName NVARCHAR(100) NOT NULL,
    ApplicantName NVARCHAR(60) NOT NULL,
    Phone NVARCHAR(30) NOT NULL,
    Status NVARCHAR(16) NOT NULL,
    PolicyMode NVARCHAR(16) NOT NULL,
    ReviewNote NVARCHAR(200) NULL,
    LastSeenIp NVARCHAR(64) NULL,
    LastSeenSiteKey NVARCHAR(32) NULL,
    CreatedAtUtc DATETIME NOT NULL,
    ApprovedAtUtc DATETIME NOT NULL,
    LastSeenAtUtc DATETIME NULL,
    RevokedAtUtc DATETIME NULL
);
GO

CREATE INDEX IX_GatewayDeviceAuthorizations_DeviceId_Status
    ON dbo.GatewayDeviceAuthorizations (DeviceId, Status);
GO

CREATE TABLE dbo.GatewayDeviceAuthorizationSites (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    AuthorizationId UNIQUEIDENTIFIER NOT NULL,
    SiteKey NVARCHAR(32) NOT NULL,
    CONSTRAINT FK_GatewayDeviceAuthorizationSites_AuthorizationId
        FOREIGN KEY (AuthorizationId) REFERENCES dbo.GatewayDeviceAuthorizations (Id) ON DELETE CASCADE
);
GO

CREATE UNIQUE INDEX IX_GatewayDeviceAuthorizationSites_AuthorizationId_SiteKey
    ON dbo.GatewayDeviceAuthorizationSites (AuthorizationId, SiteKey);
GO

CREATE TABLE dbo.GatewayAppReplayNonces (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    CredentialKey NVARCHAR(64) NOT NULL,
    NonceHash NVARCHAR(128) NOT NULL,
    CreatedAtUtc DATETIME NOT NULL,
    ExpiresAtUtc DATETIME NOT NULL
);
GO

CREATE UNIQUE INDEX IX_GatewayAppReplayNonces_CredentialKey_NonceHash
    ON dbo.GatewayAppReplayNonces (CredentialKey, NonceHash);
GO

CREATE INDEX IX_GatewayAppReplayNonces_ExpiresAtUtc
    ON dbo.GatewayAppReplayNonces (ExpiresAtUtc);
GO

CREATE TABLE dbo.GatewayAuditLogs (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Kind NVARCHAR(40) NOT NULL,
    Message NVARCHAR(200) NOT NULL,
    DeviceId NVARCHAR(64) NOT NULL,
    DeviceCode NVARCHAR(32) NOT NULL,
    SiteKey NVARCHAR(64) NULL,
    ClientIp NVARCHAR(64) NULL,
    CreatedAtUtc DATETIME NOT NULL
);
GO

CREATE INDEX IX_GatewayAuditLogs_CreatedAtUtc
    ON dbo.GatewayAuditLogs (CreatedAtUtc);
GO
