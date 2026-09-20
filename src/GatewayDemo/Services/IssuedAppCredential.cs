namespace GatewayDemo.Services;

public sealed record IssuedAppCredential(
    string DeviceCode,
    string AppKeyId,
    string AppSecret,
    long ExampleTimestamp,
    string ExampleNonce,
    string ExampleBodyHash,
    string ExamplePayload,
    string ExampleSignature);
