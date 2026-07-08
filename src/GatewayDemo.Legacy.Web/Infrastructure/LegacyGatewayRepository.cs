using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Repositories;
using GatewayDemo.Legacy.Core.Services;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class LegacyGatewayRepository : IGatewayRepository
    {
        private const int DatabaseWriteRetryCount = 5;
        private static readonly TimeSpan HeartbeatWriteInterval = TimeSpan.FromSeconds(30);
        private static readonly ConcurrentDictionary<string, DateTime> RecentHeartbeatWrites =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly object DatabaseWriteSyncRoot = new object();
        private static readonly object DatabaseFileRepairSyncRoot = new object();
        private static int heartbeatCleanupCounter;
        private static readonly TimeSpan HeartbeatEntryMaxAge = TimeSpan.FromMinutes(5);
        private readonly LegacyGatewayConfiguration configuration;

        public LegacyGatewayRepository(LegacyGatewayConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public GatewayManagedDevice GetManagedDeviceByBrowserTokenHash(string tokenHash)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT d.*
FROM GatewayManagedDevices d
INNER JOIN GatewayBrowserCredentials b ON b.DeviceId = d.DeviceId
WHERE b.TokenHash = @TokenHash
LIMIT 1;";
                command.Parameters.AddWithValue("@TokenHash", tokenHash);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapManagedDevice(reader) : null;
                }
            }
        }

        public GatewayManagedDevice GetManagedDeviceByDeviceId(string deviceId)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayManagedDevices WHERE DeviceId = @DeviceId LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapManagedDevice(reader) : null;
                }
            }
        }

        public GatewayAppCredential GetAppCredentialByDeviceId(string deviceId)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayAppCredentials WHERE DeviceId = @DeviceId LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapAppCredential(reader) : null;
                }
            }
        }

        public GatewayAppCredential GetAppCredentialByKey(string credentialKey)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayAppCredentials WHERE CredentialKey = @CredentialKey LIMIT 1;";
                command.Parameters.AddWithValue("@CredentialKey", credentialKey);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapAppCredential(reader) : null;
                }
            }
        }

        public GatewayManagedDevice GetManagedDeviceByLegacyAppIdentityHash(string identityHash)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT d.*
FROM GatewayManagedDevices d
INNER JOIN GatewayLegacyAppIdentities a ON a.DeviceId = d.DeviceId
WHERE a.IdentityHash = @IdentityHash
LIMIT 1;";
                command.Parameters.AddWithValue("@IdentityHash", identityHash);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapManagedDevice(reader) : null;
                }
            }
        }

        public GatewayManagedDevice GetManagedDeviceByLegacyAppSessionHash(string sessionKeyHash)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT d.*
FROM GatewayManagedDevices d
INNER JOIN GatewayLegacyAppSessions s ON s.DeviceId = d.DeviceId
WHERE s.SessionKeyHash = @SessionKeyHash
LIMIT 1;";
                command.Parameters.AddWithValue("@SessionKeyHash", sessionKeyHash);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapManagedDevice(reader) : null;
                }
            }
        }

        public GatewayManagedDevice CreateLegacyAppManagedDevice(string identityHash, string identitySummary, string signalHash, string userAgent, string clientIp)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            var managedDevice = new GatewayManagedDevice
            {
                Id = Guid.NewGuid(),
                DeviceId = Guid.NewGuid().ToString("N"),
                DeviceCode = BuildDeviceCode(),
                TrustState = GatewayDeviceTrustState.Challenged,
                ChallengeReason = "移动 APP 设备尚未获批，请先在网关后台完成审批。",
                SignalHash = signalHash ?? string.Empty,
                UserAgent = userAgent ?? string.Empty,
                RegisteredIp = clientIp ?? string.Empty,
                LastSeenIp = clientIp ?? string.Empty,
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
                ChallengedAtUtc = now,
                HasAppCredential = false
            };

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayManagedDevices (
    Id, DeviceId, DeviceCode, TrustState, ChallengeReason, SignalHash, UserAgent,
    RegisteredIp, LastSeenIp, HasAppCredential, CreatedAtUtc, LastSeenAtUtc, ChallengedAtUtc
) VALUES (
    @Id, @DeviceId, @DeviceCode, @TrustState, @ChallengeReason, @SignalHash, @UserAgent,
    @RegisteredIp, @LastSeenIp, @HasAppCredential, @CreatedAtUtc, @LastSeenAtUtc, @ChallengedAtUtc
);";
                    BindManagedDevice(command, managedDevice);
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayLegacyAppIdentities (Id, DeviceId, IdentityHash, IdentitySummary, CreatedAtUtc, LastSeenAtUtc)
VALUES (@Id, @DeviceId, @IdentityHash, @IdentitySummary, @CreatedAtUtc, @LastSeenAtUtc);";
                    command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                    command.Parameters.AddWithValue("@DeviceId", managedDevice.DeviceId);
                    command.Parameters.AddWithValue("@IdentityHash", identityHash);
                    command.Parameters.AddWithValue("@IdentitySummary", identitySummary ?? string.Empty);
                    command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    command.ExecuteNonQuery();
                }

                InsertAudit(
                    connection,
                    transaction,
                    "legacy-app-device-created",
                    "新的移动 APP 设备已登记，等待审批放行。",
                    managedDevice.DeviceId,
                    managedDevice.DeviceCode,
                    string.Empty,
                    clientIp,
                    now);

                transaction.Commit();
            }

            return managedDevice;
            });
        }

        public GatewayManagedDevice CreateBrowserManagedDevice(string tokenHash, string signalHash, string userAgent, string clientIp)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            var managedDevice = new GatewayManagedDevice
            {
                Id = Guid.NewGuid(),
                DeviceId = Guid.NewGuid().ToString("N"),
                DeviceCode = BuildDeviceCode(),
                TrustState = GatewayDeviceTrustState.Challenged,
                ChallengeReason = "设备尚未获批，请先提交访问申请。",
                SignalHash = signalHash ?? string.Empty,
                UserAgent = userAgent ?? string.Empty,
                RegisteredIp = clientIp ?? string.Empty,
                LastSeenIp = clientIp ?? string.Empty,
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
                ChallengedAtUtc = now,
                HasAppCredential = false
            };

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayManagedDevices (
    Id, DeviceId, DeviceCode, TrustState, ChallengeReason, SignalHash, UserAgent,
    RegisteredIp, LastSeenIp, HasAppCredential, CreatedAtUtc, LastSeenAtUtc, ChallengedAtUtc
) VALUES (
    @Id, @DeviceId, @DeviceCode, @TrustState, @ChallengeReason, @SignalHash, @UserAgent,
    @RegisteredIp, @LastSeenIp, @HasAppCredential, @CreatedAtUtc, @LastSeenAtUtc, @ChallengedAtUtc
);";
                    BindManagedDevice(command, managedDevice);
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayBrowserCredentials (Id, DeviceId, TokenHash, CreatedAtUtc)
VALUES (@Id, @DeviceId, @TokenHash, @CreatedAtUtc);";
                    command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                    command.Parameters.AddWithValue("@DeviceId", managedDevice.DeviceId);
                    command.Parameters.AddWithValue("@TokenHash", tokenHash);
                    command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                    command.ExecuteNonQuery();
                }

                InsertAudit(
                    connection,
                    transaction,
                    "device-created",
                    "新的浏览器设备已登记，等待审批放行。",
                    managedDevice.DeviceId,
                    managedDevice.DeviceCode,
                    string.Empty,
                    clientIp,
                    now);

                transaction.Commit();
            }

            return managedDevice;
            });
        }

        public void UpdateManagedDeviceObservation(string deviceId, string signalHash, string userAgent, string clientIp, bool requiresReview, string challengeReason)
        {
            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            if (!requiresReview && !ShouldWriteHeartbeat("managed-device:" + deviceId, now))
            {
                return;
            }

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    if (requiresReview)
                    {
                        command.CommandText = @"
UPDATE GatewayManagedDevices
SET SignalHash = @SignalHash,
    UserAgent = @UserAgent,
    LastSeenIp = @LastSeenIp,
    LastSeenAtUtc = @LastSeenAtUtc,
    TrustState = @TrustState,
    ChallengeReason = @ChallengeReason,
    ChallengedAtUtc = @ChallengedAtUtc
WHERE DeviceId = @DeviceId;";
                        command.Parameters.AddWithValue("@TrustState", (int)GatewayDeviceTrustState.Challenged);
                        command.Parameters.AddWithValue("@ChallengeReason", challengeReason ?? string.Empty);
                        command.Parameters.AddWithValue("@ChallengedAtUtc", ToDbDateTime(now));
                    }
                    else
                    {
                        command.CommandText = @"
UPDATE GatewayManagedDevices
SET SignalHash = @SignalHash,
    UserAgent = @UserAgent,
    LastSeenIp = @LastSeenIp,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId;";
                    }

                    command.Parameters.AddWithValue("@SignalHash", signalHash ?? string.Empty);
                    command.Parameters.AddWithValue("@UserAgent", userAgent ?? string.Empty);
                    command.Parameters.AddWithValue("@LastSeenIp", clientIp ?? string.Empty);
                    command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@DeviceId", deviceId);
                    command.ExecuteNonQuery();
                }

                if (requiresReview)
                {
                    var device = GetManagedDevice(connection, transaction, deviceId);
                    if (device != null)
                    {
                        InsertAudit(
                            connection,
                            transaction,
                            "device-challenged",
                            challengeReason,
                            device.DeviceId,
                            device.DeviceCode,
                            string.Empty,
                            clientIp,
                            now);
                    }
                }

                transaction.Commit();
            }
            });
        }

        public bool ProcessExternalDecision(string deviceId)
        {
            using (var readConnection = OpenConnection())
            {
                if (GetLatestUnprocessedDecision(readConnection, null, deviceId) == null)
                {
                    return false;
                }
            }

            return ExecuteDatabaseWrite(delegate
            {
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var request = GetLatestUnprocessedDecision(connection, transaction, deviceId);
                if (request == null)
                {
                    transaction.Commit();
                    return false;
                }

                var now = DateTime.UtcNow;
                if (request.Status == GatewayAccessRequestStatus.Approved)
                {
                    var approveAllSites = request.AllowAllSites;
                    var approvedSiteKeys = approveAllSites
                        ? new List<string>()
                        : request.RequestedSites.Select(functionSite => functionSite.SiteKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    UpsertAuthorization(connection, transaction, request, approvedSiteKeys, approveAllSites, request.ReviewNote, now);
                    PromoteManagedDevice(connection, transaction, request.DeviceId, request.SignalHash, request.ClientIp, now);
                    UpdateRequestReviewAndProcessing(
                        connection,
                        transaction,
                        request.Id,
                        request.ReviewNote,
                        string.IsNullOrWhiteSpace(request.ReviewedBy) ? "external-sync" : request.ReviewedBy,
                        request.ExternalApprovalId,
                        request.ReviewedAtUtc ?? now,
                        now,
                        request.Status);
                    InsertAudit(connection, transaction, "request-approved-external", "已从外部审批表同步批准状态。", request.DeviceId, request.DeviceCode, string.Join(",", approvedSiteKeys), request.ClientIp, now);
                }
                else if (request.Status == GatewayAccessRequestStatus.Rejected)
                {
                    UpdateRequestReviewAndProcessing(
                        connection,
                        transaction,
                        request.Id,
                        string.IsNullOrWhiteSpace(request.ReviewNote) ? "外部审批流程已驳回该申请。" : request.ReviewNote,
                        string.IsNullOrWhiteSpace(request.ReviewedBy) ? "external-sync" : request.ReviewedBy,
                        request.ExternalApprovalId,
                        request.ReviewedAtUtc ?? now,
                        now,
                        request.Status);
                    InsertAudit(connection, transaction, "request-rejected-external", "已从外部审批表同步驳回状态。", request.DeviceId, request.DeviceCode, string.Empty, request.ClientIp, now);
                }

                transaction.Commit();
                return true;
            }
            });
        }

        public Task<GatewayDeviceAuthorization> GetActiveAuthorizationAsync(string deviceId, CancellationToken cancellationToken)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT *
