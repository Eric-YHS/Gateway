using System;
using System.Collections.Generic;
using System.Linq;

namespace GatewayDemo.Legacy.Core.Options
{
    public sealed class LegacyAppProfileOptions
    {
        public bool Enabled { get; set; }
        public IList<string> LoginPathPatterns { get; set; }
        public IList<string> BootstrapPathPatterns { get; set; }
        public IList<string> BootstrapActions { get; set; }
        public IList<string> VersionActions { get; set; }
        public IList<string> TenantNameActions { get; set; }
        public IList<string> ServicePathPatterns { get; set; }
        public IList<string> UploadPathPatterns { get; set; }
        public IList<string> RootCandidatePathPatterns { get; set; }
        public IList<string> AnonymousStaticSubResourcePathPatterns { get; set; }
        public IList<string> MobileUserAgentMarkers { get; set; }
        public IList<string> ActionFields { get; set; }
        public IList<string> MachineIdFields { get; set; }
        public IList<string> DeviceImeiFields { get; set; }
        public IList<string> DataCenterIdFields { get; set; }
        public IList<string> UrlBeforeFields { get; set; }
        public IList<string> TenantNameFields { get; set; }
        public IList<string> CompanyFields { get; set; }
        public IList<string> UserIdFields { get; set; }
        public IList<string> UserNameFields { get; set; }
        public IList<string> MobileUserNumFields { get; set; }
        public IList<string> AppKeyFields { get; set; }
        public IList<string> DeviceVersionFields { get; set; }
        public IList<string> AppVersionFields { get; set; }
        public IList<string> AppNameFields { get; set; }
        public IList<string> DeviceNoFields { get; set; }
        public IList<string> ContactFields { get; set; }
        public IList<string> SessionKeyFields { get; set; }
        public IList<string> SessionKeyJsonPaths { get; set; }
        public string AutoRequestApplicantName { get; set; }
        public string AutoRequestLoginReason { get; set; }
        public string AutoRequestProtectedApiReason { get; set; }
        public LegacyAppTenantLookupOptions TenantLookup { get; set; }

        public LegacyAppProfileOptions()
        {
            Enabled = true;
            LoginPathPatterns = new List<string> { "/user/login", "/user/logout", "/user/changepwd", "/login" };
            BootstrapPathPatterns = new List<string> { "/sys.ashx", "/ashx/sys.ashx" };
            BootstrapActions = new List<string> { "version", "fileversion", "tenant_name" };
            VersionActions = new List<string> { "version", "fileversion" };
            TenantNameActions = new List<string> { "tenant_name" };
            ServicePathPatterns = new List<string> { "*/wcfservice/postbus.*", "*/wcfservice/*", "*.svc" };
            UploadPathPatterns = new List<string> { "/ashx/*", "*/ashx/*" };
            RootCandidatePathPatterns = new List<string>
            {
                "*/sys.ashx",
                "*/ashx/sys.ashx",
                "*/login",
                "/user/*",
                "*/wcfservice/*",
                "/ashx/*",
                "*.ashx",
                "*.svc",
                "/default.aspx*"
            };
            AnonymousStaticSubResourcePathPatterns = new List<string>
            {
                "/Resource/*",
                "/assets/*",
                "/static/*",
                "/css/*",
                "/js/*",
                "/scripts/*",
                "/images/*",
                "/img/*",
                "/fonts/*",
                "/lib/*",
                "/content/*"
            };
            MobileUserAgentMarkers = new List<string>();
            ActionFields = new List<string> { "action", "Action" };
            MachineIdFields = new List<string> { "MachineId", "machineId" };
            DeviceImeiFields = new List<string> { "DeviceImei", "device_imei", "device_id", "deviceId", "DeviceId", "imei" };
            DataCenterIdFields = new List<string> { "DataCenterId", "dataCenterId", "DataCenterID", "datacenterid", "DbUuid", "db_uuid", "dbUuid" };
            UrlBeforeFields = new List<string> { "UrlBefore", "urlBefore", "serverURL", "site_url", "siteUrl", "Upn", "upn" };
            TenantNameFields = new List<string> { "tenant_name", "TenantName", "tenantName" };
            CompanyFields = new List<string>
            {
                "CompanyId",
                "companyId",
                "CompanyID",
                "CompanyCode",
                "companyCode",
                "CompanyNo",
                "companyNo",
                "EnterpriseId",
                "enterpriseId",
                "EnterpriseCode",
                "enterpriseCode",
                "EnterpriseNo",
                "enterpriseNo",
                "EntId",
                "entId",
                "EntCode",
                "entCode",
                "CorpId",
                "corpId",
                "CorpCode",
                "corpCode",
                "OrgId",
                "orgId",
                "OrgCode",
                "orgCode",
                "TenantId",
                "tenantId",
                "TenantCode",
                "tenantCode",
                "CustomerId",
                "customerId",
                "CustomerCode",
                "customerCode",
                "QyId",
                "qyId",
                "QyCode",
                "qyCode",
                "企业号",
                "企业编号",
                "企业ID",
                "单位号",
                "客户号"
            };
            UserIdFields = new List<string> { "UserId", "userId", "user_code", "usercode", "userCode" };
            UserNameFields = new List<string> { "UserName", "userName", "username", "RealName", "realName", "Name", "name", "NickName", "nickName", "姓名" };
            MobileUserNumFields = new List<string> { "MobileUserNum", "mobileUserNum" };
            AppKeyFields = new List<string> { "TrAppKey", "appKey" };
            DeviceVersionFields = new List<string> { "DeviceVersion", "device_version" };
            AppVersionFields = new List<string> { "TrVersion" };
            AppNameFields = new List<string> { "TrAppName", "appName" };
            DeviceNoFields = new List<string> { "DeviceNo", "device_no" };
            ContactFields = new List<string> { "Phone", "phone", "Mobile", "mobile", "MobilePhone", "mobilePhone", "Telephone", "telephone", "Tel", "tel", "UserPhone", "userPhone" };
            SessionKeyFields = new List<string> { "userToken", "SessionKey" };
            SessionKeyJsonPaths = new List<string> { "UserToken.SessionKey", "UserToken.UserToken", "SessionKey", "UserToken", "data.SessionKey", "data.UserToken" };
            AutoRequestApplicantName = "移动APP";
            AutoRequestLoginReason = "移动客户端自动发起接入申请。";
            AutoRequestProtectedApiReason = "移动客户端访问受保护接口时自动发起接入申请。";
            TenantLookup = new LegacyAppTenantLookupOptions();
        }

