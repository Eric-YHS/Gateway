using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Models
{
    public sealed class GatewayDeviceAuthorization
    {
        public const string DevicePolicyMode = "device";
        public const string AllSitesPolicyMode = "all-sites";

        public Guid Id { get; set; }
        public string DeviceId { get; set; }
        public string DeviceCode { get; set; }
        public string CompanyName { get; set; }
        public string ApplicantName { get; set; }
        public string Phone { get; set; }
        public string LegacyCompanyId { get; set; }
        public string LegacyUrlBefore { get; set; }
        public string LegacyUserId { get; set; }
        public string LegacyDataCenterId { get; set; }
        public string LegacyDeviceImei { get; set; }
        public string LegacyMobileUserNum { get; set; }
        public GatewayAuthorizationStatus Status { get; set; }
        public string PolicyMode { get; set; }
        public string ReviewNote { get; set; }
        public string LastSeenIp { get; set; }
        public string LastSeenSiteKey { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ApprovedAtUtc { get; set; }
        public DateTime? LastSeenAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public IList<GatewayDeviceAuthorizationSite> AuthorizedSites { get; set; }

        public GatewayDeviceAuthorization()
        {
            DeviceId = string.Empty;
            DeviceCode = string.Empty;
            CompanyName = string.Empty;
            ApplicantName = string.Empty;
            Phone = string.Empty;
            LegacyCompanyId = string.Empty;
            LegacyUrlBefore = string.Empty;
            LegacyUserId = string.Empty;
            LegacyDataCenterId = string.Empty;
            LegacyDeviceImei = string.Empty;
            LegacyMobileUserNum = string.Empty;
            PolicyMode = DevicePolicyMode;
            ReviewNote = string.Empty;
            LastSeenIp = string.Empty;
            LastSeenSiteKey = string.Empty;
            AuthorizedSites = new List<GatewayDeviceAuthorizationSite>();
            Status = GatewayAuthorizationStatus.Approved;
        }

        public bool AllowsSite(string siteKey)
        {
            if (AllowsAllSites)
            {
                return true;
            }

            return AuthorizedSites.Any(
                site => string.Equals(site.SiteKey, siteKey, StringComparison.OrdinalIgnoreCase));
        }

        public bool AllowsAllSites
        {
            get
            {
                return string.Equals(PolicyMode, AllSitesPolicyMode, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