FROM GatewayDeviceAuthorizations
WHERE DeviceId = @DeviceId AND Status = @Status
ORDER BY ApprovedAtUtc DESC
LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.Parameters.AddWithValue("@Status", (int)GatewayAuthorizationStatus.Approved);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return Task.FromResult<GatewayDeviceAuthorization>(null);
                    }

                    var authorization = MapAuthorization(reader);
                    authorization.AuthorizedSites = GetAuthorizationSites(connection, authorization.Id, null);
                    return Task.FromResult(authorization);
                }
            }
        }

        public Task<GatewayAccessRequest> GetLatestRequestAsync(string deviceId, CancellationToken cancellationToken)
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT *
FROM GatewayAccessRequests
WHERE DeviceId = @DeviceId
ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC
LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return Task.FromResult<GatewayAccessRequest>(null);
                    }

                    var request = MapRequest(reader);
                    request.RequestedSites = GetRequestSites(connection, request.Id, null);
                    return Task.FromResult(request);
                }
            }
        }

        public Task<GatewayAccessRequest> CreateOrUpdateRequestAsync(DeviceContext device, string companyName, string applicantName, string phone, string reason, string targetPath, IReadOnlyCollection<string> siteKeys, CancellationToken cancellationToken)
        {
            return CreateOrUpdateRequestAsync(
                device,
                companyName,
                applicantName,
                phone,
                reason,
                targetPath,
                siteKeys,
                false,
                cancellationToken);
        }

        public Task<GatewayAccessRequest> CreateOrUpdateRequestAsync(DeviceContext device, string companyName, string applicantName, string phone, string reason, string targetPath, IReadOnlyCollection<string> siteKeys, bool allowAllSites, CancellationToken cancellationToken)
        {
            return CreateOrUpdateRequestAsync(
                device,
                companyName,
                applicantName,
                phone,
                reason,
                targetPath,
                siteKeys,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                allowAllSites,
                cancellationToken);
        }

        public Task<GatewayAccessRequest> CreateOrUpdateRequestAsync(
            DeviceContext device,
            string companyName,
            string applicantName,
            string phone,
            string reason,
            string targetPath,
            IReadOnlyCollection<string> siteKeys,
            string legacyUrlBefore,
            string legacyCompanyId,
            string legacyUserId,
            string legacyDataCenterId,
            string legacyDeviceImei,
            string legacyMobileUserNum,
            CancellationToken cancellationToken)
        {
            return CreateOrUpdateRequestAsync(
                device,
                companyName,
                applicantName,
                phone,
                reason,
                targetPath,
                siteKeys,
                legacyUrlBefore,
                legacyCompanyId,
                legacyUserId,
                legacyDataCenterId,
                legacyDeviceImei,
                legacyMobileUserNum,
                false,
                cancellationToken);
        }

        public Task<GatewayAccessRequest> CreateOrUpdateRequestAsync(
            DeviceContext device,
            string companyName,
            string applicantName,
            string phone,
            string reason,
            string targetPath,
            IReadOnlyCollection<string> siteKeys,
            string legacyUrlBefore,
            string legacyCompanyId,
            string legacyUserId,
            string legacyDataCenterId,
            string legacyDeviceImei,
            string legacyMobileUserNum,
            bool allowAllSites,
            CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var normalizedSiteKeys = NormalizeSiteKeys(siteKeys);
            var now = DateTime.UtcNow;

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var request = GetPendingRequest(connection, transaction, device.DeviceId);
                if (request == null)
                {
                    request = new GatewayAccessRequest
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = device.DeviceId,
                        DeviceCode = device.DeviceCode,
                        SignalHash = device.SignalHash,
                        CompanyName = companyName ?? string.Empty,
                        ApplicantName = applicantName ?? string.Empty,
                        Phone = phone ?? string.Empty,
                        Reason = reason ?? string.Empty,
                        TargetPath = targetPath ?? string.Empty,
                        ClientIp = device.ClientIp ?? string.Empty,
                        LegacyUrlBefore = legacyUrlBefore ?? string.Empty,
                        LegacyCompanyId = legacyCompanyId ?? string.Empty,
                        LegacyUserId = legacyUserId ?? string.Empty,
                        LegacyDataCenterId = legacyDataCenterId ?? string.Empty,
                        LegacyDeviceImei = legacyDeviceImei ?? string.Empty,
                        LegacyMobileUserNum = legacyMobileUserNum ?? string.Empty,
                        AllowAllSites = allowAllSites,
                        Status = GatewayAccessRequestStatus.Pending,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                    InsertRequest(connection, transaction, request);
                    ReplaceRequestSites(connection, transaction, request.Id, normalizedSiteKeys);
                    InsertAudit(connection, transaction, "request-created", "访问申请已创建。", request.DeviceId, request.DeviceCode, string.Join(",", normalizedSiteKeys), request.ClientIp, now);
                }
                else
                {
                    var mergedSiteKeys = request.AllowAllSites || allowAllSites
                        ? new List<string>()
                        : MergeSiteKeys(
                            request.RequestedSites == null
                                ? Enumerable.Empty<string>()
                                : request.RequestedSites.Select(functionSite => functionSite.SiteKey),
                            normalizedSiteKeys);
                    var preserveLegacyReviewInfo = LegacyReviewMergePolicy.ShouldPreserveExisting(
                        request,
                        applicantName,
                        phone,
                        reason,
                        targetPath,
                        legacyUserId);

                    request.DeviceCode = device.DeviceCode;
                    request.SignalHash = device.SignalHash;
                    request.CompanyName = PreferIncomingValue(request.CompanyName, companyName);
                    request.ApplicantName = preserveLegacyReviewInfo
                        ? PreferExistingReviewValue(request.ApplicantName, applicantName)
                        : applicantName ?? string.Empty;
                    request.Phone = preserveLegacyReviewInfo
                        ? PreferExistingReviewValue(request.Phone, phone)
                        : phone ?? string.Empty;
                    request.Reason = preserveLegacyReviewInfo
                        ? PreferExistingReviewValue(request.Reason, reason)
                        : reason ?? string.Empty;
                    request.TargetPath = preserveLegacyReviewInfo
                        ? PreferExistingReviewValue(request.TargetPath, targetPath)
                        : targetPath ?? string.Empty;
                    request.ClientIp = device.ClientIp ?? string.Empty;
                    request.LegacyUrlBefore = PreferIncomingValue(request.LegacyUrlBefore, legacyUrlBefore);
                    request.LegacyCompanyId = PreferIncomingValue(request.LegacyCompanyId, legacyCompanyId);
                    request.LegacyUserId = PreferIncomingValue(request.LegacyUserId, legacyUserId);
                    request.LegacyDataCenterId = PreferIncomingValue(request.LegacyDataCenterId, legacyDataCenterId);
                    request.LegacyDeviceImei = PreferIncomingValue(request.LegacyDeviceImei, legacyDeviceImei);
                    request.LegacyMobileUserNum = PreferIncomingValue(request.LegacyMobileUserNum, legacyMobileUserNum);
                    request.AllowAllSites = request.AllowAllSites || allowAllSites;
                    request.Status = GatewayAccessRequestStatus.Pending;
                    request.ReviewNote = string.Empty;
                    request.ReviewedBy = string.Empty;
                    request.ExternalApprovalId = string.Empty;
                    request.UpdatedAtUtc = now;
                    request.ReviewedAtUtc = null;
                    request.ProcessedAtUtc = null;

                    UpdateRequest(connection, transaction, request);
                    ReplaceRequestSites(connection, transaction, request.Id, mergedSiteKeys);
                    InsertAudit(connection, transaction, "request-updated", "待审核访问申请已更新。", request.DeviceId, request.DeviceCode, string.Join(",", mergedSiteKeys), request.ClientIp, now);
                }

                request.RequestedSites = GetRequestSites(connection, request.Id, transaction);
                transaction.Commit();
                return Task.FromResult(request);
            }
            });
        }

        private static string PreferIncomingValue(string existingValue, string incomingValue)
        {
            return string.IsNullOrWhiteSpace(incomingValue)
                ? existingValue ?? string.Empty
                : incomingValue.Trim();
        }

        private static string PreferExistingReviewValue(string existingValue, string incomingValue)
        {
            return string.IsNullOrWhiteSpace(existingValue)
                ? incomingValue ?? string.Empty
                : existingValue;
        }

        public Task ApproveRequestAsync(Guid requestId, IReadOnlyCollection<string> siteKeys, string note, CancellationToken cancellationToken)
        {
            return ApproveRequestAsync(requestId, siteKeys, false, note, cancellationToken);
        }

        public Task ApproveRequestAsync(Guid requestId, IReadOnlyCollection<string> siteKeys, bool allowAllSites, string note, CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var request = GetRequest(connection, transaction, requestId);
                if (request == null)
                {
                    throw new InvalidOperationException("未找到对应的申请记录。");
                }

                var approveAllSites = allowAllSites;
                var approvedSiteKeys = approveAllSites ? new List<string>() : NormalizeSiteKeys(siteKeys);
                if (!approveAllSites && approvedSiteKeys.Count == 0)
                {
                    approvedSiteKeys = request.RequestedSites.Select(functionSite => functionSite.SiteKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                if (!approveAllSites && approvedSiteKeys.Count == 0)
                {
                    throw new InvalidOperationException("请至少选择一个授权站点。");
                }

                request.AllowAllSites = approveAllSites;
                UpdateRequestSitePolicy(connection, transaction, request.Id, approveAllSites, now);
                ReplaceRequestSites(connection, transaction, request.Id, approvedSiteKeys);
                request.RequestedSites = GetRequestSites(connection, request.Id, transaction);
                UpdateRequestReviewAndProcessing(connection, transaction, request.Id, note, "admin", request.ExternalApprovalId, now, now, GatewayAccessRequestStatus.Approved);
                UpsertAuthorization(connection, transaction, request, approvedSiteKeys, approveAllSites, note, now);
                PromoteManagedDevice(connection, transaction, request.DeviceId, request.SignalHash, request.ClientIp, now);
                InsertAudit(connection, transaction, "request-approved", "管理员已批准访问申请。", request.DeviceId, request.DeviceCode, string.Join(",", approvedSiteKeys), request.ClientIp, now);

                transaction.Commit();
                return Task.CompletedTask;
            }
            });
        }

        public Task RejectRequestAsync(Guid requestId, string note, CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var request = GetRequest(connection, transaction, requestId);
                if (request == null)
                {
                    throw new InvalidOperationException("未找到对应的申请记录。");
                }

                UpdateRequestReviewAndProcessing(connection, transaction, request.Id, note, "admin", request.ExternalApprovalId, now, now, GatewayAccessRequestStatus.Rejected);
                InsertAudit(connection, transaction, "request-rejected", "管理员已驳回访问申请。", request.DeviceId, request.DeviceCode, string.Empty, request.ClientIp, now);
                transaction.Commit();
                return Task.CompletedTask;
            }
            });
        }

        public GatewayDeviceAuthorization GetAuthorizationById(Guid authorizationId)
        {
            using (var connection = OpenConnection())
            {
                return GetAuthorization(connection, null, authorizationId);
            }
        }

        public void SaveAppCredential(string deviceId, string credentialKey, string protectedSecret)
        {
            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var current = GetAppCredential(connection, transaction, deviceId);
                if (current == null)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
INSERT INTO GatewayAppCredentials (
    Id, DeviceId, CredentialKey, ProtectedSecret, CreatedAtUtc, LastRotatedAtUtc
) VALUES (
    @Id, @DeviceId, @CredentialKey, @ProtectedSecret, @CreatedAtUtc, @LastRotatedAtUtc
);";
                        command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                        command.Parameters.AddWithValue("@DeviceId", deviceId);
                        command.Parameters.AddWithValue("@CredentialKey", credentialKey);
                        command.Parameters.AddWithValue("@ProtectedSecret", protectedSecret);
                        command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                        command.Parameters.AddWithValue("@LastRotatedAtUtc", DBNull.Value);
                        command.ExecuteNonQuery();
                    }
                }
                else
                {
                    DeleteReplayNonces(connection, transaction, current.CredentialKey);
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
UPDATE GatewayAppCredentials
SET CredentialKey = @CredentialKey,
    ProtectedSecret = @ProtectedSecret,
    LastRotatedAtUtc = @LastRotatedAtUtc
WHERE DeviceId = @DeviceId;";
                        command.Parameters.AddWithValue("@CredentialKey", credentialKey);
                        command.Parameters.AddWithValue("@ProtectedSecret", protectedSecret);
                        command.Parameters.AddWithValue("@LastRotatedAtUtc", ToDbDateTime(now));
                        command.Parameters.AddWithValue("@DeviceId", deviceId);
                        command.ExecuteNonQuery();
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE GatewayManagedDevices
SET HasAppCredential = 1,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId;";
                    command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@DeviceId", deviceId);
                    command.ExecuteNonQuery();
                }

                var device = GetManagedDevice(connection, transaction, deviceId);
                if (device != null)
                {
                    InsertAudit(
                        connection,
                        transaction,
                        "app-credential-issued",
                        "已签发或轮换 APP 设备凭据。",
                        device.DeviceId,
                        device.DeviceCode,
                        string.Empty,
                        device.LastSeenIp,
                        now);
                }

                transaction.Commit();
            }
            });
        }

        public bool TryConsumeAppNonce(string credentialKey, string nonceHash, DateTime now, DateTime expiresAtUtc)
        {
            return ExecuteDatabaseWrite(delegate
            {
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                DeleteExpiredReplayNonces(connection, transaction, now);
                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
INSERT INTO GatewayAppReplayNonces (
    Id, CredentialKey, NonceHash, CreatedAtUtc, ExpiresAtUtc
) VALUES (
    @Id, @CredentialKey, @NonceHash, @CreatedAtUtc, @ExpiresAtUtc
);";
                        command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                        command.Parameters.AddWithValue("@CredentialKey", credentialKey);
                        command.Parameters.AddWithValue("@NonceHash", nonceHash);
                        command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                        command.Parameters.AddWithValue("@ExpiresAtUtc", ToDbDateTime(expiresAtUtc));
                        command.ExecuteNonQuery();
                    }
                }
                catch (SQLiteException ex)
                {
                    transaction.Rollback();
                    if (ex.ResultCode == SQLiteErrorCode.Constraint
                        || ex.ResultCode == SQLiteErrorCode.Constraint_PrimaryKey
                        || ex.ResultCode == SQLiteErrorCode.Constraint_Unique)
                    {
                        return false;
                    }

                    throw;
                }

                transaction.Commit();
                return true;
            }
            });
        }

        public void TouchManagedDevice(string deviceId, string userAgent, string clientIp)
        {
            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            if (!ShouldWriteHeartbeat("managed-device:" + deviceId, now))
            {
                return;
            }

            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE GatewayManagedDevices
SET UserAgent = @UserAgent,
    LastSeenIp = @LastSeenIp,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId;";
                command.Parameters.AddWithValue("@UserAgent", userAgent ?? string.Empty);
                command.Parameters.AddWithValue("@LastSeenIp", clientIp ?? string.Empty);
                command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.ExecuteNonQuery();
            }
            });
        }

        public void TouchLegacyAppIdentity(string deviceId, string identityHash, string identitySummary)
        {
            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            if (!ShouldWriteHeartbeat("legacy-app-identity:" + deviceId, now))
            {
                return;
            }

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE GatewayLegacyAppIdentities
SET IdentitySummary = @IdentitySummary,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId
  AND IdentityHash = @IdentityHash;";
                    command.Parameters.AddWithValue("@IdentitySummary", identitySummary ?? string.Empty);
                    command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@DeviceId", deviceId);
                    command.Parameters.AddWithValue("@IdentityHash", identityHash ?? string.Empty);
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            });
        }

        public void AttachLegacyAppIdentity(string deviceId, string identityHash, string identitySummary)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(identityHash))
            {
                return;
            }

            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var deleteCommand = connection.CreateCommand())
                {
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText = @"
DELETE FROM GatewayLegacyAppIdentities
WHERE IdentityHash = @IdentityHash;";
                    deleteCommand.Parameters.AddWithValue("@IdentityHash", identityHash);
                    deleteCommand.ExecuteNonQuery();
                }

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = @"
INSERT INTO GatewayLegacyAppIdentities (
    Id, DeviceId, IdentityHash, IdentitySummary, CreatedAtUtc, LastSeenAtUtc
) VALUES (
    @Id, @DeviceId, @IdentityHash, @IdentitySummary, @CreatedAtUtc, @LastSeenAtUtc
);";
                    insertCommand.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                    insertCommand.Parameters.AddWithValue("@DeviceId", deviceId);
                    insertCommand.Parameters.AddWithValue("@IdentityHash", identityHash);
                    insertCommand.Parameters.AddWithValue("@IdentitySummary", identitySummary ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                    insertCommand.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    insertCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            });
        }

        public void AttachLegacyAppIdentities(string deviceId, IEnumerable<string> identityHashes, string identitySummary)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || identityHashes == null)
            {
                return;
            }

            var normalizedHashes = NormalizeIdentityHashes(identityHashes);
            if (normalizedHashes.Count == 0)
            {
                return;
            }

            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var identityHash in normalizedHashes)
                {
                    using (var deleteCommand = connection.CreateCommand())
                    {
                        deleteCommand.Transaction = transaction;
                        deleteCommand.CommandText = @"
DELETE FROM GatewayLegacyAppIdentities
WHERE IdentityHash = @IdentityHash;";
                        deleteCommand.Parameters.AddWithValue("@IdentityHash", identityHash);
                        deleteCommand.ExecuteNonQuery();
                    }

                    using (var insertCommand = connection.CreateCommand())
                    {
                        insertCommand.Transaction = transaction;
                        insertCommand.CommandText = @"
INSERT INTO GatewayLegacyAppIdentities (
    Id, DeviceId, IdentityHash, IdentitySummary, CreatedAtUtc, LastSeenAtUtc
) VALUES (
    @Id, @DeviceId, @IdentityHash, @IdentitySummary, @CreatedAtUtc, @LastSeenAtUtc
);";
                        insertCommand.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                        insertCommand.Parameters.AddWithValue("@DeviceId", deviceId);
                        insertCommand.Parameters.AddWithValue("@IdentityHash", identityHash);
                        insertCommand.Parameters.AddWithValue("@IdentitySummary", identitySummary ?? string.Empty);
                        insertCommand.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                        insertCommand.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                        insertCommand.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
            });
        }

        public void SaveLegacyAppSession(string deviceId, string sessionKeyHash, string sessionPreview)
        {
            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var deleteCommand = connection.CreateCommand())
                {
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText = "DELETE FROM GatewayLegacyAppSessions WHERE SessionKeyHash = @SessionKeyHash OR DeviceId = @DeviceId;";
                    deleteCommand.Parameters.AddWithValue("@SessionKeyHash", sessionKeyHash ?? string.Empty);
                    deleteCommand.Parameters.AddWithValue("@DeviceId", deviceId);
                    deleteCommand.ExecuteNonQuery();
                }

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = @"
INSERT INTO GatewayLegacyAppSessions (
    Id, DeviceId, SessionKeyHash, SessionPreview, CreatedAtUtc, LastSeenAtUtc
) VALUES (
    @Id, @DeviceId, @SessionKeyHash, @SessionPreview, @CreatedAtUtc, @LastSeenAtUtc
);";
                    insertCommand.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                    insertCommand.Parameters.AddWithValue("@DeviceId", deviceId);
                    insertCommand.Parameters.AddWithValue("@SessionKeyHash", sessionKeyHash ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("@SessionPreview", sessionPreview ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(now));
                    insertCommand.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    insertCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            });
        }

        public void TouchLegacyAppSession(string sessionKeyHash)
        {
            ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            if (!ShouldWriteHeartbeat("legacy-app-session:" + sessionKeyHash, now))
            {
                return;
            }

            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE GatewayLegacyAppSessions
SET LastSeenAtUtc = @LastSeenAtUtc
WHERE SessionKeyHash = @SessionKeyHash;";
                command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                command.Parameters.AddWithValue("@SessionKeyHash", sessionKeyHash ?? string.Empty);
                command.ExecuteNonQuery();
            }
            });
        }

        public Task UpdateAuthorizationSitesAsync(Guid authorizationId, IReadOnlyCollection<string> siteKeys, bool allowAllSites, string note, CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var authorization = GetAuthorization(connection, transaction, authorizationId);
                if (authorization == null)
                {
                    throw new InvalidOperationException("Authorization record was not found.");
                }

                var normalizedSiteKeys = allowAllSites ? new List<string>() : NormalizeSiteKeys(siteKeys);
                if (!allowAllSites && normalizedSiteKeys.Count == 0)
                {
                    normalizedSiteKeys = authorization.AuthorizedSites
                        .Select(functionSite => functionSite.SiteKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (!allowAllSites && normalizedSiteKeys.Count == 0)
                {
                    throw new InvalidOperationException("请至少选择一个授权站点。");
                }

                authorization.PolicyMode = allowAllSites
                    ? GatewayDeviceAuthorization.AllSitesPolicyMode
                    : GatewayDeviceAuthorization.DevicePolicyMode;
                authorization.ReviewNote = note ?? string.Empty;
                authorization.Status = GatewayAuthorizationStatus.Approved;
                authorization.ApprovedAtUtc = now;
                authorization.RevokedAtUtc = null;

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE GatewayDeviceAuthorizations
SET Status = @Status,
    PolicyMode = @PolicyMode,
    ReviewNote = @ReviewNote,
    ApprovedAtUtc = @ApprovedAtUtc,
    RevokedAtUtc = @RevokedAtUtc
WHERE Id = @Id;";
                    command.Parameters.AddWithValue("@Status", (int)authorization.Status);
                    command.Parameters.AddWithValue("@PolicyMode", authorization.PolicyMode);
                    command.Parameters.AddWithValue("@ReviewNote", authorization.ReviewNote);
                    command.Parameters.AddWithValue("@ApprovedAtUtc", ToDbDateTime(authorization.ApprovedAtUtc));
                    command.Parameters.AddWithValue("@RevokedAtUtc", DBNull.Value);
                    command.Parameters.AddWithValue("@Id", authorizationId.ToString("D"));
                    command.ExecuteNonQuery();
                }

                ReplaceAuthorizationSites(connection, transaction, authorization.Id, normalizedSiteKeys);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE GatewayManagedDevices
SET TrustState = @TrustState,
    ChallengeReason = @ChallengeReason,
    ChallengedAtUtc = NULL,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId;";
                    command.Parameters.AddWithValue("@TrustState", (int)GatewayDeviceTrustState.Trusted);
                    command.Parameters.AddWithValue("@ChallengeReason", string.Empty);
                    command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@DeviceId", authorization.DeviceId);
                    command.ExecuteNonQuery();
                }

                InsertAudit(connection, transaction, "authorization-sites-updated", "管理员已更新授权站点。", authorization.DeviceId, authorization.DeviceCode, allowAllSites ? "*" : string.Join(",", normalizedSiteKeys), authorization.LastSeenIp, now);
                transaction.Commit();
                return Task.CompletedTask;
            }
            });
        }

        public Task RevokeAuthorizationAsync(Guid authorizationId, string note, CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                var authorization = GetAuthorization(connection, transaction, authorizationId);
                if (authorization == null)
                {
                    throw new InvalidOperationException("未找到对应的授权记录。");
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE GatewayDeviceAuthorizations
SET Status = @Status,
    ReviewNote = @ReviewNote,
    RevokedAtUtc = @RevokedAtUtc
WHERE Id = @Id;";
                    command.Parameters.AddWithValue("@Status", (int)GatewayAuthorizationStatus.Revoked);
                    command.Parameters.AddWithValue("@ReviewNote", note ?? string.Empty);
                    command.Parameters.AddWithValue("@RevokedAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@Id", authorizationId.ToString("D"));
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE GatewayManagedDevices
SET TrustState = @TrustState,
    ChallengeReason = @ChallengeReason,
    ChallengedAtUtc = @ChallengedAtUtc,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId;";
                    command.Parameters.AddWithValue("@TrustState", (int)GatewayDeviceTrustState.Challenged);
                    command.Parameters.AddWithValue("@ChallengeReason", "设备授权已撤销，需要重新发起审核。");
                    command.Parameters.AddWithValue("@ChallengedAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                    command.Parameters.AddWithValue("@DeviceId", authorization.DeviceId);
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE GatewayManagedDevices SET HasAppCredential = 0 WHERE DeviceId = @DeviceId;";
                    command.Parameters.AddWithValue("@DeviceId", authorization.DeviceId);
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM GatewayLegacyAppSessions WHERE DeviceId = @DeviceId;";
                    command.Parameters.AddWithValue("@DeviceId", authorization.DeviceId);
                    command.ExecuteNonQuery();
                }

                var appCredential = GetAppCredential(connection, transaction, authorization.DeviceId);
                if (appCredential != null)
                {
                    DeleteReplayNonces(connection, transaction, appCredential.CredentialKey);
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "DELETE FROM GatewayAppCredentials WHERE DeviceId = @DeviceId;";
                        command.Parameters.AddWithValue("@DeviceId", authorization.DeviceId);
                        command.ExecuteNonQuery();
                    }
                }

                InsertAudit(connection, transaction, "authorization-revoked", "Device authorization revoked by admin.", authorization.DeviceId, authorization.DeviceCode, authorization.LastSeenSiteKey, authorization.LastSeenIp, now);
                transaction.Commit();
                return Task.CompletedTask;
            }
            });
        }

        public Task TouchAuthorizationAsync(string deviceId, string clientIp, string siteKey, CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            var now = DateTime.UtcNow;
            if (!ShouldWriteHeartbeat("authorization:" + deviceId + ":" + siteKey, now))
            {
                return Task.CompletedTask;
            }

            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE GatewayDeviceAuthorizations
SET LastSeenIp = @LastSeenIp,
    LastSeenSiteKey = @LastSeenSiteKey,
    LastSeenAtUtc = @LastSeenAtUtc
WHERE DeviceId = @DeviceId AND Status = @Status;";
                command.Parameters.AddWithValue("@LastSeenIp", clientIp ?? string.Empty);
                command.Parameters.AddWithValue("@LastSeenSiteKey", siteKey ?? string.Empty);
                command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.Parameters.AddWithValue("@Status", (int)GatewayAuthorizationStatus.Approved);
                command.ExecuteNonQuery();
            }

            return Task.CompletedTask;
            });
        }

        public Task RecordAuditAsync(string kind, string message, DeviceContext device, string siteKey, string clientIp, CancellationToken cancellationToken)
        {
            return ExecuteDatabaseWrite(delegate
            {
            using (var connection = OpenConnection())
            {
                InsertAudit(connection, null, kind, message, device.DeviceId, device.DeviceCode, siteKey, clientIp, DateTime.UtcNow);
            }

            return Task.CompletedTask;
            });
        }

        public Task<GatewayDashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken)
        {
            var snapshot = new GatewayDashboardSnapshot();
            using (var connection = OpenConnection())
            {
                foreach (var request in GetAllRequests(connection))
                {
                    snapshot.Requests.Add(request);
                }

                foreach (var authorization in GetAllAuthorizations(connection))
                {
                    snapshot.Authorizations.Add(authorization);
                }

                foreach (var device in GetAllManagedDevices(connection))
                {
                    snapshot.Devices[device.DeviceId] = device;
                }

                foreach (var auditLog in GetAllAuditLogs(connection).Take(120))
                {
                    snapshot.AuditLogs.Add(auditLog);
                }
            }

            return Task.FromResult(snapshot);
        }

        private SQLiteConnection OpenConnection()
        {
            EnsureDatabaseFileExists();
            var connection = new SQLiteConnection(configuration.Storage.SqliteConnectionString);
            connection.Open();
            SQLiteConnectionConfigurator.ConfigureOpenConnection(connection);
            return connection;
        }

        // GatewayDatabaseInitializer.EnsureCreated 只会在 LegacyGatewayRuntime 的惰性单例
        // 构造函数里跑一次（每个 AppDomain 生命周期仅一次）。如果运维/测试人员在应用池不重启的
        // 情况下手动删除了 App_Data 下的 SQLite 主库文件（例如用 scripts\legacy\reset-gateway-db.ps1
        // 重置演示数据这个已被记录的标准操作），后续 OpenConnection 只是对着一个不存在的路径
        // new SQLiteConnection(...).Open()：由于连接串未显式设置 FailIfMissing=True（其默认值为
        // false），System.Data.SQLite 会在 Open() 时静默地在该路径新建一个空的、不含任何表结构的
        // SQLite 文件，而不是抛异常——此后所有查询都会因为"no such table"而失败，且不会被自动修复，
        // 直到应用池下一次回收（重新触发 LegacyGatewayRuntime 单例构造 -> EnsureCreated）为止。
        // 这里在每次打开连接前做一次廉价的文件存在性检查，一旦发现主库文件缺失就立即（幂等地）
        // 重新执行建表脚本，使"删除数据库文件"在应用池不重启的情况下也能立刻自愈，不需要像过去
        // 那样重复删除两次、恰好赶上一次应用池回收才能恢复。
        private void EnsureDatabaseFileExists()
        {
            if (File.Exists(configuration.DatabaseFilePath))
            {
                return;
            }

            lock (DatabaseFileRepairSyncRoot)
            {
                if (File.Exists(configuration.DatabaseFilePath))
                {
                    return;
                }

                TryDeleteStaleSidecarFile(configuration.DatabaseFilePath + "-wal");
                TryDeleteStaleSidecarFile(configuration.DatabaseFilePath + "-shm");
                SQLiteConnection.ClearAllPools();
                GatewayDatabaseInitializer.EnsureCreated(configuration);
            }
        }

        // 主库文件缺失时，残留的 -wal/-shm 影子文件（例如运维只删了主文件、或者 SQLite 连接池里
        // 仍持有旧文件句柄导致的残留)可能会在 WAL 模式下让 SQLite 对新建的空主库产生困惑；一并
        // 清理后再重建，避免"新库套旧影子文件"这种更隐蔽的状态。
        private static void TryDeleteStaleSidecarFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ExecuteDatabaseWrite(Action action)
        {
            ExecuteDatabaseWrite<object>(delegate
            {
                action();
                return null;
            });
        }

        private static T ExecuteDatabaseWrite<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    lock (DatabaseWriteSyncRoot)
                    {
                        return action();
                    }
                }
                catch (SQLiteException ex)
                {
                    if (!IsDatabaseLocked(ex) || attempt >= DatabaseWriteRetryCount)
                    {
                        throw;
                    }

                    Thread.Sleep(GetRetryDelay(attempt));
                }
            }
        }

        private static bool IsDatabaseLocked(SQLiteException ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked)
            {
                return true;
            }

            var message = ex.Message ?? string.Empty;
            return message.IndexOf("database is locked", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("database table is locked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TimeSpan GetRetryDelay(int attempt)
        {
            var milliseconds = 50 * attempt * attempt;
            return TimeSpan.FromMilliseconds(milliseconds > 1000 ? 1000 : milliseconds);
        }

        private static bool ShouldWriteHeartbeat(string key, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            // Periodic cleanup of stale entries to prevent unbounded memory growth.
            if (Interlocked.Increment(ref heartbeatCleanupCounter) % 1000 == 0)
            {
                var cutoff = now - HeartbeatEntryMaxAge;
                foreach (var kvp in RecentHeartbeatWrites)
                {
                    if (kvp.Value < cutoff)
                    {
                        DateTime removed;
                        RecentHeartbeatWrites.TryRemove(kvp.Key, out removed);
                    }
                }
            }

            DateTime lastWrite;
            if (RecentHeartbeatWrites.TryGetValue(key, out lastWrite)
                && now - lastWrite < HeartbeatWriteInterval)
            {
                return false;
            }

            RecentHeartbeatWrites[key] = now;
            return true;
        }

        private static GatewayManagedDevice MapManagedDevice(IDataRecord record)
        {
            return new GatewayManagedDevice
            {
                Id = Guid.Parse(Convert.ToString(record["Id"])),
                DeviceId = Convert.ToString(record["DeviceId"]) ?? string.Empty,
                DeviceCode = Convert.ToString(record["DeviceCode"]) ?? string.Empty,
                TrustState = (GatewayDeviceTrustState)Convert.ToInt32(record["TrustState"]),
                ChallengeReason = Convert.ToString(record["ChallengeReason"]) ?? string.Empty,
                SignalHash = Convert.ToString(record["SignalHash"]) ?? string.Empty,
                UserAgent = Convert.ToString(record["UserAgent"]) ?? string.Empty,
                RegisteredIp = Convert.ToString(record["RegisteredIp"]) ?? string.Empty,
                LastSeenIp = Convert.ToString(record["LastSeenIp"]) ?? string.Empty,
                HasAppCredential = Convert.ToInt32(record["HasAppCredential"]) == 1,
                CreatedAtUtc = ParseDbDateTime(record["CreatedAtUtc"]),
                LastSeenAtUtc = ParseNullableDbDateTime(record["LastSeenAtUtc"]),
                ChallengedAtUtc = ParseNullableDbDateTime(record["ChallengedAtUtc"])
            };
        }

        private static GatewayAppCredential MapAppCredential(IDataRecord record)
        {
            return new GatewayAppCredential
            {
                Id = Guid.Parse(Convert.ToString(record["Id"])),
                DeviceId = Convert.ToString(record["DeviceId"]) ?? string.Empty,
                CredentialKey = Convert.ToString(record["CredentialKey"]) ?? string.Empty,
                ProtectedSecret = Convert.ToString(record["ProtectedSecret"]) ?? string.Empty,
                CreatedAtUtc = ParseDbDateTime(record["CreatedAtUtc"]),
                LastRotatedAtUtc = ParseNullableDbDateTime(record["LastRotatedAtUtc"])
            };
        }

        private static void BindManagedDevice(SQLiteCommand command, GatewayManagedDevice device)
        {
            command.Parameters.AddWithValue("@Id", device.Id.ToString("D"));
            command.Parameters.AddWithValue("@DeviceId", device.DeviceId);
            command.Parameters.AddWithValue("@DeviceCode", device.DeviceCode);
            command.Parameters.AddWithValue("@TrustState", (int)device.TrustState);
            command.Parameters.AddWithValue("@ChallengeReason", device.ChallengeReason ?? string.Empty);
            command.Parameters.AddWithValue("@SignalHash", device.SignalHash ?? string.Empty);
            command.Parameters.AddWithValue("@UserAgent", device.UserAgent ?? string.Empty);
            command.Parameters.AddWithValue("@RegisteredIp", device.RegisteredIp ?? string.Empty);
            command.Parameters.AddWithValue("@LastSeenIp", device.LastSeenIp ?? string.Empty);
            command.Parameters.AddWithValue("@HasAppCredential", device.HasAppCredential ? 1 : 0);
            command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(device.CreatedAtUtc));
            command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(device.LastSeenAtUtc ?? device.CreatedAtUtc));
            command.Parameters.AddWithValue("@ChallengedAtUtc", device.ChallengedAtUtc.HasValue
                ? (object)ToDbDateTime(device.ChallengedAtUtc.Value)
                : DBNull.Value);
        }

        private static string BuildDeviceCode()
        {
            return "DEV-" + DateTime.UtcNow.ToString("yyMMdd") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        private static string ToDbDateTime(DateTime value)
        {
            return value.ToUniversalTime().ToString("o");
        }

        private static DateTime ParseDbDateTime(object value)
        {
            return DateTime.Parse(Convert.ToString(value)).ToUniversalTime();
        }

        private static DateTime? ParseNullableDbDateTime(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            var text = Convert.ToString(value);
            return string.IsNullOrWhiteSpace(text) ? (DateTime?)null : DateTime.Parse(text).ToUniversalTime();
        }

        private static void InsertAudit(SQLiteConnection connection, SQLiteTransaction transaction, string kind, string message, string deviceId, string deviceCode, string siteKey, string clientIp, DateTime createdAtUtc)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO GatewayAuditLogs (
    Id, Kind, Message, DeviceId, DeviceCode, SiteKey, ClientIp, CreatedAtUtc
) VALUES (
    @Id, @Kind, @Message, @DeviceId, @DeviceCode, @SiteKey, @ClientIp, @CreatedAtUtc
);";
                command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("@Kind", kind ?? string.Empty);
                command.Parameters.AddWithValue("@Message", message ?? string.Empty);
                command.Parameters.AddWithValue("@DeviceId", deviceId ?? string.Empty);
                command.Parameters.AddWithValue("@DeviceCode", deviceCode ?? string.Empty);
                command.Parameters.AddWithValue("@SiteKey", siteKey ?? string.Empty);
                command.Parameters.AddWithValue("@ClientIp", clientIp ?? string.Empty);
                command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(createdAtUtc));
                command.ExecuteNonQuery();
            }
        }

        private GatewayManagedDevice GetManagedDevice(SQLiteConnection connection, SQLiteTransaction transaction, string deviceId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM GatewayManagedDevices WHERE DeviceId = @DeviceId LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapManagedDevice(reader) : null;
                }
            }
        }

        private GatewayAppCredential GetAppCredential(SQLiteConnection connection, SQLiteTransaction transaction, string deviceId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM GatewayAppCredentials WHERE DeviceId = @DeviceId LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapAppCredential(reader) : null;
                }
            }
        }

        private GatewayAccessRequest GetPendingRequest(SQLiteConnection connection, SQLiteTransaction transaction, string deviceId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT *
FROM GatewayAccessRequests
WHERE DeviceId = @DeviceId AND Status = @Status
ORDER BY UpdatedAtUtc DESC
LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.Parameters.AddWithValue("@Status", (int)GatewayAccessRequestStatus.Pending);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    var request = MapRequest(reader);
                    request.RequestedSites = GetRequestSites(connection, request.Id, transaction);
                    return request;
                }
            }
        }

        private void InsertRequest(SQLiteConnection connection, SQLiteTransaction transaction, GatewayAccessRequest request)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO GatewayAccessRequests (
    Id, DeviceId, DeviceCode, SignalHash, CompanyName, ApplicantName, Phone, Reason,
    TargetPath, ClientIp, LegacyUrlBefore, LegacyCompanyId, LegacyUserId, LegacyDataCenterId,
    LegacyDeviceImei, LegacyMobileUserNum, AllowAllSites, Status, ReviewNote, ReviewedBy, ExternalApprovalId,
    CreatedAtUtc, UpdatedAtUtc, ReviewedAtUtc, ProcessedAtUtc
) VALUES (
    @Id, @DeviceId, @DeviceCode, @SignalHash, @CompanyName, @ApplicantName, @Phone, @Reason,
    @TargetPath, @ClientIp, @LegacyUrlBefore, @LegacyCompanyId, @LegacyUserId, @LegacyDataCenterId,
    @LegacyDeviceImei, @LegacyMobileUserNum, @AllowAllSites, @Status, @ReviewNote, @ReviewedBy, @ExternalApprovalId,
    @CreatedAtUtc, @UpdatedAtUtc, @ReviewedAtUtc, @ProcessedAtUtc
);";
                BindRequest(command, request);
                command.ExecuteNonQuery();
            }
        }

        private void UpdateRequest(SQLiteConnection connection, SQLiteTransaction transaction, GatewayAccessRequest request)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE GatewayAccessRequests
