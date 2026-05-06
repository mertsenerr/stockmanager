using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using SayimLink.Api.Controllers;
using SayimLink.Api.Dtos.Sayim;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Hubs;

[Authorize]
public sealed class SayimHub : Hub<ISayimHubClient>
{
    private readonly IOturumRepository _oturumlar;
    private readonly ICellLockService _locks;
    private readonly IAuditService _audit;
    private readonly ICallRegistry _calls;
    private readonly IFriendshipRepository _friends;
    private readonly ILogger<SayimHub> _logger;

    public SayimHub(
        IOturumRepository oturumlar,
        ICellLockService locks,
        IAuditService audit,
        ICallRegistry calls,
        IFriendshipRepository friends,
        ILogger<SayimHub> logger)
    {
        _oturumlar = oturumlar;
        _locks = locks;
        _audit = audit;
        _calls = calls;
        _friends = friends;
        _logger = logger;
    }

    private string UserId => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    private string UserAd => Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "?";
    private string UserRol => Context.User?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

    private static string GroupName(string oturumId) => $"oturum:{oturumId}";
    private static string CallGroupName(string oturumId) => $"call:{oturumId}";
    private static string UserGroupName(string userId) => $"user:{userId}";

    public async Task OturumaKatil(string oturumId)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Bu oturuma erişim yetkin yok.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(oturumId), Context.ConnectionAborted);
        await Clients.OthersInGroup(GroupName(oturumId))
            .KullaniciKatildi(oturumId, UserId, UserAd, UserRol);

        // Replay current locks to the joiner.
        var active = _locks.GetActiveLocksForOturum(oturumId);
        foreach (var l in active)
        {
            await Clients.Caller.HucreKilitlendi(
                oturumId, l.UrunId, l.Alan, l.KullaniciId, l.KullaniciAdi, l.ExpiresAt);
        }
    }

    public async Task OturumdanAyril(string oturumId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(oturumId), Context.ConnectionAborted);
        await Clients.OthersInGroup(GroupName(oturumId))
            .KullaniciAyrildi(oturumId, UserId);
    }

    public async Task HucreKilitle(string oturumId, string urunId, string alan)
    {
        var locked = _locks.Acquire(oturumId, urunId, alan, UserId, UserAd);
        await Clients.Group(GroupName(oturumId))
            .HucreKilitlendi(oturumId, urunId, alan, locked.KullaniciId, locked.KullaniciAdi, locked.ExpiresAt);
    }

    public async Task HucreSerbestBirak(string oturumId, string urunId, string alan)
    {
        _locks.Release(oturumId, urunId, alan, UserId);
        await Clients.Group(GroupName(oturumId))
            .HucreSerbestBirakildi(oturumId, urunId, alan);
    }

    public async Task UrunGuncelle(
        string oturumId, string urunId,
        decimal? sayilanStok, string? durum, string? atananSaymanId, string? yorum)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Erişim yok.");
        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal or OturumDurumlari.Kilitli)
            throw new HubException("Kapanmış/kilitli oturumda satır güncellenemez.");

        var urun = oturum.Urunler.FirstOrDefault(u => u.Id == urunId)
            ?? throw new HubException("Ürün bulunamadı.");

        var canEditDurum = UserRol == Roles.Admin;
        var canAtaSayman = UserRol == Roles.Admin || UserRol == Roles.SayimYoneticisi;
        var canEditSayilan = UserRol != Roles.Sayman || urun.Durum == UrunDurumlari.TekrarSayiliyor;

        var changes = new List<UrunDegisiklik>();

        if (sayilanStok.HasValue && sayilanStok.Value != urun.SayilanStok)
        {
            if (!canEditSayilan) throw new HubException("Bu hücreyi düzenleme yetkin yok.");
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = UserId, KullaniciAdi = UserAd, Alan = "sayilanStok",
                EskiDeger = urun.SayilanStok.ToString(CultureInfo.InvariantCulture),
                YeniDeger = sayilanStok.Value.ToString(CultureInfo.InvariantCulture),
            });
            urun.SayilanStok = sayilanStok.Value;
        }

        if (!string.IsNullOrEmpty(durum) && durum != urun.Durum)
        {
            if (!canEditDurum) throw new HubException("Durumu değiştirme yetkin yok.");
            if (!UrunDurumlari.IsValid(durum)) throw new HubException("Geçersiz durum.");
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = UserId, KullaniciAdi = UserAd, Alan = "durum",
                EskiDeger = urun.Durum, YeniDeger = durum,
            });
            urun.Durum = durum;
        }

        if (atananSaymanId is not null && atananSaymanId != urun.AtananSaymanId)
        {
            if (!canAtaSayman) throw new HubException("Sayman atayamazsın.");
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = UserId, KullaniciAdi = UserAd, Alan = "atananSayman",
                EskiDeger = urun.AtananSaymanId, YeniDeger = atananSaymanId,
            });
            urun.AtananSaymanId = string.IsNullOrEmpty(atananSaymanId) ? null : atananSaymanId;
        }

        UrunYorum? yorumEklenen = null;
        if (!string.IsNullOrWhiteSpace(yorum))
        {
            yorumEklenen = new UrunYorum
            {
                KullaniciId = UserId, KullaniciAdi = UserAd, Mesaj = yorum.Trim(),
            };
            urun.Yorumlar.Add(yorumEklenen);
        }

        if (changes.Count == 0 && yorumEklenen is null)
            return;

        urun.DegisiklikGecmisi.AddRange(changes);
        urun.SonGuncelleyenId = UserId;
        urun.GuncellenmeTarihi = DateTime.UtcNow;
        oturum.Ozetler = SayimOturumu.ComputeOzet(oturum.Urunler);

        // H-6: positional update — write only the changed Urun fields + Ozetler instead of
        // ReplaceAsync(oturum), which would rewrite the entire (potentially multi-MB) doc.
        // The ElemMatch in UpdateUrunAsync's filter binds the `$` to the matched product.
        var ub = Builders<SayimOturumu>.Update;
        var ops = new List<UpdateDefinition<SayimOturumu>>
        {
            ub.Set(o => o.Urunler[-1].SonGuncelleyenId, urun.SonGuncelleyenId),
            ub.Set(o => o.Urunler[-1].GuncellenmeTarihi, urun.GuncellenmeTarihi),
            ub.Set(o => o.Ozetler, oturum.Ozetler),
        };
        if (sayilanStok.HasValue)
            ops.Add(ub.Set(o => o.Urunler[-1].SayilanStok, urun.SayilanStok));
        if (!string.IsNullOrEmpty(durum))
            ops.Add(ub.Set(o => o.Urunler[-1].Durum, urun.Durum));
        if (atananSaymanId is not null)
            ops.Add(ub.Set(o => o.Urunler[-1].AtananSaymanId, urun.AtananSaymanId));
        if (changes.Count > 0)
            ops.Add(ub.PushEach(o => o.Urunler[-1].DegisiklikGecmisi, changes));
        if (yorumEklenen is not null)
            ops.Add(ub.Push(o => o.Urunler[-1].Yorumlar, yorumEklenen));

        await _oturumlar.UpdateUrunAsync(oturumId, urunId, ub.Combine(ops), Context.ConnectionAborted);

        // Release any held cell lock for this user/urun (best-effort).
        if (sayilanStok.HasValue) _locks.Release(oturumId, urunId, "sayilanStok", UserId);
        if (durum is not null) _locks.Release(oturumId, urunId, "durum", UserId);

        var patch = new UrunGuncellendiPatch(
            urunId,
            sayilanStok,
            urun.Fark,
            durum,
            atananSaymanId,
            urun.Yorumlar.Count,
            UserId,
            UserAd,
            DateTime.UtcNow);

        await Clients.Group(GroupName(oturumId)).UrunGuncellendi(oturumId, patch, oturum.Ozetler.ToWire());

        if (yorumEklenen is not null)
        {
            await Clients.Group(GroupName(oturumId)).YorumEklendi(
                oturumId, urunId, yorumEklenen.KullaniciId, yorumEklenen.KullaniciAdi,
                yorumEklenen.Mesaj, yorumEklenen.Tarih);
        }
    }

    // ─── Değişiklik talep akışı ─────────────────────────────────────────────
    // Kullanici (mağaza müdürü) doğrudan yazamaz; talep oluşturur.
    // SayimBaskani/Sistem talebi onaylar veya reddeder.

    public async Task TalepOlustur(string oturumId, string urunId, string alan, decimal yeniDeger, string? gerekce)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Erişim yok.");
        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal or OturumDurumlari.Kilitli)
            throw new HubException("Kapanmış/kilitli oturumda talep açılamaz.");

        if (alan != "sayilanStok")
            throw new HubException("Şimdilik sadece 'Fiili' (sayilanStok) için talep gönderilebilir.");

        var urun = oturum.Urunler.FirstOrDefault(u => u.Id == urunId)
            ?? throw new HubException("Ürün bulunamadı.");

        // Sayim başkanı/sistem direkt yazsın; talep akışı sadece Kullanici içindir.
        if (UserRol != Roles.Kullanici)
            throw new HubException("Yöneticiler değişikliği doğrudan yapar; talep göndermeniz gerekmiyor.");

        // Aynı kullanıcı, aynı hücrede açık talep tutamaz.
        if (urun.Talepler.Any(t => t.Durum == TalepDurumlari.Beklemede
                                   && t.Alan == alan
                                   && t.KullaniciId == UserId))
            throw new HubException("Bu hücre için zaten bekleyen bir talebin var.");

        if (yeniDeger == urun.SayilanStok)
            throw new HubException("Yeni değer mevcut değerle aynı.");

        var talep = new UrunDegisiklikTalebi
        {
            KullaniciId = UserId,
            KullaniciAdi = UserAd,
            Alan = alan,
            EskiDeger = urun.SayilanStok.ToString(CultureInfo.InvariantCulture),
            YeniDeger = yeniDeger.ToString(CultureInfo.InvariantCulture),
            Gerekce = string.IsNullOrWhiteSpace(gerekce) ? null : gerekce.Trim(),
        };

        // H-6: positional Push instead of ReplaceAsync — append the new request without
        // rewriting the whole oturum document.
        var pushTalep = Builders<SayimOturumu>.Update.Push(o => o.Urunler[-1].Talepler, talep);
        await _oturumlar.UpdateUrunAsync(oturumId, urunId, pushTalep, Context.ConnectionAborted);
        urun.Talepler.Add(talep);

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.TalepCreate, UserId, UserAd, UserRol,
            hedef: "oturum.urun.talep", hedefId: $"{oturumId}/{urunId}/{talep.Id}",
            yeniDeger: $"{alan}: {talep.EskiDeger} → {talep.YeniDeger}"));

        var dto = OturumlarController.ToTalepDto(talep, urunId);
        await Clients.Group(GroupName(oturumId)).TalepOlusturuldu(oturumId, dto);
    }

    public async Task TalepOnayla(string oturumId, string urunId, string talepId)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Erişim yok.");
        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal or OturumDurumlari.Kilitli)
            throw new HubException("Kapanmış/kilitli oturumda karar verilemez.");
        if (UserRol != Roles.Sistem && UserRol != Roles.SayimBaskani)
            throw new HubException("Talebi sadece sayım başkanı onaylayabilir.");

        var urun = oturum.Urunler.FirstOrDefault(u => u.Id == urunId)
            ?? throw new HubException("Ürün bulunamadı.");
        var talep = urun.Talepler.FirstOrDefault(t => t.Id == talepId)
            ?? throw new HubException("Talep bulunamadı.");
        if (talep.Durum != TalepDurumlari.Beklemede)
            throw new HubException("Bu talep zaten karara bağlanmış.");

        if (talep.Alan != "sayilanStok")
            throw new HubException("Bilinmeyen talep alanı.");
        if (!decimal.TryParse(talep.YeniDeger, NumberStyles.Number, CultureInfo.InvariantCulture, out var yeni))
            throw new HubException("Talep değeri geçersiz.");

        // Talebi karara bağla.
        talep.Durum = TalepDurumlari.Onaylandi;
        talep.KararVerenId = UserId;
        talep.KararVerenAdi = UserAd;
        talep.KararTarihi = DateTime.UtcNow;

        // Değeri uygula + history kaydı.
        var oldVal = urun.SayilanStok;
        urun.SayilanStok = yeni;
        urun.DegisiklikGecmisi.Add(new UrunDegisiklik
        {
            KullaniciId = UserId,
            KullaniciAdi = UserAd,
            Alan = "sayilanStok",
            EskiDeger = oldVal.ToString(CultureInfo.InvariantCulture),
            YeniDeger = yeni.ToString(CultureInfo.InvariantCulture),
        });
        urun.SonGuncelleyenId = UserId;
        urun.GuncellenmeTarihi = DateTime.UtcNow;
        oturum.Ozetler = SayimOturumu.ComputeOzet(oturum.Urunler);

        await _oturumlar.ReplaceAsync(oturum, Context.ConnectionAborted);

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.TalepApprove, UserId, UserAd, UserRol,
            hedef: "oturum.urun.talep", hedefId: $"{oturumId}/{urunId}/{talep.Id}",
            eskiDeger: oldVal.ToString(CultureInfo.InvariantCulture),
            yeniDeger: yeni.ToString(CultureInfo.InvariantCulture)));

        // Talep kararını ve değer değişimini yay.
        await Clients.Group(GroupName(oturumId))
            .TalepOnaylandi(oturumId, urunId, talep.Id, UserAd, talep.KararTarihi.Value);

        var patch = new UrunGuncellendiPatch(
            urunId, yeni, urun.Fark, null, null, urun.Yorumlar.Count, UserId, UserAd, DateTime.UtcNow);
        await Clients.Group(GroupName(oturumId)).UrunGuncellendi(oturumId, patch, oturum.Ozetler.ToWire());
    }

    public async Task TalepReddet(string oturumId, string urunId, string talepId, string? sebep)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Erişim yok.");
        if (UserRol != Roles.Sistem && UserRol != Roles.SayimBaskani)
            throw new HubException("Talebi sadece sayım başkanı reddedebilir.");

        var urun = oturum.Urunler.FirstOrDefault(u => u.Id == urunId)
            ?? throw new HubException("Ürün bulunamadı.");
        var talep = urun.Talepler.FirstOrDefault(t => t.Id == talepId)
            ?? throw new HubException("Talep bulunamadı.");
        if (talep.Durum != TalepDurumlari.Beklemede)
            throw new HubException("Bu talep zaten karara bağlanmış.");

        talep.Durum = TalepDurumlari.Reddedildi;
        talep.KararVerenId = UserId;
        talep.KararVerenAdi = UserAd;
        talep.KararSebep = string.IsNullOrWhiteSpace(sebep) ? null : sebep.Trim();
        talep.KararTarihi = DateTime.UtcNow;

        await _oturumlar.ReplaceAsync(oturum, Context.ConnectionAborted);

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.TalepReject, UserId, UserAd, UserRol,
            hedef: "oturum.urun.talep", hedefId: $"{oturumId}/{urunId}/{talep.Id}",
            eskiDeger: $"{talep.Alan}: {talep.EskiDeger} → {talep.YeniDeger}",
            yeniDeger: talep.KararSebep ?? "(sebep yok)"));

        await Clients.Group(GroupName(oturumId))
            .TalepReddedildi(oturumId, urunId, talep.Id, UserAd, talep.KararSebep, talep.KararTarihi.Value);
    }

    // ─── WebRTC sinyalleşme ─────────────────────────────────────────────────
    public async Task CallJoin(string oturumId)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Bu oturuma erişim yetkin yok.");

        var me = new CallParticipant(Context.ConnectionId, UserId, UserAd, UserRol);
        var existing = _calls.Join(oturumId, me);
        var firstParticipant = existing.Count == 0;

        await Groups.AddToGroupAsync(Context.ConnectionId, CallGroupName(oturumId), Context.ConnectionAborted);

        // Yeni katılımcıyı diğerlerine duyur — onlar offer hazırlayacak.
        await Clients.OthersInGroup(CallGroupName(oturumId))
            .CallParticipantJoined(oturumId, me.KullaniciId, me.KullaniciAdi, me.ConnectionId);

        // Mevcut katılımcı listesini yeni gelene gönder — peer connection nesnesi onlara hazırlanır.
        var dtoList = existing.Select(p => new CallParticipantDto(p.ConnectionId, p.KullaniciId, p.KullaniciAdi, p.Rol)).ToList();
        await Clients.Caller.CallRoster(oturumId, dtoList);

        // Aramayı ilk başlatan kişi → oturumun katılımcılarına "çalıyor" bildirimi yolla.
        // user:{id} grubuna gönderiyoruz, bu sayede kullanıcı hangi sayfada olursa olsun pop-up alır.
        if (firstParticipant)
        {
            var notifyIds = oturum.Katilimcilar
                .Select(k => k.KullaniciId)
                .Where(id => !string.IsNullOrEmpty(id) && id != UserId)
                .Distinct()
                .ToList();
            foreach (var uid in notifyIds)
            {
                await Clients.Group(UserGroupName(uid))
                    .CallRinging(oturumId, me.KullaniciId, me.KullaniciAdi);
            }
        }
    }

    public async Task CallLeave(string oturumId)
    {
        var p = _calls.Leave(Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, CallGroupName(oturumId), Context.ConnectionAborted);
        if (p is not null)
        {
            await Clients.Group(CallGroupName(oturumId))
                .CallParticipantLeft(oturumId, p.ConnectionId);
        }
    }

    /// <summary>SDP offer/answer veya ICE candidate'ı hedef connectionId'ye iletir.</summary>
    public async Task CallSignal(string oturumId, string toConnectionId, string type, string payload)
    {
        // Gönderen aramada mı kontrol et — istemediğimiz peer'ler signal akıtmasın.
        if (_calls.OturumIdOf(Context.ConnectionId) != oturumId)
            throw new HubException("Önce aramaya katılın.");

        await Clients.Client(toConnectionId)
            .CallSignal(oturumId, Context.ConnectionId, UserId, UserAd, type, payload);
    }

    /// <summary>
    /// Aramaya bireysel kullanıcı davet eder. Hedef user oturumun katılımcısı değilse otomatik eklenir
    /// (oturuma erişim verilir), sonra user-grubuna CallRinging push'u atılır.
    /// </summary>
    public async Task CallInvite(string oturumId, string hedefKullaniciId)
    {
        if (string.IsNullOrEmpty(hedefKullaniciId))
            throw new HubException("Hedef kullanıcı geçersiz.");

        var oturum = await _oturumlar.FindByIdAsync(oturumId, Context.ConnectionAborted);
        if (oturum is null) throw new HubException("Oturum bulunamadı.");
        if (!CanAccess(oturum)) throw new HubException("Erişim yok.");

        // Yetki kuralı:
        // - Sistem/SayimBaskani: oturuma katılabilen herhangi birini davet edebilir
        // - Diğer roller (Kullanici, MagazaMuduru): sadece arkadaşlarını davet edebilir
        if (UserRol != Roles.Sistem && UserRol != Roles.SayimBaskani)
        {
            var friendship = await _friends.FindBetweenAsync(UserId, hedefKullaniciId, Context.ConnectionAborted);
            if (friendship is null || friendship.Durum != FriendshipDurumlari.Kabul)
                throw new HubException("Sadece arkadaşlarını davet edebilirsin.");
        }

        // Hedef user yoksa Katilimcilar'a ekle — bu sayede oturum verilerine erişebilir.
        if (!oturum.Katilimcilar.Any(k => k.KullaniciId == hedefKullaniciId))
        {
            oturum.Katilimcilar.Add(new Katilimci
            {
                KullaniciId = hedefKullaniciId,
                Rol = "Davetli",
                AktifMi = true,
            });
            await _oturumlar.ReplaceAsync(oturum, Context.ConnectionAborted);
        }

        await Clients.Group(UserGroupName(hedefKullaniciId))
            .CallRinging(oturumId, UserId, UserAd);
    }

    private bool CanAccess(SayimOturumu oturum)
    {
        if (UserRol == Roles.Admin) return true;
        if (oturum.Katilimcilar.Any(k => k.KullaniciId == UserId)) return true;
        if (UserRol == Roles.MagazaMuduru)
        {
            var magazaIds = (Context.User?.FindFirst("magazaIds")?.Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (magazaIds.Contains(oturum.MagazaId)) return true;
        }
        return false;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR connected: {User}", UserAd);
        // Kullanıcı bazlı bildirim için (gelen arama vs.) user grubuna ekle.
        if (!string.IsNullOrEmpty(UserId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(UserId));
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR disconnected: {User}", UserAd);
        // Aramadan koparsa diğer katılımcılara bildir.
        var oturumId = _calls.OturumIdOf(Context.ConnectionId);
        var p = _calls.Leave(Context.ConnectionId);
        if (oturumId is not null && p is not null)
        {
            await Clients.Group(CallGroupName(oturumId))
                .CallParticipantLeft(oturumId, p.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}

public sealed record CallParticipantDto(
    string ConnectionId,
    string KullaniciId,
    string KullaniciAdi,
    string Rol);

public sealed record UrunGuncellendiPatch(
    string UrunId,
    decimal? SayilanStok,
    decimal Fark,
    string? Durum,
    string? AtananSaymanId,
    int YorumSayisi,
    string KullaniciId,
    string KullaniciAdi,
    DateTime Tarih);

public sealed record OturumOzetWire(
    int ToplamUrun, int Beklemede, int TekrarSayilan, int Onaylanmis,
    int IptalEdilmis, int Inceleme, decimal ToplamFarkPozitif, decimal ToplamFarkNegatif);

public static class OzetExt
{
    public static OturumOzetWire ToWire(this OturumOzet o) => new(
        o.ToplamUrun, o.BeklemedeSayisi, o.TekrarSayilan, o.Onaylanmis,
        o.IptalEdilmis, o.Inceleme, o.ToplamFarkPozitif, o.ToplamFarkNegatif);
}

public interface ISayimHubClient
{
    Task KullaniciKatildi(string oturumId, string kullaniciId, string kullaniciAdi, string rol);
    Task KullaniciAyrildi(string oturumId, string kullaniciId);
    Task HucreKilitlendi(string oturumId, string urunId, string alan, string kullaniciId, string kullaniciAdi, DateTime expiresAt);
    Task HucreSerbestBirakildi(string oturumId, string urunId, string alan);
    Task UrunGuncellendi(string oturumId, UrunGuncellendiPatch patch, OturumOzetWire ozet);
    Task YorumEklendi(string oturumId, string urunId, string kullaniciId, string kullaniciAdi, string mesaj, DateTime tarih);
    Task OturumDurumuDegisti(string oturumId, string yeniDurum);

    Task TalepOlusturuldu(string oturumId, UrunDegisiklikTalebiDto talep);
    Task TalepOnaylandi(string oturumId, string urunId, string talepId, string kararVerenAdi, DateTime kararTarihi);
    Task TalepReddedildi(string oturumId, string urunId, string talepId, string kararVerenAdi, string? sebep, DateTime kararTarihi);

    // WebRTC sinyalleşme
    Task CallRoster(string oturumId, IReadOnlyList<CallParticipantDto> participants);
    Task CallParticipantJoined(string oturumId, string kullaniciId, string kullaniciAdi, string connectionId);
    Task CallParticipantLeft(string oturumId, string connectionId);
    Task CallSignal(string oturumId, string fromConnectionId, string fromKullaniciId, string fromKullaniciAdi, string type, string payload);
    Task CallRinging(string oturumId, string baslatanKullaniciId, string baslatanKullaniciAdi);
}
