using GatewayDemo.Data;
using GatewayDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace GatewayDemo.Services;

public sealed class GatewayRepository(GatewayDbContext dbContext) : IGatewayRepository
{
    public async Task<GatewayDeviceAuthorization?> GetActiveAuthorizationAsync(string deviceId, CancellationToken cancellationToken)
    {
        return await dbContext.DeviceAuthorizations
            .Include(x => x.AuthorizedSites)
            .FirstOrDefaultAsync(
                x => x.DeviceId == deviceId && x.Status == GatewayAuthorizationStatus.Approved,
                cancellationToken);
    }

    public async Task<GatewayAccessRequest?> GetLatestRequestAsync(string deviceId, CancellationToken cancellationToken)
    {
        var requests = await dbContext.AccessRequests
            .AsNoTracking()
            .Include(x => x.RequestedSites)
            .Where(x => x.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        return requests
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefault();
    }

    public async Task<GatewayAccessRequest> CreateOrUpdateRequestAsync(
        DeviceContext device,
        string companyName,
        string applicantName,
        string phone,
        string reason,
        string targetPath,
        IReadOnlyCollection<string> siteKeys,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var request = await dbContext.AccessRequests
            .Include(x => x.RequestedSites)
            .FirstOrDefaultAsync(
                x => x.DeviceId == device.DeviceId && x.Status == GatewayAccessRequestStatus.Pending,
                cancellationToken);

        if (request is null)
        {
            request = new GatewayAccessRequest
            {
                Id = Guid.NewGuid(),
                DeviceId = device.DeviceId,
                DeviceCode = device.DeviceCode,
                SignalHash = device.SignalHash,
                CompanyName = companyName,
                ApplicantName = applicantName,
                Phone = phone,
                Reason = reason,
                TargetPath = targetPath,
                ClientIp = device.ClientIp,
                Status = GatewayAccessRequestStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            ReplaceRequestedSites(request, siteKeys);
            dbContext.AccessRequests.Add(request);
            dbContext.AuditLogs.Add(BuildAudit(
                "request-created",
                $"提交接入申请，站点：{string.Join(", ", siteKeys)}",
                device,
                string.Join(",", siteKeys),
                device.ClientIp));
        }
        else
        {
            request.DeviceCode = device.DeviceCode;
            request.SignalHash = device.SignalHash;
            request.CompanyName = companyName;
            request.ApplicantName = applicantName;
            request.Phone = phone;
            request.Reason = reason;
            request.TargetPath = targetPath;
            request.ClientIp = device.ClientIp;
            request.UpdatedAtUtc = now;
            ReplaceRequestedSites(request, siteKeys);
            dbContext.AuditLogs.Add(BuildAudit(
                "request-updated",
                $"更新接入申请，站点：{string.Join(", ", siteKeys)}",
                device,
                string.Join(",", siteKeys),
                device.ClientIp));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task ApproveRequestAsync(Guid requestId, IReadOnlyCollection<string> siteKeys, string note, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var request = await dbContext.AccessRequests
            .Include(x => x.RequestedSites)
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken)
            ?? throw new InvalidOperationException("Request not found.");

        request.Status = GatewayAccessRequestStatus.Approved;
        request.ReviewNote = note;
        request.ReviewedAtUtc = now;
        request.UpdatedAtUtc = now;

        var approvedSiteKeys = siteKeys.Count > 0
            ? siteKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : request.RequestedSites.Select(x => x.SiteKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var authorization = await dbContext.DeviceAuthorizations
            .Include(x => x.AuthorizedSites)
            .FirstOrDefaultAsync(
                x => x.DeviceId == request.DeviceId && x.Status == GatewayAuthorizationStatus.Approved,
                cancellationToken);

        if (authorization is null)
        {
            authorization = new GatewayDeviceAuthorization
            {
                Id = Guid.NewGuid(),
                DeviceId = request.DeviceId,
                DeviceCode = request.DeviceCode,
                CompanyName = request.CompanyName,
                ApplicantName = request.ApplicantName,
                Phone = request.Phone,
                Status = GatewayAuthorizationStatus.Approved,
                PolicyMode = "device",
                ReviewNote = note,
                CreatedAtUtc = now,
                ApprovedAtUtc = now,
            };

            ReplaceAuthorizedSites(authorization, approvedSiteKeys);
            dbContext.DeviceAuthorizations.Add(authorization);
        }
        else
        {
            authorization.DeviceCode = request.DeviceCode;
            authorization.CompanyName = request.CompanyName;
            authorization.ApplicantName = request.ApplicantName;
            authorization.Phone = request.Phone;
            authorization.ReviewNote = note;
            authorization.ApprovedAtUtc = now;

            var mergedSiteKeys = authorization.AuthorizedSites
                .Select(x => x.SiteKey)
                .Concat(approvedSiteKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ReplaceAuthorizedSites(authorization, mergedSiteKeys);
        }

        var managedDevice = await dbContext.ManagedDevices
            .FirstOrDefaultAsync(x => x.DeviceId == request.DeviceId, cancellationToken);

        if (managedDevice is not null)
        {
            managedDevice.SignalHash = request.SignalHash;
            managedDevice.TrustState = GatewayDeviceTrustState.Trusted;
            managedDevice.ChallengeReason = string.Empty;
            managedDevice.ChallengedAtUtc = null;
            managedDevice.LastSeenIp = request.ClientIp;
            managedDevice.LastSeenAtUtc = now;
        }

        dbContext.AuditLogs.Add(new GatewayAuditLog
        {
            Id = Guid.NewGuid(),
            Kind = "request-approved",
            Message = $"审批通过，站点：{string.Join(", ", approvedSiteKeys)}",
            DeviceId = request.DeviceId,
            DeviceCode = request.DeviceCode,
            SiteKey = string.Join(",", approvedSiteKeys),
            ClientIp = request.ClientIp,
            CreatedAtUtc = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectRequestAsync(Guid requestId, string note, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var request = await dbContext.AccessRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken)
            ?? throw new InvalidOperationException("Request not found.");

        request.Status = GatewayAccessRequestStatus.Rejected;
        request.ReviewNote = note;
        request.ReviewedAtUtc = now;
        request.UpdatedAtUtc = now;

        dbContext.AuditLogs.Add(new GatewayAuditLog
        {
            Id = Guid.NewGuid(),
            Kind = "request-rejected",
            Message = "审批拒绝，设备未放行。",
            DeviceId = request.DeviceId,
            DeviceCode = request.DeviceCode,
            SiteKey = string.Empty,
            ClientIp = request.ClientIp,
            CreatedAtUtc = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAuthorizationAsync(Guid authorizationId, string note, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var authorization = await dbContext.DeviceAuthorizations
            .FirstOrDefaultAsync(x => x.Id == authorizationId, cancellationToken)
            ?? throw new InvalidOperationException("Authorization not found.");

        var managedDevice = await dbContext.ManagedDevices
            .FirstOrDefaultAsync(x => x.DeviceId == authorization.DeviceId, cancellationToken);
        var revokedAppKeyId = managedDevice?.AppKeyId;

        authorization.Status = GatewayAuthorizationStatus.Revoked;
        authorization.ReviewNote = note;
        authorization.RevokedAtUtc = now;

        if (!string.IsNullOrWhiteSpace(revokedAppKeyId))
        {
            var appReplayNonces = await dbContext.AppReplayNonces
                .Where(x => x.AppKeyId == revokedAppKeyId)
                .ToListAsync(cancellationToken);

            if (appReplayNonces.Count > 0)
            {
                dbContext.AppReplayNonces.RemoveRange(appReplayNonces);
            }
        }

        if (managedDevice is not null)
        {
            managedDevice.AppKeyId = null;
            managedDevice.ProtectedAppSecret = null;
            managedDevice.LastSeenAtUtc = now;
        }

        dbContext.AuditLogs.Add(new GatewayAuditLog
        {
            Id = Guid.NewGuid(),
            Kind = "authorization-revoked",
            Message = "管理员撤销了设备放行。",
            DeviceId = authorization.DeviceId,
            DeviceCode = authorization.DeviceCode,
            SiteKey = authorization.LastSeenSiteKey,
            ClientIp = authorization.LastSeenIp,
            CreatedAtUtc = now,
        });

        if (managedDevice is not null)
        {
            dbContext.AuditLogs.Add(new GatewayAuditLog
            {
                Id = Guid.NewGuid(),
                Kind = "app-credential-revoked",
                Message = $"撤销设备放行时同步废弃 APP 密钥 {managedDevice.DeviceCode}",
                DeviceId = managedDevice.DeviceId,
                DeviceCode = managedDevice.DeviceCode,
                SiteKey = authorization.LastSeenSiteKey,
                ClientIp = authorization.LastSeenIp,
                CreatedAtUtc = now,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAuthorizationAsync(string deviceId, string clientIp, string siteKey, CancellationToken cancellationToken)
    {
        var authorization = await dbContext.DeviceAuthorizations
            .FirstOrDefaultAsync(
                x => x.DeviceId == deviceId && x.Status == GatewayAuthorizationStatus.Approved,
                cancellationToken);

        if (authorization is null)
        {
            return;
        }

        authorization.LastSeenAtUtc = DateTimeOffset.UtcNow;
        authorization.LastSeenIp = clientIp;
        authorization.LastSeenSiteKey = siteKey;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAuditAsync(string kind, string message, DeviceContext device, string siteKey, string clientIp, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(BuildAudit(kind, message, device, siteKey, clientIp));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<GatewayDashboardSnapshot> GetDashboardSnapshotAsync()
    {
        var requests = await dbContext.AccessRequests
            .AsNoTracking()
            .Include(x => x.RequestedSites)
            .ToListAsync();

        var authorizations = await dbContext.DeviceAuthorizations
            .AsNoTracking()
            .Include(x => x.AuthorizedSites)
            .ToListAsync();

        var devices = await dbContext.ManagedDevices
            .AsNoTracking()
            .ToDictionaryAsync(x => x.DeviceId, StringComparer.OrdinalIgnoreCase);

        var auditLogs = await dbContext.AuditLogs
            .AsNoTracking()
            .ToListAsync();

        return new GatewayDashboardSnapshot(
            requests.OrderByDescending(x => x.UpdatedAtUtc).ToList(),
            authorizations.OrderByDescending(x => x.ApprovedAtUtc).ToList(),
            devices,
            auditLogs.OrderByDescending(x => x.CreatedAtUtc).Take(120).ToList());
    }

    private static GatewayAuditLog BuildAudit(string kind, string message, DeviceContext device, string siteKey, string clientIp)
    {
        return new GatewayAuditLog
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Message = message,
            DeviceId = device.DeviceId,
            DeviceCode = device.DeviceCode,
            SiteKey = siteKey,
            ClientIp = clientIp,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static void ReplaceRequestedSites(GatewayAccessRequest request, IEnumerable<string> siteKeys)
    {
        var normalizedKeys = siteKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedSites = request.RequestedSites
            .Where(site => !normalizedKeys.Contains(site.SiteKey))
            .ToList();

        foreach (var removedSite in removedSites)
        {
            request.RequestedSites.Remove(removedSite);
        }

        var existingKeys = request.RequestedSites
            .Select(site => site.SiteKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var siteKey in normalizedKeys.Where(siteKey => !existingKeys.Contains(siteKey)))
        {
            request.RequestedSites.Add(new GatewayAccessRequestSite
            {
                Id = Guid.NewGuid(),
                SiteKey = siteKey,
            });
        }
    }

    private static void ReplaceAuthorizedSites(GatewayDeviceAuthorization authorization, IEnumerable<string> siteKeys)
    {
        var normalizedKeys = siteKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedSites = authorization.AuthorizedSites
            .Where(site => !normalizedKeys.Contains(site.SiteKey))
            .ToList();

        foreach (var removedSite in removedSites)
        {
            authorization.AuthorizedSites.Remove(removedSite);
        }

        var existingKeys = authorization.AuthorizedSites
            .Select(site => site.SiteKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var siteKey in normalizedKeys.Where(siteKey => !existingKeys.Contains(siteKey)))
        {
            authorization.AuthorizedSites.Add(new GatewayDeviceAuthorizationSite
            {
                Id = Guid.NewGuid(),
                SiteKey = siteKey,
            });
        }
    }
}