SET DeviceCode = @DeviceCode,
    SignalHash = @SignalHash,
    CompanyName = @CompanyName,
    ApplicantName = @ApplicantName,
    Phone = @Phone,
    Reason = @Reason,
    TargetPath = @TargetPath,
    ClientIp = @ClientIp,
    LegacyUrlBefore = @LegacyUrlBefore,
    LegacyCompanyId = @LegacyCompanyId,
    LegacyUserId = @LegacyUserId,
    LegacyDataCenterId = @LegacyDataCenterId,
    LegacyDeviceImei = @LegacyDeviceImei,
    LegacyMobileUserNum = @LegacyMobileUserNum,
    AllowAllSites = @AllowAllSites,
    Status = @Status,
    ReviewNote = @ReviewNote,
    ReviewedBy = @ReviewedBy,
    ExternalApprovalId = @ExternalApprovalId,
    UpdatedAtUtc = @UpdatedAtUtc,
    ReviewedAtUtc = @ReviewedAtUtc,
    ProcessedAtUtc = @ProcessedAtUtc
WHERE Id = @Id;";
                BindRequest(command, request);
                command.ExecuteNonQuery();
            }
        }

        private void UpdateRequestSitePolicy(SQLiteConnection connection, SQLiteTransaction transaction, Guid requestId, bool allowAllSites, DateTime updatedAtUtc)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE GatewayAccessRequests
