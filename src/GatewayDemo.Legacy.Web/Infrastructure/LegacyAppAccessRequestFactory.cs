using System;
using System.Collections.Generic;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class LegacyAppAccessRequestFactory
    {
        public static LegacyAppAccessRequestDraft CreateAutoRequest(
            LegacyGatewayRuntime runtime,
            LegacyAppRequestDescriptor descriptor,
            GatewaySiteOptions site,
            string siteKey,
            string targetPath)
        {
            descriptor = descriptor ?? new LegacyAppRequestDescriptor();

            var normalizedSiteKey = (siteKey ?? string.Empty).Trim();
            var profile = site == null || site.LegacyAppProfile == null
                ? new LegacyAppProfileOptions()
                : site.LegacyAppProfile;

            var reason = descriptor.IsLoginRequest
                ? profile.AutoRequestLoginReason
                : profile.AutoRequestProtectedApiReason;
            if (!string.IsNullOrWhiteSpace(descriptor.ReviewSummary))
            {
                reason = string.IsNullOrWhiteSpace(reason)
                    ? descriptor.ReviewSummary
                    : reason + " " + descriptor.ReviewSummary;
            }

            return new LegacyAppAccessRequestDraft
            {
                CompanyName = FirstNonEmpty(descriptor.CompanyName, normalizedSiteKey),
                ApplicantName = FirstNonEmpty(descriptor.ApplicantName, profile.AutoRequestApplicantName),
                ContactHint = descriptor.ContactHint ?? string.Empty,
                Reason = reason ?? string.Empty,
                TargetPath = ResolveTargetPath(runtime, normalizedSiteKey, targetPath),
                SiteKeys = new List<string> { normalizedSiteKey },
                LegacyUrlBefore = descriptor.UrlBefore,
                LegacyCompanyId = descriptor.CompanyId,
                LegacyUserId = descriptor.UserId,
                LegacyDataCenterId = descriptor.DataCenterId,
                LegacyDeviceImei = descriptor.DeviceImei,
                LegacyMobileUserNum = descriptor.MobileUserNum
            };
        }

        private static string ResolveTargetPath(LegacyGatewayRuntime runtime, string siteKey, string targetPath)
        {
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                return targetPath;
            }

            return runtime == null || runtime.Configuration == null
                ? string.Empty
                : runtime.Configuration.BuildSiteTargetPath(siteKey);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }
    }
}
