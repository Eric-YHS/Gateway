using System.Collections.Generic;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal sealed class LegacyAppAccessRequestDraft
    {
        public string CompanyName { get; set; }
        public string ApplicantName { get; set; }
        public string ContactHint { get; set; }
        public string Reason { get; set; }
        public string TargetPath { get; set; }
        public IReadOnlyCollection<string> SiteKeys { get; set; }
        public string LegacyUrlBefore { get; set; }
        public string LegacyCompanyId { get; set; }
        public string LegacyUserId { get; set; }
        public string LegacyDataCenterId { get; set; }
        public string LegacyDeviceImei { get; set; }
        public string LegacyMobileUserNum { get; set; }

        public LegacyAppAccessRequestDraft()
        {
            CompanyName = string.Empty;
            ApplicantName = string.Empty;
            ContactHint = string.Empty;
            Reason = string.Empty;
            TargetPath = string.Empty;
            SiteKeys = new List<string>();
            LegacyUrlBefore = string.Empty;
            LegacyCompanyId = string.Empty;
            LegacyUserId = string.Empty;
            LegacyDataCenterId = string.Empty;
            LegacyDeviceImei = string.Empty;
            LegacyMobileUserNum = string.Empty;
        }
    }
}