SET AllowAllSites = @AllowAllSites,
    UpdatedAtUtc = @UpdatedAtUtc
WHERE Id = @Id;";
                command.Parameters.AddWithValue("@AllowAllSites", allowAllSites ? 1 : 0);
                command.Parameters.AddWithValue("@UpdatedAtUtc", ToDbDateTime(updatedAtUtc));
                command.Parameters.AddWithValue("@Id", requestId.ToString("D"));
                command.ExecuteNonQuery();
            }
        }

        private void ReplaceRequestSites(SQLiteConnection connection, SQLiteTransaction transaction, Guid requestId, IList<string> siteKeys)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM GatewayAccessRequestSites WHERE RequestId = @RequestId;";
                command.Parameters.AddWithValue("@RequestId", requestId.ToString("D"));
                command.ExecuteNonQuery();
            }

            foreach (var siteKey in siteKeys)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayAccessRequestSites (Id, RequestId, SiteKey)
VALUES (@Id, @RequestId, @SiteKey);";
                    command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                    command.Parameters.AddWithValue("@RequestId", requestId.ToString("D"));
                    command.Parameters.AddWithValue("@SiteKey", siteKey);
                    command.ExecuteNonQuery();
                }
            }
        }

        private IList<GatewayAccessRequestSite> GetRequestSites(SQLiteConnection connection, Guid requestId, SQLiteTransaction transaction)
        {
            var sites = new List<GatewayAccessRequestSite>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM GatewayAccessRequestSites WHERE RequestId = @RequestId ORDER BY SiteKey;";
                command.Parameters.AddWithValue("@RequestId", requestId.ToString("D"));
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sites.Add(new GatewayAccessRequestSite
                        {
                            Id = Guid.Parse(Convert.ToString(reader["Id"])),
                            RequestId = Guid.Parse(Convert.ToString(reader["RequestId"])),
                            SiteKey = Convert.ToString(reader["SiteKey"]) ?? string.Empty
                        });
                    }
                }
            }

            return sites;
        }

        private static GatewayAccessRequest MapRequest(IDataRecord record)
        {
            return new GatewayAccessRequest
            {
                Id = Guid.Parse(Convert.ToString(record["Id"])),
                DeviceId = Convert.ToString(record["DeviceId"]) ?? string.Empty,
                DeviceCode = Convert.ToString(record["DeviceCode"]) ?? string.Empty,
                SignalHash = Convert.ToString(record["SignalHash"]) ?? string.Empty,
                CompanyName = Convert.ToString(record["CompanyName"]) ?? string.Empty,
                ApplicantName = Convert.ToString(record["ApplicantName"]) ?? string.Empty,
                Phone = Convert.ToString(record["Phone"]) ?? string.Empty,
                Reason = Convert.ToString(record["Reason"]) ?? string.Empty,
                TargetPath = Convert.ToString(record["TargetPath"]) ?? string.Empty,
                ClientIp = Convert.ToString(record["ClientIp"]) ?? string.Empty,
                LegacyUrlBefore = Convert.ToString(record["LegacyUrlBefore"]) ?? string.Empty,
                LegacyCompanyId = Convert.ToString(record["LegacyCompanyId"]) ?? string.Empty,
                LegacyUserId = Convert.ToString(record["LegacyUserId"]) ?? string.Empty,
                LegacyDataCenterId = Convert.ToString(record["LegacyDataCenterId"]) ?? string.Empty,
                LegacyDeviceImei = Convert.ToString(record["LegacyDeviceImei"]) ?? string.Empty,
                LegacyMobileUserNum = Convert.ToString(record["LegacyMobileUserNum"]) ?? string.Empty,
                AllowAllSites = Convert.ToInt32(record["AllowAllSites"]) == 1,
                Status = (GatewayAccessRequestStatus)Convert.ToInt32(record["Status"]),
                ReviewNote = Convert.ToString(record["ReviewNote"]) ?? string.Empty,
                ReviewedBy = Convert.ToString(record["ReviewedBy"]) ?? string.Empty,
                ExternalApprovalId = Convert.ToString(record["ExternalApprovalId"]) ?? string.Empty,
                CreatedAtUtc = ParseDbDateTime(record["CreatedAtUtc"]),
                UpdatedAtUtc = ParseDbDateTime(record["UpdatedAtUtc"]),
                ReviewedAtUtc = ParseNullableDbDateTime(record["ReviewedAtUtc"]),
                ProcessedAtUtc = ParseNullableDbDateTime(record["ProcessedAtUtc"])
            };
        }

        private static void BindRequest(SQLiteCommand command, GatewayAccessRequest request)
        {
            command.Parameters.AddWithValue("@Id", request.Id.ToString("D"));
            command.Parameters.AddWithValue("@DeviceId", request.DeviceId);
            command.Parameters.AddWithValue("@DeviceCode", request.DeviceCode);
            command.Parameters.AddWithValue("@SignalHash", request.SignalHash ?? string.Empty);
            command.Parameters.AddWithValue("@CompanyName", request.CompanyName ?? string.Empty);
            command.Parameters.AddWithValue("@ApplicantName", request.ApplicantName ?? string.Empty);
            command.Parameters.AddWithValue("@Phone", request.Phone ?? string.Empty);
            command.Parameters.AddWithValue("@Reason", request.Reason ?? string.Empty);
            command.Parameters.AddWithValue("@TargetPath", request.TargetPath ?? string.Empty);
            command.Parameters.AddWithValue("@ClientIp", request.ClientIp ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyUrlBefore", request.LegacyUrlBefore ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyCompanyId", request.LegacyCompanyId ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyUserId", request.LegacyUserId ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyDataCenterId", request.LegacyDataCenterId ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyDeviceImei", request.LegacyDeviceImei ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyMobileUserNum", request.LegacyMobileUserNum ?? string.Empty);
            command.Parameters.AddWithValue("@AllowAllSites", request.AllowAllSites ? 1 : 0);
            command.Parameters.AddWithValue("@Status", (int)request.Status);
            command.Parameters.AddWithValue("@ReviewNote", request.ReviewNote ?? string.Empty);
            command.Parameters.AddWithValue("@ReviewedBy", request.ReviewedBy ?? string.Empty);
            command.Parameters.AddWithValue("@ExternalApprovalId", request.ExternalApprovalId ?? string.Empty);
            command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(request.CreatedAtUtc));
            command.Parameters.AddWithValue("@UpdatedAtUtc", ToDbDateTime(request.UpdatedAtUtc));
            command.Parameters.AddWithValue("@ReviewedAtUtc", request.ReviewedAtUtc.HasValue ? (object)ToDbDateTime(request.ReviewedAtUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("@ProcessedAtUtc", request.ProcessedAtUtc.HasValue ? (object)ToDbDateTime(request.ProcessedAtUtc.Value) : DBNull.Value);
        }

        private IList<GatewayAccessRequest> GetAllRequests(SQLiteConnection connection)
        {
            var requests = new List<GatewayAccessRequest>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayAccessRequests ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var request = MapRequest(reader);
                        request.RequestedSites = GetRequestSites(connection, request.Id, null);
                        requests.Add(request);
                    }
                }
            }

            return requests;
        }

        private GatewayAccessRequest GetRequest(SQLiteConnection connection, SQLiteTransaction transaction, Guid requestId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM GatewayAccessRequests WHERE Id = @Id LIMIT 1;";
                command.Parameters.AddWithValue("@Id", requestId.ToString("D"));
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    var request = MapRequest(reader);
                    request.RequestedSites = GetRequestSites(connection, request.Id, transaction);
                    return request;
                }
            }
        }

        private GatewayAccessRequest GetLatestUnprocessedDecision(SQLiteConnection connection, SQLiteTransaction transaction, string deviceId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT *
FROM GatewayAccessRequests
WHERE DeviceId = @DeviceId
  AND ProcessedAtUtc IS NULL
  AND Status IN (@ApprovedStatus, @RejectedStatus)
ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC
LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.Parameters.AddWithValue("@ApprovedStatus", (int)GatewayAccessRequestStatus.Approved);
                command.Parameters.AddWithValue("@RejectedStatus", (int)GatewayAccessRequestStatus.Rejected);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    var request = MapRequest(reader);
                    request.RequestedSites = GetRequestSites(connection, request.Id, transaction);
                    return request;
                }
            }
        }

        private GatewayDeviceAuthorization GetAuthorization(SQLiteConnection connection, SQLiteTransaction transaction, Guid authorizationId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM GatewayDeviceAuthorizations WHERE Id = @Id LIMIT 1;";
                command.Parameters.AddWithValue("@Id", authorizationId.ToString("D"));
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    var authorization = MapAuthorization(reader);
                    authorization.AuthorizedSites = GetAuthorizationSites(connection, authorization.Id, transaction);
                    return authorization;
                }
            }
        }

        private GatewayDeviceAuthorization GetActiveAuthorization(SQLiteConnection connection, SQLiteTransaction transaction, string deviceId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT *
FROM GatewayDeviceAuthorizations
WHERE DeviceId = @DeviceId AND Status = @Status
ORDER BY ApprovedAtUtc DESC
LIMIT 1;";
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.Parameters.AddWithValue("@Status", (int)GatewayAuthorizationStatus.Approved);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    var authorization = MapAuthorization(reader);
                    authorization.AuthorizedSites = GetAuthorizationSites(connection, authorization.Id, transaction);
                    return authorization;
                }
            }
        }

        private void ReplaceAuthorizationSites(SQLiteConnection connection, SQLiteTransaction transaction, Guid authorizationId, IList<string> siteKeys)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM GatewayDeviceAuthorizationSites WHERE AuthorizationId = @AuthorizationId;";
                command.Parameters.AddWithValue("@AuthorizationId", authorizationId.ToString("D"));
                command.ExecuteNonQuery();
            }

            foreach (var siteKey in siteKeys)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayDeviceAuthorizationSites (Id, AuthorizationId, SiteKey)
