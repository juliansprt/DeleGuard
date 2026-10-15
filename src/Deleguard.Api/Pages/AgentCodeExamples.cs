using System.Text.Json;

namespace Deleguard.Api.Pages;

public static class AgentCodeExamples
{
    public static IReadOnlyList<(string Language,string Code)> Build(string url,string agent,string environment,string action,string resourceType)
    {
        var body=JsonSerializer.Serialize(new{agent=new{id=agent,environment},action,resource=new{type=resourceType,id="your-resource-id",attributes=new{}},context=new{source="application"}},new JsonSerializerOptions{WriteIndented=true});
        var endpoint=JsonSerializer.Serialize(url.TrimEnd('/')+"/v1/authorize");
        return [
            ("Python",$$"""
            import getpass, json, urllib.request, uuid

            key = getpass.getpass("DeleGuard API key: ")
            payload = json.loads({{JsonSerializer.Serialize(body)}})
            request = urllib.request.Request({{endpoint}},
                data=json.dumps(payload).encode(), method="POST",
                headers={"Authorization": "Bearer " + key,
                         "Content-Type": "application/json",
                         "Idempotency-Key": str(uuid.uuid4())})
            with urllib.request.urlopen(request, timeout=10) as response:
                decision = json.load(response)
            print(decision["decision"], decision["decision_id"])
            if decision["decision"] != "ALLOW":
                raise RuntimeError("Operation paused or denied")
            # Add your protected operation here, then report its actual result.
            """),
            ("JavaScript",$$"""
            // Server-side JavaScript. Supply the key only to this process.
            const key = process.env.DELEGUARD_API_KEY;
            if (!key) throw new Error("Missing API key");
            const payload = {{body}};
            const response = await fetch({{endpoint}}, {
              method: "POST", signal: AbortSignal.timeout(10000),
              headers: { "Authorization": `Bearer ${key}`,
                "Content-Type": "application/json",
                "Idempotency-Key": crypto.randomUUID() },
              body: JSON.stringify(payload)
            });
            if (!response.ok) throw new Error("Authorization unavailable");
            const decision = await response.json();
            console.log(decision.decision, decision.decision_id);
            if (decision.decision !== "ALLOW") throw new Error("Operation paused or denied");
            // Add your protected operation here, then report its actual result.
            """),
            (".NET",$$"""
            using System.Net.Http.Headers;
            using System.Text;
            using System.Text.Json;

            var key = Environment.GetEnvironmentVariable("DELEGUARD_API_KEY")
                ?? throw new InvalidOperationException("Missing API key");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, {{endpoint}});
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            request.Content = new StringContent({{JsonSerializer.Serialize(body)}}, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var decision = json.RootElement;
            Console.WriteLine(decision.GetProperty("decision").GetString());
            if (decision.GetProperty("decision").GetString() != "ALLOW")
                throw new InvalidOperationException("Operation paused or denied");
            // Add your protected operation here, then report its actual result.
            """)];
    }
}
