using GatewayDemo.Models;

namespace GatewayDemo.Extensions;

public static class HttpContextDeviceExtensions
{
    private const string DeviceContextKey = "__gateway_device_context";

    public static void SetDeviceContext(this HttpContext context, DeviceContext deviceContext)
    {
        context.Items[DeviceContextKey] = deviceContext;
    }

    public static DeviceContext GetRequiredDeviceContext(this HttpContext context)
    {
        return context.Items.TryGetValue(DeviceContextKey, out var value) && value is DeviceContext device
            ? device
            : throw new InvalidOperationException("Device context is not available.");
    }
}
