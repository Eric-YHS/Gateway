using System;
using System.IO;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal sealed class GatewayClientDisconnectedException : IOException
    {
        public GatewayClientDisconnectedException(Exception innerException)
            : base("客户端在上传请求体时中断连接。", innerException)
        {
        }
    }
}