VALUES (@Id, @AuthorizationId, @SiteKey);";
                    command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
                    command.Parameters.AddWithValue("@AuthorizationId", authorizationId.ToString("D"));
                    command.Parameters.AddWithValue("@SiteKey", siteKey);
                    command.ExecuteNonQuery();
                }
            }
        }

        private IList<GatewayDeviceAuthorizationSite> GetAuthorizationSites(SQLiteConnection connection, Guid authorizationId, SQLiteTransaction transaction)
        {
            var sites = new List<GatewayDeviceAuthorizationSite>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM GatewayDeviceAuthorizationSites WHERE AuthorizationId = @AuthorizationId ORDER BY SiteKey;";
                command.Parameters.AddWithValue("@AuthorizationId", authorizationId.ToString("D"));
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sites.Add(new GatewayDeviceAuthorizationSite
                        {
                            Id = Guid.Parse(Convert.ToString(reader["Id"])),
                            AuthorizationId = Guid.Parse(Convert.ToString(reader["AuthorizationId"])),
                            SiteKey = Convert.ToString(reader["SiteKey"]) ?? string.Empty
                        });
                    }
                }
            }

            return sites;
        }

        private static GatewayDeviceAuthorization MapAuthorization(IDataRecord record)
        {
            Guid id;
            Guid.TryParse(Convert.ToString(record["Id"]), out id);
            return new GatewayDeviceAuthorization
            {
                Id = id,
                DeviceId = Convert.ToString(record["DeviceId"]) ?? string.Empty,
                DeviceCode = Convert.ToString(record["DeviceCode"]) ?? string.Empty,
                CompanyName = Convert.ToString(record["CompanyName"]) ?? string.Empty,
                ApplicantName = Convert.ToString(record["ApplicantName"]) ?? string.Empty,
                Phone = Convert.ToString(record["Phone"]) ?? string.Empty,
                LegacyCompanyId = Convert.ToString(record["LegacyCompanyId"]) ?? string.Empty,
                LegacyUrlBefore = Convert.ToString(record["LegacyUrlBefore"]) ?? string.Empty,
                LegacyUserId = Convert.ToString(record["LegacyUserId"]) ?? string.Empty,
                LegacyDataCenterId = Convert.ToString(record["LegacyDataCenterId"]) ?? string.Empty,
                LegacyDeviceImei = Convert.ToString(record["LegacyDeviceImei"]) ?? string.Empty,
                LegacyMobileUserNum = Convert.ToString(record["LegacyMobileUserNum"]) ?? string.Empty,
                Status = (GatewayAuthorizationStatus)Convert.ToInt32(record["Status"]),
                PolicyMode = Convert.ToString(record["PolicyMode"]) ?? string.Empty,
                ReviewNote = Convert.ToString(record["ReviewNote"]) ?? string.Empty,
                LastSeenIp = Convert.ToString(record["LastSeenIp"]) ?? string.Empty,
                LastSeenSiteKey = Convert.ToString(record["LastSeenSiteKey"]) ?? string.Empty,
                CreatedAtUtc = ParseDbDateTime(record["CreatedAtUtc"]),
                ApprovedAtUtc = ParseDbDateTime(record["ApprovedAtUtc"]),
                LastSeenAtUtc = ParseNullableDbDateTime(record["LastSeenAtUtc"]),
                RevokedAtUtc = ParseNullableDbDateTime(record["RevokedAtUtc"])
            };
        }

        private static void BindAuthorization(SQLiteCommand command, GatewayDeviceAuthorization authorization)
        {
            command.Parameters.AddWithValue("@Id", authorization.Id.ToString("D"));
            command.Parameters.AddWithValue("@DeviceId", authorization.DeviceId);
            command.Parameters.AddWithValue("@DeviceCode", authorization.DeviceCode);
            command.Parameters.AddWithValue("@CompanyName", authorization.CompanyName ?? string.Empty);
            command.Parameters.AddWithValue("@ApplicantName", authorization.ApplicantName ?? string.Empty);
            command.Parameters.AddWithValue("@Phone", authorization.Phone ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyCompanyId", authorization.LegacyCompanyId ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyUrlBefore", authorization.LegacyUrlBefore ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyUserId", authorization.LegacyUserId ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyDataCenterId", authorization.LegacyDataCenterId ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyDeviceImei", authorization.LegacyDeviceImei ?? string.Empty);
            command.Parameters.AddWithValue("@LegacyMobileUserNum", authorization.LegacyMobileUserNum ?? string.Empty);
            command.Parameters.AddWithValue("@Status", (int)authorization.Status);
            command.Parameters.AddWithValue("@PolicyMode", authorization.PolicyMode ?? GatewayDeviceAuthorization.DevicePolicyMode);
            command.Parameters.AddWithValue("@ReviewNote", authorization.ReviewNote ?? string.Empty);
            command.Parameters.AddWithValue("@LastSeenIp", authorization.LastSeenIp ?? string.Empty);
            command.Parameters.AddWithValue("@LastSeenSiteKey", authorization.LastSeenSiteKey ?? string.Empty);
            command.Parameters.AddWithValue("@CreatedAtUtc", ToDbDateTime(authorization.CreatedAtUtc));
            command.Parameters.AddWithValue("@ApprovedAtUtc", ToDbDateTime(authorization.ApprovedAtUtc));
            command.Parameters.AddWithValue("@LastSeenAtUtc", authorization.LastSeenAtUtc.HasValue ? (object)ToDbDateTime(authorization.LastSeenAtUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("@RevokedAtUtc", authorization.RevokedAtUtc.HasValue ? (object)ToDbDateTime(authorization.RevokedAtUtc.Value) : DBNull.Value);
        }

        private IList<GatewayDeviceAuthorization> GetAllAuthorizations(SQLiteConnection connection)
        {
            var authorizations = new List<GatewayDeviceAuthorization>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayDeviceAuthorizations ORDER BY ApprovedAtUtc DESC, CreatedAtUtc DESC;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var authorization = MapAuthorization(reader);
                        authorization.AuthorizedSites = GetAuthorizationSites(connection, authorization.Id, null);
                        authorizations.Add(authorization);
                    }
                }
            }

            return authorizations;
        }

        private void UpsertAuthorization(SQLiteConnection connection, SQLiteTransaction transaction, GatewayAccessRequest request, IList<string> approvedSiteKeys, bool allowAllSites, string note, DateTime now)
        {
            var authorization = GetActiveAuthorization(connection, transaction, request.DeviceId);
            if (authorization == null)
            {
                authorization = new GatewayDeviceAuthorization
                {
                    Id = Guid.NewGuid(),
                    DeviceId = request.DeviceId,
                    DeviceCode = request.DeviceCode,
                    CompanyName = request.CompanyName,
                    ApplicantName = request.ApplicantName,
                    Phone = request.Phone,
                    LegacyCompanyId = request.LegacyCompanyId,
                    LegacyUrlBefore = request.LegacyUrlBefore,
                    LegacyUserId = request.LegacyUserId,
                    LegacyDataCenterId = request.LegacyDataCenterId,
                    LegacyDeviceImei = request.LegacyDeviceImei,
                    LegacyMobileUserNum = request.LegacyMobileUserNum,
                    Status = GatewayAuthorizationStatus.Approved,
                    PolicyMode = allowAllSites ? GatewayDeviceAuthorization.AllSitesPolicyMode : GatewayDeviceAuthorization.DevicePolicyMode,
                    ReviewNote = note ?? string.Empty,
                    LastSeenIp = request.ClientIp,
                    LastSeenSiteKey = allowAllSites ? "*" : (approvedSiteKeys.Count > 0 ? approvedSiteKeys[0] : string.Empty),
                    CreatedAtUtc = now,
                    ApprovedAtUtc = now,
                    LastSeenAtUtc = now
                };

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO GatewayDeviceAuthorizations (
    Id, DeviceId, DeviceCode, CompanyName, ApplicantName, Phone, LegacyCompanyId, LegacyUrlBefore,
    LegacyUserId, LegacyDataCenterId, LegacyDeviceImei, LegacyMobileUserNum, Status, PolicyMode,
    ReviewNote, LastSeenIp, LastSeenSiteKey, CreatedAtUtc, ApprovedAtUtc, LastSeenAtUtc, RevokedAtUtc
) VALUES (
    @Id, @DeviceId, @DeviceCode, @CompanyName, @ApplicantName, @Phone, @LegacyCompanyId, @LegacyUrlBefore,
    @LegacyUserId, @LegacyDataCenterId, @LegacyDeviceImei, @LegacyMobileUserNum, @Status, @PolicyMode,
    @ReviewNote, @LastSeenIp, @LastSeenSiteKey, @CreatedAtUtc, @ApprovedAtUtc, @LastSeenAtUtc, @RevokedAtUtc
);";
                    BindAuthorization(command, authorization);
                    command.ExecuteNonQuery();
                }

                ReplaceAuthorizationSites(connection, transaction, authorization.Id, allowAllSites ? new List<string>() : approvedSiteKeys);
                return;
            }

            var mergedSiteKeys = allowAllSites
                ? new List<string>()
                : authorization.AuthorizedSites
                    .Select(functionSite => functionSite.SiteKey)
                    .Concat(approvedSiteKeys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            authorization.DeviceCode = request.DeviceCode;
            authorization.CompanyName = request.CompanyName;
            authorization.ApplicantName = request.ApplicantName;
            authorization.Phone = request.Phone;
            authorization.LegacyCompanyId = request.LegacyCompanyId;
            authorization.LegacyUrlBefore = request.LegacyUrlBefore;
            authorization.LegacyUserId = request.LegacyUserId;
            authorization.LegacyDataCenterId = request.LegacyDataCenterId;
            authorization.LegacyDeviceImei = request.LegacyDeviceImei;
            authorization.LegacyMobileUserNum = request.LegacyMobileUserNum;
            authorization.Status = GatewayAuthorizationStatus.Approved;
            authorization.PolicyMode = allowAllSites ? GatewayDeviceAuthorization.AllSitesPolicyMode : GatewayDeviceAuthorization.DevicePolicyMode;
            authorization.ReviewNote = note ?? string.Empty;
            authorization.LastSeenIp = request.ClientIp;
            authorization.LastSeenSiteKey = allowAllSites ? "*" : (approvedSiteKeys.Count > 0 ? approvedSiteKeys[0] : authorization.LastSeenSiteKey);
            authorization.ApprovedAtUtc = now;
            authorization.LastSeenAtUtc = now;
            authorization.RevokedAtUtc = null;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE GatewayDeviceAuthorizations
SET DeviceCode = @DeviceCode,
    CompanyName = @CompanyName,
    ApplicantName = @ApplicantName,
    Phone = @Phone,
    LegacyCompanyId = @LegacyCompanyId,
    LegacyUrlBefore = @LegacyUrlBefore,
    LegacyUserId = @LegacyUserId,
    LegacyDataCenterId = @LegacyDataCenterId,
    LegacyDeviceImei = @LegacyDeviceImei,
    LegacyMobileUserNum = @LegacyMobileUserNum,
    Status = @Status,
    PolicyMode = @PolicyMode,
    ReviewNote = @ReviewNote,
    LastSeenIp = @LastSeenIp,
    LastSeenSiteKey = @LastSeenSiteKey,
    ApprovedAtUtc = @ApprovedAtUtc,
    LastSeenAtUtc = @LastSeenAtUtc,
    RevokedAtUtc = @RevokedAtUtc
WHERE Id = @Id;";
                BindAuthorization(command, authorization);
                command.ExecuteNonQuery();
            }

            ReplaceAuthorizationSites(connection, transaction, authorization.Id, mergedSiteKeys);
        }

        private void PromoteManagedDevice(SQLiteConnection connection, SQLiteTransaction transaction, string deviceId, string signalHash, string clientIp, DateTime now)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE GatewayManagedDevices
