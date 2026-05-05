using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public sealed class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = Roles.Kullanici;

    // Yeni model: birincil firma. Eski FirmaIds geri uyumluluk için tutuluyor.
    [BsonRepresentation(BsonType.ObjectId)]
    public string? FirmaId { get; set; }

    public List<string> FirmaIds { get; set; } = [];
    public List<string> MagazaIds { get; set; } = [];

    public bool AktifMi { get; set; } = true;
    /// <summary>Sayım Başkanı tarafından onaylandı mı? Sistem/SayimBaskani için true; yeni Kullanici kayıtları için false.</summary>
    public bool Onayli { get; set; } = true;

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? SonGirisTarihi { get; set; }

    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
}
