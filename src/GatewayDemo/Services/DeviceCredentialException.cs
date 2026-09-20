namespace GatewayDemo.Services;

public sealed class DeviceCredentialException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
