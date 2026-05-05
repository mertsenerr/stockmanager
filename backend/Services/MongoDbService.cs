using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services;

public interface IMongoDbService
{
    IMongoDatabase Database { get; }
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}

public sealed class MongoDbService : IMongoDbService
{
    private readonly IMongoClient _client;

    public MongoDbService(IOptions<MongoDbSettings> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException("MongoDb:ConnectionString is not configured.");
        if (string.IsNullOrWhiteSpace(settings.DatabaseName))
            throw new InvalidOperationException("MongoDb:DatabaseName is not configured.");

        _client = new MongoClient(settings.ConnectionString);
        Database = _client.GetDatabase(settings.DatabaseName);
    }

    public IMongoDatabase Database { get; }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
