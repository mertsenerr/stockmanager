using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Sayim;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/oturumlar")]
public sealed class OturumlarController : ControllerBase
{
    private readonly IOturumRepository _oturumlar;
    private readonly IMagazaRepository _magazalar;
    private readonly IFirmaRepository _firmalar;
    private readonly IUserRepository _users;
    private readonly IAtamaRepository _atamalar;
    private readonly IAuditService _audit;
    private readonly IValidator<OturumCreateRequest> _createValidator;
    private readonly IValidator<OturumUpdateRequest> _updateValidator;
    private readonly IValidator<OturumDurumChangeRequest> _durumValidator;
    private readonly IValidator<ExcelImportRequest> _excelValidator;
    private readonly IValidator<UrunPatchRequest> _urunValidator;

    public OturumlarController(
        IOturumRepository oturumlar,
        IMagazaRepository magazalar,
        IFirmaRepository firmalar,
        IUserRepository users,
        IAtamaRepository atamalar,
        IAuditService audit,
        IValidator<OturumCreateRequest> createValidator,
        IValidator<OturumUpdateRequest> updateValidator,
        IValidator<OturumDurumChangeRequest> durumValidator,
        IValidator<ExcelImportRequest> excelValidator,
        IValidator<UrunPatchRequest> urunValidator)
    {
        _oturumlar = oturumlar;
        _magazalar = magazalar;
        _firmalar = firmalar;
        _users = users;
        _atamalar = atamalar;
        _audit = audit;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _durumValidator = durumValidator;
        _excelValidator = excelValidator;
        _urunValidator = urunValidator;
    }

    private void Audit(string aksiyon, string? hedefId, string? eski = null, string? yeni = null) =>
        _audit.Enqueue(_audit.Build(
            aksiyon,
            CurrentUserId(),
            User.FindFirst(ClaimTypes.Name)?.Value,
            User.FindFirst(ClaimTypes.Role)?.Value,
            hedef: "oturum", hedefId: hedefId,
            eskiDeger: eski, yeniDeger: yeni));

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? magazaId,
        [FromQuery] string? durum,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        DateTime? fromUtc = TryParseDate(from);
        DateTime? toUtc = TryParseDate(to);

        // C-1: Only Sistem (platform super-admin) sees everything. SayimBaskani falls through
        // to the per-participant filter — they see oturums where they're a Katilimci, an
        // assigned Magaza member, or a DavetliMail target. Creators are auto-added as
        // Katilimci on Create, so SayimBaskani retains visibility for sessions they own.
        IReadOnlyList<SayimOturumu> oturumlar;
        if (User.IsSistem())
        {
            oturumlar = await _oturumlar.ListAsync(magazaId, durum, fromUtc, toUtc, ct);
        }
        else
        {
            var uid = CurrentUserId();
            if (uid is null) return Unauthorized();

            // DB'den oku (JWT claim eski mağaza atamalarını taşımaz).
            var dbUser = await _users.FindByIdAsync(uid, ct);
            var myEmail = dbUser?.Email?.ToLowerInvariant()
                ?? User.FindFirst(ClaimTypes.Email)?.Value?.ToLowerInvariant();
            var myMagazaIds = dbUser?.MagazaIds ?? new List<string>();

            var participating = await _oturumlar.ListWhereUserParticipatesAsync(uid, ct);

            // Mağaza müdürü olarak atanan mağazaların tüm oturumları.
            if (myMagazaIds.Count > 0)
            {
                var byMagaza = await _oturumlar.ListByMagazaIdsAsync(myMagazaIds, ct);
                participating = participating.Concat(byMagaza)
                    .GroupBy(o => o.Id).Select(g => g.First()).ToList();
            }

            // E-posta ile davet edilen oturumlar.
            if (!string.IsNullOrEmpty(myEmail))
            {
                var all = await _oturumlar.ListAsync(magazaId: null, durum: null, fromUtc: null, toUtc: null, ct);
                var davetliOturumlar = all.Where(o => o.DavetliMailler.Any(m =>
                    string.Equals(m, myEmail, StringComparison.OrdinalIgnoreCase)));
                participating = participating.Concat(davetliOturumlar)
                    .GroupBy(o => o.Id).Select(g => g.First()).ToList();
            }

            oturumlar = participating
                .Where(o => string.IsNullOrEmpty(magazaId) || o.MagazaId == magazaId)
                .Where(o => string.IsNullOrEmpty(durum) || o.Durum == durum)
                .Where(o => !fromUtc.HasValue || o.Tarih >= fromUtc.Value)
                .Where(o => !toUtc.HasValue || o.Tarih < toUtc.Value)
                .OrderByDescending(o => o.Tarih).ToList();
        }

