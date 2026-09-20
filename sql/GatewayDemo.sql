CREATE TABLE dbo.GatewayManagedDevices (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DeviceId NVARCHAR(64) NOT NULL,
    DeviceCode NVARCHAR(32) NOT NULL,
    BrowserTokenHash NVARCHAR(128) NULL,
    AppKeyId NVARCHAR(48) NULL,
    ProtectedAppSecret NVARCHAR(300) NULL,
    TrustState NVARCHAR(16) NOT NULL,
    ChallengeReason NVARCHAR(200) NULL,
    SignalHash NVARCHAR(32) NOT NULL,
    UserAgent NVARCHAR(400) NOT NULL,
    RegisteredIp NVARCHAR(64) NOT NULL,
    LastSeenIp NVARCHAR(64) NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    LastSeenAtUtc DATETIME2 NULL,
    ChallengedAtUtc DATETIME2 NULL
);

CREATE UNIQUE INDEX IX_GatewayManagedDevices_DeviceId
    ON dbo.GatewayManagedDevices (DeviceId);

CREATE UNIQUE INDEX IX_GatewayManagedDevices_BrowserTokenHash
    ON dbo.GatewayManagedDevices (BrowserTokenHash)
    WHERE BrowserTokenHash IS NOT NULL;

CREATE UNIQUE INDEX IX_GatewayManagedDevices_AppKeyId
    ON dbo.GatewayManagedDevices (AppKeyId)
    WHERE AppKeyId IS NOT NULL;

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
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL,
    ReviewedAtUtc DATETIME2 NULL
);

CREATE INDEX IX_GatewayAccessRequests_DeviceId_Status
    ON dbo.GatewayAccessRequests (DeviceId, Status);

CREATE TABLE dbo.GatewayAccessRequestSites (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    RequestId UNIQUEIDENTIFIER NOT NULL,
    SiteKey NVARCHAR(32) NOT NULL,
    CONSTRAINT FK_GatewayAccessRequestSites_RequestId
        FOREIGN KEY (RequestId) REFERENCES dbo.GatewayAccessRequests (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IX_GatewayAccessRequestSites_RequestId_SiteKey
    ON dbo.GatewayAccessRequestSites (RequestId, SiteKey);

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
    CreatedAtUtc DATETIME2 NOT NULL,
    ApprovedAtUtc DATETIME2 NOT NULL,
    LastSeenAtUtc DATETIME2 NULL,
    RevokedAtUtc DATETIME2 NULL
);

CREATE INDEX IX_GatewayDeviceAuthorizations_DeviceId_Status
    ON dbo.GatewayDeviceAuthorizations (DeviceId, Status);

CREATE TABLE dbo.GatewayDeviceAuthorizationSites (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    AuthorizationId UNIQUEIDENTIFIER NOT NULL,
    SiteKey NVARCHAR(32) NOT NULL,
    CONSTRAINT FK_GatewayDeviceAuthorizationSites_AuthorizationId
        FOREIGN KEY (AuthorizationId) REFERENCES dbo.GatewayDeviceAuthorizations (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IX_GatewayDeviceAuthorizationSites_AuthorizationId_SiteKey
    ON dbo.GatewayDeviceAuthorizationSites (AuthorizationId, SiteKey);

CREATE TABLE dbo.GatewayAppReplayNonces (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    AppKeyId NVARCHAR(48) NOT NULL,
    NonceHash NVARCHAR(128) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    ExpiresAtUtc DATETIME2 NOT NULL
);

CREATE UNIQUE INDEX IX_GatewayAppReplayNonces_AppKeyId_NonceHash
    ON dbo.GatewayAppReplayNonces (AppKeyId, NonceHash);

CREATE INDEX IX_GatewayAppReplayNonces_ExpiresAtUtc
    ON dbo.GatewayAppReplayNonces (ExpiresAtUtc);

CREATE TABLE dbo.GatewayAuditLogs (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Kind NVARCHAR(40) NOT NULL,
    Message NVARCHAR(200) NOT NULL,
    DeviceId NVARCHAR(64) NOT NULL,
    DeviceCode NVARCHAR(32) NOT NULL,
    SiteKey NVARCHAR(64) NULL,
    ClientIp NVARCHAR(64) NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);

CREATE INDEX IX_GatewayAuditLogs_CreatedAtUtc
    ON dbo.GatewayAuditLogs (CreatedAtUtc DESC);
