// The same call from .NET Framework 4.8.1, which loads the netstandard2.0 build of TypeSafeSharp.
// Written in C# 7.3 on purpose (see NetFramework.csproj), so no top-level statements, no
// `using var`, no nullable annotations.
// Needs TYPESAFE_API_KEY. Run on Windows: dotnet run --project src/NetFramework
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TypeSafeSharp;

namespace NetFramework
{
    internal static class Program
    {
        private static async Task<int> Main()
        {
            // .NET Framework 4.7.2 and later use the OS TLS defaults, which include TLS 1.2. Older
            // versions need the SystemDefaultTlsVersions registry setting before this call will connect.
            using (var client = new TypeSafeClient(new TypeSafeClientOptions()))
            {
                var request = new SystemOneRequest(
                    "The invoice total does not match the purchase order.",
                    new Dictionary<string, Question>
                    {
                        ["billing"] = Question.Noul("Is this about billing?"),
                    })
                {
                    // A plain setter, not init, because C# 7.3 cannot call init accessors.
                    Model = "jev-latest",
                };

                var response = await client.SystemOneAsync(request);

                // Not Environment.Version, which is 4.0.30319.42000 on every .NET Framework 4.x.
                Console.WriteLine($"billing: {response.GetNoul("billing").Noul:P0} ({response.Model} on {RuntimeInformation.FrameworkDescription})");
                return 0;
            }
        }
    }
}