        public void Normalize()
        {
            LoginPathPatterns = NormalizeList(LoginPathPatterns);
            BootstrapPathPatterns = NormalizeList(BootstrapPathPatterns);
            BootstrapActions = NormalizeList(BootstrapActions);
            VersionActions = NormalizeList(VersionActions);
            TenantNameActions = NormalizeList(TenantNameActions);
            ServicePathPatterns = NormalizeList(ServicePathPatterns);
            UploadPathPatterns = NormalizeList(UploadPathPatterns);
            RootCandidatePathPatterns = NormalizeList(RootCandidatePathPatterns);
            AnonymousStaticSubResourcePathPatterns = NormalizeList(AnonymousStaticSubResourcePathPatterns);
            MobileUserAgentMarkers = NormalizeList(MobileUserAgentMarkers);
            ActionFields = NormalizeList(ActionFields);
            MachineIdFields = NormalizeList(MachineIdFields);
            DeviceImeiFields = NormalizeList(DeviceImeiFields);
            DataCenterIdFields = NormalizeList(DataCenterIdFields);
            UrlBeforeFields = NormalizeList(UrlBeforeFields);
            TenantNameFields = NormalizeList(TenantNameFields);
            CompanyFields = NormalizeList(CompanyFields);
            UserIdFields = NormalizeList(UserIdFields);
            UserNameFields = NormalizeList(UserNameFields);
            MobileUserNumFields = NormalizeList(MobileUserNumFields);
            AppKeyFields = NormalizeList(AppKeyFields);
            DeviceVersionFields = NormalizeList(DeviceVersionFields);
            AppVersionFields = NormalizeList(AppVersionFields);
            AppNameFields = NormalizeList(AppNameFields);
            DeviceNoFields = NormalizeList(DeviceNoFields);
            ContactFields = NormalizeList(ContactFields);
            SessionKeyFields = NormalizeList(SessionKeyFields);
            SessionKeyJsonPaths = NormalizeList(SessionKeyJsonPaths);
            AutoRequestApplicantName = string.IsNullOrWhiteSpace(AutoRequestApplicantName)
                ? "移动APP"
                : AutoRequestApplicantName.Trim();
            AutoRequestLoginReason = string.IsNullOrWhiteSpace(AutoRequestLoginReason)
                ? "移动客户端自动发起接入申请。"
                : AutoRequestLoginReason.Trim();
            AutoRequestProtectedApiReason = string.IsNullOrWhiteSpace(AutoRequestProtectedApiReason)
                ? "移动客户端访问受保护接口时自动发起接入申请。"
                : AutoRequestProtectedApiReason.Trim();
            TenantLookup = TenantLookup ?? new LegacyAppTenantLookupOptions();
            TenantLookup.Normalize();
        }

        private static IList<string> NormalizeList(IEnumerable<string> values)
        {
            return (values ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
