using System;
using System.Collections.Generic;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayAccessRequest
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; }
        public string DeviceCode { get; set; }
        public string SignalHash { get; set; }
        public string CompanyName { get; set; }
        public string ApplicantName { get; set; }
        public string Phone { get; set; }
        public string Reason { get; set; }
        public string TargetPath { get; set; }
        public string ClientIp { get; set; }
        public string LegacyUrlBefore { get; set; }
        public string LegacyCompanyId { get; set; }
        public string LegacyUserId { get; set; }
        public string LegacyDataCenterId { get; set; }
        public string LegacyDeviceImei { get; set; }
        public string LegacyMobileUserNum { get; set; }
        public bool AllowAllSites { get; set; }
        public GatewayAccessRequestStatus Status { get; set; }
        public string ReviewNote { get; set; }
        public string ReviewedBy { get; set; }
        public string ExternalApprovalId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public DateTime? ProcessedAtUtc { get; set; }
        public IList<GatewayAccessRequestSite> RequestedSites { get; set; }

        public GatewayAccessRequest()
        {
            DeviceId = string.Empty;
            DeviceCode = string.Empty;
            SignalHash = string.Empty;
            CompanyName = string.Empty;
            ApplicantName = string.Empty;
            Phone = string.Empty;
            Reason = string.Empty;
            TargetPath = string.Empty;
            ClientIp = string.Empty;
            LegacyUrlBefore = string.Empty;
            LegacyCompanyId = string.Empty;
            LegacyUserId = string.Empty;
            LegacyDataCenterId = string.Empty;
            LegacyDeviceImei = string.Empty;
            LegacyMobileUserNum = string.Empty;
            AllowAllSites = false;
            ReviewNote = string.Empty;
            ReviewedBy = string.Empty;
            ExternalApprovalId = string.Empty;
            RequestedSites = new List<GatewayAccessRequestSite>();
            Status = GatewayAccessRequestStatus.Pending;
        }
    }
}