SET SignalHash = @SignalHash,
    LastSeenIp = @LastSeenIp,
    LastSeenAtUtc = @LastSeenAtUtc,
    TrustState = @TrustState,
    ChallengeReason = @ChallengeReason,
    ChallengedAtUtc = NULL
WHERE DeviceId = @DeviceId;";
                command.Parameters.AddWithValue("@SignalHash", signalHash ?? string.Empty);
                command.Parameters.AddWithValue("@LastSeenIp", clientIp ?? string.Empty);
                command.Parameters.AddWithValue("@LastSeenAtUtc", ToDbDateTime(now));
                command.Parameters.AddWithValue("@TrustState", (int)GatewayDeviceTrustState.Trusted);
                command.Parameters.AddWithValue("@ChallengeReason", string.Empty);
                command.Parameters.AddWithValue("@DeviceId", deviceId);
                command.ExecuteNonQuery();
            }
        }

        private void UpdateRequestReviewAndProcessing(SQLiteConnection connection, SQLiteTransaction transaction, Guid requestId, string note, string reviewedBy, string externalApprovalId, DateTime reviewedAtUtc, DateTime processedAtUtc, GatewayAccessRequestStatus status)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE GatewayAccessRequests
SET Status = @Status,
    ReviewNote = @ReviewNote,
    ReviewedBy = @ReviewedBy,
    ExternalApprovalId = @ExternalApprovalId,
    ReviewedAtUtc = @ReviewedAtUtc,
    ProcessedAtUtc = @ProcessedAtUtc,
    UpdatedAtUtc = @UpdatedAtUtc
