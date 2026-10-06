// AC-4.4: C# 7.3 on .NET Framework 4.8.1, with no System.Net.Http reference, against the packed
// netstandard2.0 build. The key is fake; this program never reads TYPESAFE_API_KEY.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TypeSafeSharp;

namespace Consumer
{
    internal static class Program
    {
        private static async Task<int> Main()
        {
            using (var server = new StubServer())
            {
                var options = new TypeSafeClientOptions
                {
                    ApiKey = "ts_test_0000000000000000",
                    BaseUrl = server.BaseUrl,
                };
                options.Retry.MaxRetries = 1;

                using (var client = new TypeSafeClient(options))
                {
                    var questions = new Dictionary<string, Question>
                    {
                        { "is_urgent", Question.Noul("Does this convey urgency?") },
                        { "department", Question.Choice("Which team should handle this?", "billing", "technical") },
                    };
                    var request = new SystemOneRequest("Help! My payouts have been failing for 3 days.", questions);
                    request.Model = "jev-1.13.0";

                    var response = await client.SystemOneAsync(request).ConfigureAwait(false);

                    if (response.GetNoul("is_urgent").Noul != 0.95
                        || response.GetChoice("department").Choice != "billing"
                        || response.RequestId != StubServer.RequestId)
                    {
                        Console.Error.WriteLine("FAIL: unexpected answer");
                        return 1;
                    }
                }
            }

            Console.WriteLine("PASS (NetFx)");
            return 0;
        }
    }
}
