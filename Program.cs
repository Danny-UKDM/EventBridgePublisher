using System.Net;
using System.Text.Json.Nodes;
using Amazon;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;

Console.WriteLine("Enter AWS profile name:");
var enteredProfile = Console.ReadLine();

Console.WriteLine("Looking for credentials...");
var roleCredentials = GetCredentials(enteredProfile);
Console.WriteLine("Credentials found");

Console.WriteLine("Creating event bridge client...");
var client = GetEventBridgeClient(roleCredentials);
Console.WriteLine("Client created");

Console.WriteLine("Getting events from directory...");
var eventsToPut = await GetEvents();
Console.WriteLine($"{eventsToPut.Length} events found");

if (eventsToPut.Any())
{
    Console.WriteLine("Putting events to event bridge...");
    await PutEvents(eventsToPut);
    Console.WriteLine("Completed");
    Console.ReadKey();
}
else
{
    Console.WriteLine("No events to send - restart application to send new events");
    Console.ReadKey();
}

AssumeRoleAWSCredentials GetCredentials(string profileName)
{
    var credentialStore = new CredentialProfileStoreChain();

    if (!credentialStore.TryGetProfile(profileName, out var profile))
        throw new Exception($"Could not find AWS Profile, no profile named '{profileName}'");

    var remoteAwsCredentials = AWSCredentialsFactory.GetAWSCredentials(profile, credentialStore);
    if (remoteAwsCredentials is not AssumeRoleAWSCredentials assumeRoleCredentials) return null;

    assumeRoleCredentials.Options.MfaTokenCodeCallback = () => {

        var code = string.Empty;
        ConsoleKeyInfo key;

        Console.Write($"Enter MFA code for '{profileName}': ");

        do
        {
            key = Console.ReadKey(true);
            if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
            {
                code += key.KeyChar;
                Console.Write("*");
            }
            else
            {
                if (key.Key != ConsoleKey.Backspace || code.Length <= 0) continue;

                code = code[..^1];
                Console.Write("\b \b");
            }
        } while (key.Key != ConsoleKey.Enter);

        Console.WriteLine();
        return code;
    };
    return assumeRoleCredentials;
}

AmazonEventBridgeClient GetEventBridgeClient(AWSCredentials credentials) => new(credentials, new AmazonEventBridgeConfig { RegionEndpoint = RegionEndpoint.EUWest1 });

async Task<string[]> GetEvents()
{
    var filePaths = Directory.GetFiles("events").OrderBy(s => s);

    var events = new List<string>();

    foreach (var filePath in filePaths)
        events.Add(await File.ReadAllTextAsync(filePath));

    return events.ToArray();
}

async Task PutEvents(string[] events)
{
    foreach (var @event in events)
    {
        const string eventBusName = "marketplace-event-bus";
        const string eventSource = "EVENT-BRIDGE-PUBLISHER-TOOL";

        var node = JsonNode.Parse(@event);
        var detailType = node!["detail"]!["metadata"]!["type"]!.GetValue<string>();
        var status = node!["detail"]!["metadata"]!["status"]!.GetValue<string>();

        var putEventsRequest = new PutEventsRequest
        {
            Entries = new List<PutEventsRequestEntry> { new()
                {
                    EventBusName = eventBusName,
                    Source = eventSource,
                    DetailType = detailType,
                    Detail = @event
                }
            }
        };

        Console.WriteLine($"Putting event with type '{detailType}' & status '{status}'");

        var putEventsResponse = await client.PutEventsAsync(putEventsRequest);

        if (putEventsResponse.HttpStatusCode != HttpStatusCode.OK)
        {
            Console.WriteLine("Event bridge client received non-OK response when putting event");
        }
    }
}