WHERE Id = @Id;";
                command.Parameters.AddWithValue("@Status", (int)status);
                command.Parameters.AddWithValue("@ReviewNote", note ?? string.Empty);
                command.Parameters.AddWithValue("@ReviewedBy", reviewedBy ?? string.Empty);
                command.Parameters.AddWithValue("@ExternalApprovalId", externalApprovalId ?? string.Empty);
                command.Parameters.AddWithValue("@ReviewedAtUtc", ToDbDateTime(reviewedAtUtc));
                command.Parameters.AddWithValue("@ProcessedAtUtc", ToDbDateTime(processedAtUtc));
                command.Parameters.AddWithValue("@UpdatedAtUtc", ToDbDateTime(processedAtUtc));
                command.Parameters.AddWithValue("@Id", requestId.ToString("D"));
                command.ExecuteNonQuery();
            }
        }

        private static IList<string> NormalizeSiteKeys(IEnumerable<string> siteKeys)
        {
            return (siteKeys ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<string> MergeSiteKeys(params IEnumerable<string>[] siteKeyGroups)
        {
            return (siteKeyGroups ?? new IEnumerable<string>[0])
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<string> NormalizeIdentityHashes(IEnumerable<string> identityHashes)
        {
            return (identityHashes ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void DeleteReplayNonces(SQLiteConnection connection, SQLiteTransaction transaction, string credentialKey)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM GatewayAppReplayNonces WHERE CredentialKey = @CredentialKey;";
                command.Parameters.AddWithValue("@CredentialKey", credentialKey ?? string.Empty);
                command.ExecuteNonQuery();
            }
        }

        private static void DeleteExpiredReplayNonces(SQLiteConnection connection, SQLiteTransaction transaction, DateTime now)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM GatewayAppReplayNonces WHERE ExpiresAtUtc <= @ExpiresAtUtc;";
                command.Parameters.AddWithValue("@ExpiresAtUtc", ToDbDateTime(now));
                command.ExecuteNonQuery();
            }
        }

        private IList<GatewayManagedDevice> GetAllManagedDevices(SQLiteConnection connection)
        {
            var devices = new List<GatewayManagedDevice>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayManagedDevices ORDER BY CreatedAtUtc DESC;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        devices.Add(MapManagedDevice(reader));
                    }
                }
            }

            return devices;
        }

        private IList<GatewayAuditLog> GetAllAuditLogs(SQLiteConnection connection)
        {
            var auditLogs = new List<GatewayAuditLog>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GatewayAuditLogs ORDER BY CreatedAtUtc DESC;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        auditLogs.Add(MapAuditLog(reader));
                    }
                }
            }

            return auditLogs;
        }

        private static GatewayAuditLog MapAuditLog(IDataRecord record)
        {
            return new GatewayAuditLog
            {
                Id = Guid.Parse(Convert.ToString(record["Id"])),
                Kind = Convert.ToString(record["Kind"]) ?? string.Empty,
                Message = Convert.ToString(record["Message"]) ?? string.Empty,
                DeviceId = Convert.ToString(record["DeviceId"]) ?? string.Empty,
                DeviceCode = Convert.ToString(record["DeviceCode"]) ?? string.Empty,
                SiteKey = Convert.ToString(record["SiteKey"]) ?? string.Empty,
                ClientIp = Convert.ToString(record["ClientIp"]) ?? string.Empty,
                CreatedAtUtc = ParseDbDateTime(record["CreatedAtUtc"])
            };
        }
    }
}
