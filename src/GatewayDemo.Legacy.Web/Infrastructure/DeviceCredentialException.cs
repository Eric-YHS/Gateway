using System;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class DeviceCredentialException : Exception
    {
        public DeviceCredentialException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; private set; }
    }
}
