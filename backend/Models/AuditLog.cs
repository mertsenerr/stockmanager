using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public static class AuditAksiyonlari
{
    public const string LoginSuccess = "auth.login.ok";
    public const string LoginFail = "auth.login.fail";
    public const string Logout = "auth.logout";
    public const string PasswordReset = "auth.password.reset";

    public const string FirmaCreate = "firma.create";
    public const string FirmaUpdate = "firma.update";
    public const string FirmaDelete = "firma.delete";

    public const string MagazaCreate = "magaza.create";
    public const string MagazaUpdate = "magaza.update";
    public const string MagazaDelete = "magaza.delete";

    public const string KullaniciCreate = "kullanici.create";
    public const string KullaniciUpdate = "kullanici.update";
    public const string KullaniciDelete = "kullanici.delete";

    public const string AtamaCreate = "atama.create";
    public const string AtamaUpdate = "atama.update";
    public const string AtamaMoveDate = "atama.move-date";
    public const string AtamaDelete = "atama.delete";

    public const string OturumCreate = "oturum.create";
    public const string OturumUpdate = "oturum.update";
    public const string OturumDurumChange = "oturum.durum-change";
    public const string OturumExcelImport = "oturum.excel-import";
    public const string OturumDelete = "oturum.delete";
    public const string UrunUpdate = "oturum.urun-update";
    public const string UrunDelete = "oturum.urun-delete";

    public const string TalepCreate = "oturum.talep.olustur";
    public const string TalepApprove = "oturum.talep.onayla";
    public const string TalepReject = "oturum.talep.reddet";

    public const string OzelRaporCreate = "ozel-rapor.create";
    public const string OzelRaporUpdate = "ozel-rapor.update";
    public const string OzelRaporDelete = "ozel-rapor.delete";
    public const string OzelRaporFileAdd = "ozel-rapor.file.add";
    public const string OzelRaporFileDelete = "ozel-rapor.file.delete";
    public const string OzelRaporDownload = "ozel-rapor.download";

    public const string BelgeTipiCreate = "belge-tipi.create";
    public const string BelgeTipiUpdate = "belge-tipi.update";
    public const string BelgeTipiArchive = "belge-tipi.archive";
    public const string BelgeTipiRestore = "belge-tipi.restore";

    public const string OzelRaporImzaAt = "ozel-rapor.imza.at";
    public const string OzelRaporImzaSil = "ozel-rapor.imza.sil";
    public const string OzelRaporKaseBas = "ozel-rapor.kase.bas";
    public const string OzelRaporKaseSil = "ozel-rapor.kase.sil";
    public const string OzelRaporImzaliDownload = "ozel-rapor.imzali.download";
}

public sealed class AuditLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public DateTime Tarih { get; set; } = DateTime.UtcNow;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? KullaniciId { get; set; }
    public string KullaniciAdi { get; set; } = string.Empty;
    public string KullaniciRol { get; set; } = string.Empty;

    public string Aksiyon { get; set; } = string.Empty;
    public string? Hedef { get; set; }
    public string? HedefId { get; set; }
    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }

    public string? IpAdres { get; set; }
    public string? UserAgent { get; set; }
    public bool Basarili { get; set; } = true;
}