        return Ok(await EnrichListAsync(oturumlar, ct));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        if (!await CanReadAsync(oturum, ct)) return Forbid();

        return Ok(await EnrichDetailAsync(oturum, ct));
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SayimYoneticisi}")]
    public async Task<IActionResult> Create([FromBody] OturumCreateRequest request, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var magaza = await _magazalar.FindByIdAsync(request.MagazaId, ct);
        if (magaza is null) return BadRequest(new { message = "Mağaza bulunamadı." });

        // Non-Sistem callers may only open oturums under firmalar they own — otherwise a
        // SayimBaskani who guessed another tenant's MagazaId could attach an oturum there.
        if (!User.IsSistem())
        {
            var uid = CurrentUserId();
            var firma = await _firmalar.FindByIdAsync(magaza.FirmaId, ct);
            if (firma is null || firma.OlusturanKullaniciId != uid) return Forbid();
        }

        var oturum = new SayimOturumu
        {
            MagazaId = magaza.Id,
            FirmaId = magaza.FirmaId,
            AtamaId = string.IsNullOrEmpty(request.AtamaId) ? null : request.AtamaId,
            Tarih = ParseUtcMidnight(request.Tarih),
            Durum = OturumDurumlari.ExcelBekleniyor,
            Katilimcilar = request.Katilimcilar
                .Where(k => !string.IsNullOrWhiteSpace(k.KullaniciId))
                .Select(k => new Katilimci { KullaniciId = k.KullaniciId, Rol = k.Rol })
                .ToList(),
            DavetliMailler = NormalizeEmails(request.DavetliMailler),
            OlusturanId = CurrentUserId() ?? string.Empty,
        };
        // Ensure creator is a participant.
        var creatorId = CurrentUserId();
        if (creatorId is not null && !oturum.Katilimcilar.Any(k => k.KullaniciId == creatorId))
        {
            oturum.Katilimcilar.Add(new Katilimci
            {
                KullaniciId = creatorId,
                Rol = Roles.IsAdminLevel(User) ? Roles.Admin : Roles.SayimYoneticisi,
            });
        }

        await _oturumlar.InsertAsync(oturum, ct);
        Audit(AuditAksiyonlari.OturumCreate, oturum.Id, yeni: $"{magaza.Ad} · {oturum.Tarih:yyyy-MM-dd}");
        return CreatedAtAction(nameof(Get), new { id = oturum.Id }, await EnrichDetailAsync(oturum, ct));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SayimYoneticisi}")]
    public async Task<IActionResult> Update(string id, [FromBody] OturumUpdateRequest request, CancellationToken ct)
    {
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        if (!await CanWriteStructuralAsync(oturum, ct)) return Forbid();
        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal)
            return Conflict(new { message = "Kapanmış oturum düzenlenemez." });

        oturum.Tarih = ParseUtcMidnight(request.Tarih);
        oturum.AtamaId = string.IsNullOrEmpty(request.AtamaId) ? null : request.AtamaId;
        oturum.Katilimcilar = request.Katilimcilar
            .Where(k => !string.IsNullOrWhiteSpace(k.KullaniciId))
            .Select(k => new Katilimci { KullaniciId = k.KullaniciId, Rol = k.Rol })
            .ToList();
        oturum.DavetliMailler = NormalizeEmails(request.DavetliMailler);

        await _oturumlar.ReplaceAsync(oturum, ct);
        return Ok(await EnrichDetailAsync(oturum, ct));
    }

    [HttpPatch("{id}/durum")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> ChangeDurum(
        string id, [FromBody] OturumDurumChangeRequest request, CancellationToken ct)
    {
        var validation = await _durumValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        if (!await CanWriteStructuralAsync(oturum, ct)) return Forbid();

        if (!IsValidTransition(oturum.Durum, request.Durum))
            return Conflict(new
            {
                message = $"Geçersiz durum geçişi: {oturum.Durum} → {request.Durum}",
            });

        var oldDurum = oturum.Durum;
        await _oturumlar.UpdateDurumAsync(id, request.Durum, ct);
        oturum.Durum = request.Durum;
        Audit(AuditAksiyonlari.OturumDurumChange, id, eski: oldDurum, yeni: request.Durum);
        return Ok(await EnrichDetailAsync(oturum, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> SoftCancel(string id, CancellationToken ct)
    {
        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        if (!await CanWriteStructuralAsync(oturum, ct)) return Forbid();
        await _oturumlar.UpdateDurumAsync(id, OturumDurumlari.Iptal, ct);
        Audit(AuditAksiyonlari.OturumDelete, id, eski: oturum.Durum, yeni: OturumDurumlari.Iptal);
        return NoContent();
    }

    [HttpDelete("{id}/permanent")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> HardDelete(string id, CancellationToken ct)
    {
        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        if (!await CanWriteStructuralAsync(oturum, ct)) return Forbid();
        var deleted = await _oturumlar.HardDeleteAsync(id, ct);
        if (!deleted) return NotFound();
        Audit(AuditAksiyonlari.OturumDelete, id, eski: $"hard-delete · {oturum.Durum}", yeni: "deleted");
        return NoContent();
    }

    [HttpPost("{id}/excel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SayimYoneticisi}")]
    // Hard cap on the request body. ExcelImportRequestValidator already enforces
    // 50K rows + per-field length limits, but the body limit is the first line of
    // defence — Kestrel rejects oversize payloads before model binding allocates
    // anything. 40MB is enough for the legitimate 50K-row × 800-byte case.
    [RequestSizeLimit(40_000_000)]
    public async Task<IActionResult> ImportExcel(
        string id, [FromBody] ExcelImportRequest request, CancellationToken ct)
    {
        // Excel ihracatlarında sıkça header tekrarı, ara satır, toplam satırı gibi
        // barkod hücresi boş olan satırlar oluyor. Bunlar yüzünden tüm yüklemenin
        // 400'le reddedilmesi pratik değil — sessizce eliyoruz, sayıyı audit'e
        // koyuyoruz ki kullanıcıdan da gizli kalmasın.
        var rawCount = request.Urunler.Count;
        request.Urunler = request.Urunler
            .Where(r => !string.IsNullOrWhiteSpace(r.Barkod))
            .ToList();
        var skippedEmptyBarkod = rawCount - request.Urunler.Count;

        var validation = await _excelValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        if (!await CanWriteStructuralAsync(oturum, ct)) return Forbid();

        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal or OturumDurumlari.Kilitli)
            return Conflict(new { message = "Bu oturumun durumu Excel yüklemeye uygun değil." });

        var urunler = request.Urunler.Select(r => new OturumUrun
        {
            Barkod = r.Barkod.Trim(),
            UrunAdi = r.UrunAdi.Trim(),
            SistemStok = r.SistemStok,
            SayilanStok = r.SayilanStok,
            StokKodu = TrimOrNull(r.StokKodu),
            Kategori = TrimOrNull(r.Kategori),
            AltKategori = TrimOrNull(r.AltKategori),
            Renk = TrimOrNull(r.Renk),
            Beden = TrimOrNull(r.Beden),
            Marka = TrimOrNull(r.Marka),
            Model = TrimOrNull(r.Model),
            Fiyat = r.Fiyat,
            Durum = r.SistemStok == r.SayilanStok ? UrunDurumlari.Onaylandi : UrunDurumlari.Beklemede,
        }).ToList();

        var ozet = SayimOturumu.ComputeOzet(urunler);
        var mapping = new ExcelMapping
        {
            BarkodKolon = request.Mapping.BarkodKolon,
            UrunAdiKolon = request.Mapping.UrunAdiKolon,
            SistemStokKolon = request.Mapping.SistemStokKolon,
            SayilanStokKolon = request.Mapping.SayilanStokKolon,
            StokKoduKolon = request.Mapping.StokKoduKolon,
            KategoriKolon = request.Mapping.KategoriKolon,
            AltKategoriKolon = request.Mapping.AltKategoriKolon,
            RenkKolon = request.Mapping.RenkKolon,
            BedenKolon = request.Mapping.BedenKolon,
            MarkaKolon = request.Mapping.MarkaKolon,
            ModelKolon = request.Mapping.ModelKolon,
            FiyatKolon = request.Mapping.FiyatKolon,
        };

        var updated = await _oturumlar.ReplaceUrunlerAndOzetAsync(id, mapping, urunler, ozet, ct);
        if (updated is null) return StatusCode(500);
        var auditDetail = skippedEmptyBarkod > 0
            ? $"{urunler.Count} ürün (boş barkodlu {skippedEmptyBarkod} satır atlandı)"
            : $"{urunler.Count} ürün";
        Audit(AuditAksiyonlari.OturumExcelImport, id, yeni: auditDetail);
        return Ok(await EnrichDetailAsync(updated, ct));
    }

    [HttpPatch("{oturumId}/urun/{urunId}")]
    public async Task<IActionResult> PatchUrun(
        string oturumId, string urunId, [FromBody] UrunPatchRequest request, CancellationToken ct)
    {
        var validation = await _urunValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var oturum = await _oturumlar.FindByIdAsync(oturumId, ct);
        if (oturum is null) return NotFound();
        if (!await CanReadAsync(oturum, ct)) return Forbid();
        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal)
            return Conflict(new { message = "Kapanmış oturum güncellenemez." });

        var urun = oturum.Urunler.FirstOrDefault(u => u.Id == urunId);
        if (urun is null) return NotFound();

        var uid = CurrentUserId() ?? string.Empty;
        var uname = User.FindFirst(ClaimTypes.Name)?.Value ?? "?";
        var canEditDurum = Roles.IsAdminLevel(User);
        // The "Sayman" role string is an alias for "Kullanici" (see Roles.cs note), so
        // role membership alone says nothing about whether the caller is actually a
        // counter on THIS oturum's atama. The authoritative source is the atama's
        // SaymanKullaniciIds list. Without this check, a mağaza müdürü (also "Kullanici")
        // would be allowed to edit sayım counts they have nothing to do with.
        Atama? oturumAtamasi = null;
        if (!string.IsNullOrEmpty(oturum.AtamaId))
            oturumAtamasi = await _atamalar.FindByIdAsync(oturum.AtamaId, ct);
        var isAtamaSayman = oturumAtamasi is not null
            && oturumAtamasi.SaymanKullaniciIds.Contains(uid);
        var isAdminLevel = Roles.IsAdminLevel(User) || User.IsInRole(Roles.SayimYoneticisi);
        // Sayman edit: only atama saymans may edit sayilan stok, and only their own
        // assignment or an unassigned row, and only while the row is in a counting state.
        var saymanOwnsUrun = string.IsNullOrEmpty(urun.AtananSaymanId)
            || urun.AtananSaymanId == uid;
        var canEditSayilan = isAdminLevel
            || (isAtamaSayman
                && saymanOwnsUrun
                && (urun.Durum == UrunDurumlari.Beklemede || urun.Durum == UrunDurumlari.TekrarSayiliyor));
        var canAtaSayman = Roles.IsAdminLevel(User) || User.IsInRole(Roles.SayimYoneticisi);
        var canEditMaster = Roles.IsAdminLevel(User); // Barkod/UrunAdi/SistemStok = Excel "ground truth"

        var changes = new List<UrunDegisiklik>();

        if (request.Barkod is not null && request.Barkod != urun.Barkod)
        {
            if (!canEditMaster) return Forbid();
            var newBarkod = request.Barkod.Trim();
            if (oturum.Urunler.Any(x => x.Id != urun.Id && x.Barkod == newBarkod))
                return Conflict(new { message = $"Bu oturumda zaten '{newBarkod}' barkodu var." });
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = uid, KullaniciAdi = uname, Alan = "barkod",
                EskiDeger = urun.Barkod, YeniDeger = newBarkod,
            });
            urun.Barkod = newBarkod;
        }

        if (request.UrunAdi is not null && request.UrunAdi != urun.UrunAdi)
        {
            if (!canEditMaster) return Forbid();
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = uid, KullaniciAdi = uname, Alan = "urunAdi",
                EskiDeger = urun.UrunAdi, YeniDeger = request.UrunAdi,
            });
            urun.UrunAdi = request.UrunAdi.Trim();
        }

        if (request.SistemStok.HasValue && request.SistemStok.Value != urun.SistemStok)
        {
            if (!canEditMaster) return Forbid();
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = uid, KullaniciAdi = uname, Alan = "sistemStok",
                EskiDeger = urun.SistemStok.ToString(CultureInfo.InvariantCulture),
                YeniDeger = request.SistemStok.Value.ToString(CultureInfo.InvariantCulture),
            });
            urun.SistemStok = request.SistemStok.Value;
        }

        if (request.SayilanStok.HasValue && request.SayilanStok.Value != urun.SayilanStok)
        {
            if (!canEditSayilan) return Forbid();
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = uid, KullaniciAdi = uname, Alan = "sayilanStok",
                EskiDeger = urun.SayilanStok.ToString(CultureInfo.InvariantCulture),
                YeniDeger = request.SayilanStok.Value.ToString(CultureInfo.InvariantCulture),
            });
            urun.SayilanStok = request.SayilanStok.Value;
        }

        if (!string.IsNullOrEmpty(request.Durum) && request.Durum != urun.Durum)
        {
            if (!canEditDurum) return Forbid();
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = uid, KullaniciAdi = uname, Alan = "durum",
                EskiDeger = urun.Durum, YeniDeger = request.Durum,
            });
            urun.Durum = request.Durum;
        }

        if (request.AtananSaymanId is not null && request.AtananSaymanId != urun.AtananSaymanId)
        {
            if (!canAtaSayman) return Forbid();
            changes.Add(new UrunDegisiklik
            {
                KullaniciId = uid, KullaniciAdi = uname, Alan = "atananSayman",
                EskiDeger = urun.AtananSaymanId, YeniDeger = request.AtananSaymanId,
            });
            urun.AtananSaymanId = string.IsNullOrEmpty(request.AtananSaymanId) ? null : request.AtananSaymanId;
        }

        if (!string.IsNullOrWhiteSpace(request.YorumEkle))
        {
            urun.Yorumlar.Add(new UrunYorum
            {
                KullaniciId = uid, KullaniciAdi = uname, Mesaj = request.YorumEkle.Trim(),
            });
        }

        if (changes.Count == 0 && string.IsNullOrWhiteSpace(request.YorumEkle))
            return Ok(new { message = "Değişiklik yok." });

        urun.DegisiklikGecmisi.AddRange(changes);
        urun.SonGuncelleyenId = uid;
        urun.GuncellenmeTarihi = DateTime.UtcNow;

        oturum.Ozetler = SayimOturumu.ComputeOzet(oturum.Urunler);

        // H-6: positional update — only push the changed scalar fields + appended history /
        // comment entries instead of rewriting the entire SayimOturumu document.
        var ub = Builders<SayimOturumu>.Update;
        var ops = new List<UpdateDefinition<SayimOturumu>>
        {
            ub.Set(o => o.Urunler.FirstMatchingElement().SonGuncelleyenId, urun.SonGuncelleyenId),
            ub.Set(o => o.Urunler.FirstMatchingElement().GuncellenmeTarihi, urun.GuncellenmeTarihi),
            ub.Set(o => o.Ozetler, oturum.Ozetler),
        };
        foreach (var c in changes)
        {
            switch (c.Alan)
            {
                case "barkod":         ops.Add(ub.Set(o => o.Urunler.FirstMatchingElement().Barkod,         urun.Barkod));         break;
                case "urunAdi":        ops.Add(ub.Set(o => o.Urunler.FirstMatchingElement().UrunAdi,        urun.UrunAdi));        break;
                case "sistemStok":     ops.Add(ub.Set(o => o.Urunler.FirstMatchingElement().SistemStok,     urun.SistemStok));     break;
                case "sayilanStok":    ops.Add(ub.Set(o => o.Urunler.FirstMatchingElement().SayilanStok,    urun.SayilanStok));    break;
                case "durum":          ops.Add(ub.Set(o => o.Urunler.FirstMatchingElement().Durum,          urun.Durum));          break;
                case "atananSayman":   ops.Add(ub.Set(o => o.Urunler.FirstMatchingElement().AtananSaymanId, urun.AtananSaymanId)); break;
            }
        }
        if (changes.Count > 0)
            ops.Add(ub.PushEach(o => o.Urunler.FirstMatchingElement().DegisiklikGecmisi, changes));
        if (!string.IsNullOrWhiteSpace(request.YorumEkle))
            ops.Add(ub.Push(o => o.Urunler.FirstMatchingElement().Yorumlar, urun.Yorumlar[^1]));

        await _oturumlar.UpdateUrunAsync(oturumId, urunId, ub.Combine(ops), ct);
        Audit(AuditAksiyonlari.UrunUpdate, $"{oturumId}/{urunId}",
            yeni: string.Join(", ", changes.Select(c => $"{c.Alan}:{c.YeniDeger}")));

        return Ok(ToUrunDto(urun, FindUserName(oturum, urun.AtananSaymanId)));
    }

    [HttpDelete("{oturumId}/urun/{urunId}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> DeleteUrun(string oturumId, string urunId, CancellationToken ct)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, ct);
        if (oturum is null) return NotFound();
        if (!await CanWriteStructuralAsync(oturum, ct)) return Forbid();
        if (oturum.Durum is OturumDurumlari.Tamamlandi or OturumDurumlari.Iptal)
            return Conflict(new { message = "Kapanmış oturumdan satır silinemez." });

        var urun = oturum.Urunler.FirstOrDefault(u => u.Id == urunId);
        if (urun is null) return NotFound();

        oturum.Urunler.RemoveAll(u => u.Id == urunId);
        oturum.Ozetler = SayimOturumu.ComputeOzet(oturum.Urunler);
        await _oturumlar.ReplaceAsync(oturum, ct);

        Audit(AuditAksiyonlari.UrunDelete, $"{oturumId}/{urunId}",
            eski: $"{urun.Barkod} · {urun.UrunAdi}");
        return NoContent();
    }

    [HttpGet("{oturumId}/davet-edilebilir")]
    public async Task<IActionResult> ListInvitableUsers(string oturumId, CancellationToken ct)
    {
        var oturum = await _oturumlar.FindByIdAsync(oturumId, ct);
        if (oturum is null) return NotFound();
        if (!await CanReadAsync(oturum, ct)) return Forbid();
        if (!Roles.IsAdminLevel(User)) return Forbid();

        var allUsers = await _users.ListAsync(includeInactive: false, ct);
        var katilimciIds = oturum.Katilimcilar.Select(k => k.KullaniciId).ToHashSet();

        // Sistem her kullanıcıyı görsün; SayimBaskani sadece oturumun firma scope'una bağlı user'ları görsün.
        bool isSistem = User.IsInRole(Roles.Sistem);
        var firmaId = oturum.FirmaId;

        var filtered = allUsers
            .Where(u => u.Id != CurrentUserId())
            .Where(u =>
            {
                if (isSistem) return true;
                if (u.Rol == Roles.Sistem) return true; // Sistem'e davet edilebilir
                // Firma scope'una bağlı mı?
                if (!string.IsNullOrEmpty(u.FirmaId) && u.FirmaId == firmaId) return true;
                if (u.FirmaIds.Contains(firmaId)) return true;
                return false;
            })
            .Select(u => new DavetEdilebilirKullaniciDto
            {
                Id = u.Id,
                AdSoyad = u.AdSoyad,
                Email = u.Email,
                Rol = u.Rol,
                ZatenKatilimci = katilimciIds.Contains(u.Id),
            })
            .OrderBy(u => u.AdSoyad)
            .ToList();

        return Ok(filtered);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private string? CurrentUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private async Task<bool> CanReadAsync(SayimOturumu oturum, CancellationToken ct)
    {
        // C-1: Only Sistem bypasses participation checks. SayimBaskani must be a Katilimci /
        // assigned to the Magaza / on DavetliMailler — same scope as List filtering.
        if (User.IsSistem()) return true;
        var uid = CurrentUserId();
        if (uid is null) return false;
        if (oturum.Katilimcilar.Any(k => k.KullaniciId == uid)) return true;

        var dbUser = await _users.FindByIdAsync(uid, ct);
        if (dbUser is not null && dbUser.MagazaIds.Contains(oturum.MagazaId)) return true;

        var myEmail = dbUser?.Email ?? User.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(myEmail) && oturum.DavetliMailler.Any(m =>
                string.Equals(m, myEmail, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    // Structural writes (Update, ChangeDurum, SoftCancel, HardDelete, ImportExcel,
    // DeleteUrun) require the caller to own the firma the oturum belongs to. Being
    // a Katilimci or DavetliMail target is enough for participation (PatchUrun uses
    // CanReadAsync) but not enough to reshape the session itself — that authority
    // stays with the firma owner and the platform super-admin.
    private async Task<bool> CanWriteStructuralAsync(SayimOturumu oturum, CancellationToken ct)
    {
        if (User.IsSistem()) return true;
        var uid = CurrentUserId();
        if (uid is null) return false;

        var firma = await _firmalar.FindByIdAsync(oturum.FirmaId, ct);
        return firma is not null && firma.OlusturanKullaniciId == uid;
    }

    private static bool IsValidTransition(string from, string to)
    {
        return (from, to) switch
        {
            (OturumDurumlari.Taslak, OturumDurumlari.ExcelBekleniyor) => true,
            (OturumDurumlari.ExcelBekleniyor, OturumDurumlari.Aktif) => true,
            (OturumDurumlari.Aktif, OturumDurumlari.Kilitli) => true,
            (OturumDurumlari.Kilitli, OturumDurumlari.Aktif) => true,
            (OturumDurumlari.Aktif, OturumDurumlari.Tamamlandi) => true,
            (OturumDurumlari.Kilitli, OturumDurumlari.Tamamlandi) => true,
            (_, OturumDurumlari.Iptal) when from is not OturumDurumlari.Tamamlandi => true,
            _ => false,
        };
    }

    private async Task<IReadOnlyList<OturumListDto>> EnrichListAsync(
        IReadOnlyList<SayimOturumu> oturumlar, CancellationToken ct)
    {
        if (oturumlar.Count == 0) return Array.Empty<OturumListDto>();

        var magazaMap = (await _magazalar.ListByIdsAsync(oturumlar.Select(o => o.MagazaId).Distinct(), ct))
            .ToDictionary(m => m.Id, m => m);
        var firmaIds = oturumlar.Select(o => o.FirmaId).Distinct();
        var firmaMap = new Dictionary<string, (string Ad, string Tip)>();
        foreach (var id in firmaIds)
        {
            var f = await _firmalar.FindByIdAsync(id, ct);
            if (f is not null) firmaMap[id] = (f.Ad, f.Tip);
        }
        var userIds = oturumlar.SelectMany(o => o.Katilimcilar.Select(k => k.KullaniciId)).Distinct();
        var userMap = (await _users.ListByIdsAsync(userIds, ct))
            .ToDictionary(u => u.Id, u => u.AdSoyad);

        return oturumlar.Select(o => ToListDto(o, magazaMap, firmaMap, userMap)).ToList();
    }

    private async Task<OturumDetailDto> EnrichDetailAsync(SayimOturumu oturum, CancellationToken ct)
    {
        var magaza = await _magazalar.FindByIdAsync(oturum.MagazaId, ct);
        var firma = await _firmalar.FindByIdAsync(oturum.FirmaId, ct);
        var userIds = oturum.Katilimcilar.Select(k => k.KullaniciId)
            .Concat(oturum.Urunler.Where(u => u.AtananSaymanId is not null).Select(u => u.AtananSaymanId!))
            .Distinct();
        var userMap = (await _users.ListByIdsAsync(userIds, ct))
            .ToDictionary(u => u.Id, u => u.AdSoyad);

        var dto = new OturumDetailDto
        {
            Id = oturum.Id,
            MagazaId = oturum.MagazaId,
            MagazaAdi = magaza?.Ad ?? "?",
            FirmaId = oturum.FirmaId,
            FirmaAdi = firma?.Ad ?? "?",
            FirmaTip = firma?.Tip ?? FirmaTipleri.Diger,
            DavetliMailler = oturum.DavetliMailler,
            AtamaId = oturum.AtamaId,
            Tarih = oturum.Tarih,
            Durum = oturum.Durum,
            Ozetler = ToOzetDto(oturum.Ozetler),
            Katilimcilar = oturum.Katilimcilar.Select(k => new KatilimciDto
            {
                KullaniciId = k.KullaniciId,
                AdSoyad = userMap.GetValueOrDefault(k.KullaniciId, "?"),
                Rol = k.Rol,
            }).ToList(),
            OlusturmaTarihi = oturum.OlusturmaTarihi,
            Urunler = oturum.Urunler.Select(u => ToUrunDto(u, FindUserName(oturum, u.AtananSaymanId))).ToList(),
            ExcelMapping = new ExcelMappingDto
            {
                BarkodKolon = oturum.ExcelMapping.BarkodKolon,
                UrunAdiKolon = oturum.ExcelMapping.UrunAdiKolon,
                SistemStokKolon = oturum.ExcelMapping.SistemStokKolon,
                SayilanStokKolon = oturum.ExcelMapping.SayilanStokKolon,
                StokKoduKolon = oturum.ExcelMapping.StokKoduKolon,
                KategoriKolon = oturum.ExcelMapping.KategoriKolon,
                AltKategoriKolon = oturum.ExcelMapping.AltKategoriKolon,
                RenkKolon = oturum.ExcelMapping.RenkKolon,
                BedenKolon = oturum.ExcelMapping.BedenKolon,
                MarkaKolon = oturum.ExcelMapping.MarkaKolon,
                ModelKolon = oturum.ExcelMapping.ModelKolon,
                FiyatKolon = oturum.ExcelMapping.FiyatKolon,
            },
        };
        return dto;
    }

    private static OturumListDto ToListDto(
        SayimOturumu o,
        IReadOnlyDictionary<string, Magaza> magazaMap,
        IReadOnlyDictionary<string, (string Ad, string Tip)> firmaMap,
        IReadOnlyDictionary<string, string> userMap)
    {
        var firmaInfo = firmaMap.TryGetValue(o.FirmaId, out var f) ? f : ("?", FirmaTipleri.Diger);
        return new()
        {
            Id = o.Id,
            MagazaId = o.MagazaId,
            MagazaAdi = magazaMap.TryGetValue(o.MagazaId, out var m) ? m.Ad : "?",
            FirmaId = o.FirmaId,
            FirmaAdi = firmaInfo.Item1,
            FirmaTip = firmaInfo.Item2,
            AtamaId = o.AtamaId,
            Tarih = o.Tarih,
            Durum = o.Durum,
            Ozetler = ToOzetDto(o.Ozetler),
            Katilimcilar = o.Katilimcilar.Select(k => new KatilimciDto
            {
                KullaniciId = k.KullaniciId,
                AdSoyad = userMap.GetValueOrDefault(k.KullaniciId, "?"),
                Rol = k.Rol,
            }).ToList(),
            DavetliMailler = o.DavetliMailler,
            OlusturmaTarihi = o.OlusturmaTarihi,
        };
    }

    private static OturumOzetDto ToOzetDto(OturumOzet o) => new()
    {
        ToplamUrun = o.ToplamUrun,
        BeklemedeSayisi = o.BeklemedeSayisi,
        TekrarSayilan = o.TekrarSayilan,
        Onaylanmis = o.Onaylanmis,
        IptalEdilmis = o.IptalEdilmis,
        Inceleme = o.Inceleme,
        ToplamFarkPozitif = o.ToplamFarkPozitif,
        ToplamFarkNegatif = o.ToplamFarkNegatif,
    };

    private static OturumUrunDto ToUrunDto(OturumUrun u, string? atananAdi) => new()
    {
        Id = u.Id,
        Barkod = u.Barkod,
        UrunAdi = u.UrunAdi,
        SistemStok = u.SistemStok,
        SayilanStok = u.SayilanStok,
        Fark = u.SayilanStok - u.SistemStok,
        Durum = u.Durum,
        AtananSaymanId = u.AtananSaymanId,
        AtananSaymanAdi = atananAdi,
        YorumSayisi = u.Yorumlar.Count,
        KilitleyenKullaniciId = u.KilitleyenKullaniciId,
        KilitleyenAdi = null,
        KilitlenmeTarihi = u.KilitlenmeTarihi,
        StokKodu = u.StokKodu,
        Kategori = u.Kategori,
        AltKategori = u.AltKategori,
        Renk = u.Renk,
        Beden = u.Beden,
        Marka = u.Marka,
        Model = u.Model,
        Fiyat = u.Fiyat,
        SistemFarki = u.Fiyat.HasValue ? u.SistemStok * u.Fiyat.Value : null,
        FiiliFarki = u.Fiyat.HasValue ? u.SayilanStok * u.Fiyat.Value : null,
        FiyatFarki = u.Fiyat.HasValue ? (u.SayilanStok - u.SistemStok) * u.Fiyat.Value : null,
        AcikTalepler = u.Talepler
            .Where(t => t.Durum == TalepDurumlari.Beklemede)
            .Select(t => ToTalepDto(t, u.Id))
            .ToList(),
    };

    internal static UrunDegisiklikTalebiDto ToTalepDto(UrunDegisiklikTalebi t, string urunId) => new()
    {
        Id = t.Id,
        UrunId = urunId,
        KullaniciId = t.KullaniciId,
        KullaniciAdi = t.KullaniciAdi,
        Alan = t.Alan,
        EskiDeger = t.EskiDeger,
        YeniDeger = t.YeniDeger,
        Gerekce = t.Gerekce,
        Durum = t.Durum,
        KararVerenId = t.KararVerenId,
        KararVerenAdi = t.KararVerenAdi,
        KararSebep = t.KararSebep,
        KararTarihi = t.KararTarihi,
        OlusturmaTarihi = t.OlusturmaTarihi,
    };

    private static string? TrimOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<string> NormalizeEmails(IEnumerable<string>? emails) =>
        emails is null ? new List<string>() : emails
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

    private static string? FindUserName(SayimOturumu oturum, string? userId) =>
        userId is null ? null : oturum.Katilimcilar.FirstOrDefault(k => k.KullaniciId == userId)?.KullaniciId is { } _ ? null : null;

    private static DateTime? TryParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;
    }

    private static DateTime ParseUtcMidnight(string yyyyMMdd)
    {
        var d = DateTime.ParseExact(yyyyMMdd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    private IActionResult ValidationFailure(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
