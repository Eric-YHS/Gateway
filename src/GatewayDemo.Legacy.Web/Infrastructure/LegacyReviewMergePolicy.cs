using System;
using GatewayDemo.Legacy.Core.Models;
using GatewayDemo.Legacy.Core.Options;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class LegacyReviewMergePolicy
    {
        private const int PreserveExistingScoreThreshold = 5;

        public static bool ShouldPreserveExisting(
            GatewayAccessRequest existingRequest,
            string incomingApplicantName,
            string incomingPhone,
            string incomingReason,
            string incomingTargetPath,
            string incomingLegacyUserId)
        {
            if (existingRequest == null)
            {
                return false;
            }

            var existingScore = Score(
                existingRequest.ApplicantName,
                existingRequest.Phone,
                existingRequest.Reason,
                existingRequest.TargetPath,
                existingRequest.LegacyUserId);
            var incomingScore = Score(
                incomingApplicantName,
                incomingPhone,
                incomingReason,
                incomingTargetPath,
                incomingLegacyUserId);

            return existingScore >= PreserveExistingScoreThreshold && existingScore > incomingScore;
        }

        private static int Score(string applicantName, string phone, string reason, string targetPath, string legacyUserId)
        {
            var score = 0;
            var applicant = (applicantName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(applicant))
            {
                score += IsGenericMobileApplicant(applicant, legacyUserId) ? 1 : 4;
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                score += 2;
            }

            if (ContainsLoginProfile(reason))
            {
                score += 3;
            }

            if (LooksLikeLoginPath(targetPath))
            {
                score += 3;
            }

            if (!string.IsNullOrWhiteSpace(legacyUserId))
            {
                score += 1;
            }

            return score;
        }

        private static bool IsGenericMobileApplicant(string applicantName, string legacyUserId)
        {
            var applicant = (applicantName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(applicant))
            {
                return true;
            }

            var defaultProfile = new LegacyAppProfileOptions();
            if (string.Equals(applicant, defaultProfile.AutoRequestApplicantName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(applicant, "Mobile APP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(applicant, "MobileApp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(applicant, "移动APP", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(legacyUserId)
                && string.Equals(applicant, legacyUserId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsLoginProfile(string reason)
        {
            var normalized = reason ?? string.Empty;
            var defaultProfile = new LegacyAppProfileOptions();
            foreach (var userNameField in defaultProfile.UserNameFields)
            {
                if (!string.IsNullOrWhiteSpace(userNameField)
                    && normalized.IndexOf(userNameField, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeLoginPath(string targetPath)
        {
            var normalized = (targetPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var defaultProfile = new LegacyAppProfileOptions();
            return LegacyAppCompatibilityPolicy.MatchesPath(normalized, defaultProfile.LoginPathPatterns);
        }
    }
}
