using Boney.Delivery;
using System.Text;

if (args is ["policy"])
{
    Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    Environment.ExitCode = await DeliveryPolicyProtocol.RunAsync(Console.In, Console.Out);
    return;
}

try
{
    var result = await DeliveryCommandLine.RunAsync(args);
    Console.WriteLine(result.ToJsonString());
    if (result["status"]?.GetValue<string>() is "blocked" or "rolled-back") Environment.ExitCode = 1;
}
catch
{
    Console.Error.WriteLine("Delivery refused; inspect the admission policy or retained release receipt.");
    Environment.ExitCode = 1;
}
