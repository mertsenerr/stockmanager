using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public static class FriendshipDurumlari
{
    public const string Beklemede = "beklemede";
    public const string Kabul = "kabul";
    public const string Red = "red";
}

public sealed class Friendship
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string FromUserId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ToUserId { get; set; } = string.Empty;

    public string Durum { get; set; } = FriendshipDurumlari.Beklemede;

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? KararTarihi { get; set; }
}
