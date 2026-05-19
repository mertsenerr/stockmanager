using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ozel-raporlar")]
public sealed class OzelRaporlarController : ControllerBase
{
    private readonly IOzelRaporRepository _repo;
    private readonly IUserRepository _users;
    private readonly IOzelRaporStorage _storage;
    private readonly IBelgeTipiRepository _belgeTipleri;
    private readonly IPdfSigner _pdfSigner;
    private readonly IAuditService _audit;
    private readonly IValidator<OzelRaporUpsertRequest> _validator;
    private readonly ILogger<OzelRaporlarController> _logger;

    public OzelRaporlarController(
        IOzelRaporRepository repo,
        IUserRepository users,
        IOzelRaporStorage storage,
        IBelgeTipiRepository belgeTipleri,
        IPdfSigner pdfSigner,
        IAuditService audit,
        IValidator<OzelRaporUpsertRequest> validator,
        ILogger<OzelRaporlarController> logger)
    {
        _repo = repo;
        _users = users;
        _storage = storage;
        _belgeTipleri = belgeTipleri;
        _pdfSigner = pdfSigner;
        _audit = audit;
        _validator = validator;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var uid = User.GetUserId();
        if (string.IsNullOrEmpty(uid)) return Ok(Array.Empty<OzelRaporListDto>());

        IReadOnlyList<OzelRapor> raporlar;
        if (User.IsSistem())
        {
            raporlar = await _repo.ListAllAsync(ct);
        }
        else if (User.IsInRole(Roles.SayimBaskani))
        {
            raporlar = await _repo.ListByOwnerAsync(uid, ct);
        }
        else
        {
            raporlar = await _repo.ListAccessibleByAsync(uid, ct);
        }

        var olusturanIds = raporlar
            .Select(r => r.OlusturanKullaniciId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();
        var olusturanlar = olusturanIds.Count == 0
            ? new Dictionary<string, User>()
            : (await _users.ListByIdsAsync(olusturanIds, ct))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var isSistem = User.IsSistem();
        // List endpoint birden fazla rapor döner — referans verilen tüm belge
        // tiplerini tek seferde topla, sonra in-memory map'le çöz.
        var belgeTipiIds = raporlar
            .SelectMany(r => r.Dosyalar)
            .Where(d => !string.IsNullOrEmpty(d.BelgeTipiId))
            .Select(d => d.BelgeTipiId!)
            .Distinct()
            .ToList();
        var belgeTipiMap = new Dictionary<string, string>();
        foreach (var btId in belgeTipiIds)
        {
            var bt = await _belgeTipleri.FindByIdAsync(btId, ct);
            if (bt is not null) belgeTipiMap[bt.Id] = bt.Ad;
        }
        return Ok(raporlar.Select(r => ToListDto(r, uid, isSistem, olusturanlar, belgeTipiMap)).ToList());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();

        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var olusturanlar = (await _users.ListByIdsAsync([rapor.OlusturanKullaniciId], ct))
            .ToDictionary(u => u.Id);
        return Ok(await ToListDtoAsync(rapor, uid, User.IsSistem(), olusturanlar, ct));
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Create(
        [FromBody] OzelRaporUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblemFromResult(validation);

        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();

        var allowedAccess = await FilterAccessibleUserIdsAsync(uid, request.ErisebilenKullaniciIds, ct);

        var rapor = new OzelRapor
        {
            Ad = request.Ad.Trim(),
            Aciklama = string.IsNullOrWhiteSpace(request.Aciklama) ? null : request.Aciklama.Trim(),
            OlusturanKullaniciId = uid,
            ErisebilenKullaniciIds = allowedAccess,
        };
        await _repo.InsertAsync(rapor, ct);
        _audit.Log(User, AuditAksiyonlari.OzelRaporCreate, "ozel-rapor", rapor.Id, yeni: rapor.Ad);

        var olusturanlar = (await _users.ListByIdsAsync([rapor.OlusturanKullaniciId], ct))
            .ToDictionary(u => u.Id);
        return CreatedAtAction(nameof(Get), new { id = rapor.Id },
            await ToListDtoAsync(rapor, uid, User.IsSistem(), olusturanlar, ct));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Update(
        string id, [FromBody] OzelRaporUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblemFromResult(validation);

        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanWrite(rapor, uid)) return Forbid();

        rapor.Ad = request.Ad.Trim();
        rapor.Aciklama = string.IsNullOrWhiteSpace(request.Aciklama) ? null : request.Aciklama.Trim();
        rapor.ErisebilenKullaniciIds = await FilterAccessibleUserIdsAsync(
            rapor.OlusturanKullaniciId, request.ErisebilenKullaniciIds, ct);
        await _repo.ReplaceAsync(rapor, ct);
        _audit.Log(User, AuditAksiyonlari.OzelRaporUpdate, "ozel-rapor", rapor.Id, yeni: rapor.Ad);

        var olusturanlar = (await _users.ListByIdsAsync([rapor.OlusturanKullaniciId], ct))
            .ToDictionary(u => u.Id);
        return Ok(await ToListDtoAsync(rapor, uid, User.IsSistem(), olusturanlar, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanWrite(rapor, uid)) return Forbid();

        await _repo.DeleteAsync(id, ct);
        try { _storage.DeleteRaporFolder(rapor.Id); }
        catch (Exception ex) { _logger.LogWarning(ex, "OzelRapor klasör silme hatası: {Id}", rapor.Id); }
        _audit.Log(User, AuditAksiyonlari.OzelRaporDelete, "ozel-rapor", rapor.Id, eski: rapor.Ad);
        return NoContent();
    }

    [HttpPost("{id}/files")]
    [Authorize(Roles = Roles.AdminLevel)]
    [RequestSizeLimit(60_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 60_000_000)]
    public async Task<IActionResult> UploadFiles(
        string id,
        [FromForm] IFormFileCollection files,
        [FromForm] string? belgeTipiId,
        CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanWrite(rapor, uid)) return Forbid();

        if (files is null || files.Count == 0)
            return BadRequest(new { message = "Dosya seçilmedi." });

        // Bu request'te yüklenen tüm dosyalar aynı belge tipine bağlanır. Farklı
        // tipler için frontend ayrı çağrı yapar. Snapshot — sonradan katalog
        // değişirse mevcut dosyalar etkilenmez.
        BelgeTipi? belgeTipi = null;
        if (!string.IsNullOrEmpty(belgeTipiId))
        {
            belgeTipi = await _belgeTipleri.FindByIdAsync(belgeTipiId, ct);
            if (belgeTipi is null || belgeTipi.Arsivlendi)
                return BadRequest(new { message = "Belge tipi bulunamadı veya arşivlenmiş." });
        }

        var added = new List<OzelRaporDosya>();
        foreach (var file in files)
        {
            var hata = _storage.ValidateUpload(file.FileName, file.Length);
            if (hata is not null)
                return BadRequest(new { message = $"{file.FileName}: {hata}" });
        }

        foreach (var file in files)
        {
            var dosyaId = ObjectId.GenerateNewId().ToString();
            await using var src = file.OpenReadStream();
            var storageName = await _storage.SaveAsync(rapor.Id, dosyaId, file.FileName, src, ct);
            var dosya = new OzelRaporDosya
            {
                Id = dosyaId,
                Ad = Path.GetFileName(file.FileName),
                MimeType = string.IsNullOrEmpty(file.ContentType)
                    ? "application/octet-stream" : file.ContentType,
                Boyut = file.Length,
                StorageName = storageName,
                BelgeTipiId = belgeTipi?.Id,
                ImzaGerekenRoller = belgeTipi is null ? [] : [..belgeTipi.GerekenImzaRolleri],
                KaseGerekli = belgeTipi?.KaseGerekli ?? false,
            };
            rapor.Dosyalar.Add(dosya);
            added.Add(dosya);
        }

        await _repo.ReplaceAsync(rapor, ct);
        foreach (var d in added)
        {
            _audit.Log(User, AuditAksiyonlari.OzelRaporFileAdd, "ozel-rapor", rapor.Id,
                yeni: $"{d.Ad} ({d.Boyut} bayt)");
        }

        var olusturanlar = (await _users.ListByIdsAsync([rapor.OlusturanKullaniciId], ct))
            .ToDictionary(u => u.Id);
        return Ok(await ToListDtoAsync(rapor, uid, User.IsSistem(), olusturanlar, ct));
    }

    [HttpPost("{id}/files/{fileId}/imza")]
    public async Task<IActionResult> ImzaAt(
        string id, string fileId,
        [FromBody] ImzaEkleRequest request,
        CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null) return NotFound();

        if (!ImzaRolleri.IsValid(request.Rol))
            return BadRequest(new { message = "Geçersiz imza rolü." });

        if (!dosya.ImzaGerekenRoller.Contains(request.Rol))
            return BadRequest(new { message = "Bu belge için bu rolün imzası gerekli değil." });

        if (!CanSignAs(request.Rol))
            return Forbid();

        if (dosya.Imzalar.Any(i => i.Rol == request.Rol))
            return Conflict(new { message = "Bu rol için zaten imza var. Önce mevcut imzayı silmelisiniz." });

        if (string.IsNullOrEmpty(request.ImzaGorseliDataUri) ||
            !request.ImzaGorseliDataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Geçerli bir imza görseli gönderin (PNG data URI)." });

        var dbUser = await _users.FindByIdAsync(uid, ct);
        var imza = new DosyaImza
        {
            Rol = request.Rol,
            KullaniciId = uid,
            KullaniciAdSoyad = dbUser?.AdSoyad ?? "Bilinmiyor",
            ImzaGorseliDataUri = request.ImzaGorseliDataUri,
        };
        dosya.Imzalar.Add(imza);
        await _repo.ReplaceAsync(rapor, ct);
        _audit.Log(User, AuditAksiyonlari.OzelRaporImzaAt, "ozel-rapor", rapor.Id, yeni: $"{dosya.Ad} · {request.Rol}");

        return Ok(await ToListDtoAsync(rapor, uid, User.IsSistem(),
            (await _users.ListByIdsAsync([rapor.OlusturanKullaniciId], ct)).ToDictionary(u => u.Id), ct));
    }

    [HttpDelete("{id}/files/{fileId}/imza/{imzaId}")]
    public async Task<IActionResult> ImzaSil(string id, string fileId, string imzaId, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null) return NotFound();
        var imza = dosya.Imzalar.FirstOrDefault(i => i.Id == imzaId);
        if (imza is null) return NotFound();

        // Kendi imzanı veya Sistem rolü herkesi silebilir.
        if (imza.KullaniciId != uid && !User.IsSistem()) return Forbid();

        dosya.Imzalar.Remove(imza);
        await _repo.ReplaceAsync(rapor, ct);
        _audit.Log(User, AuditAksiyonlari.OzelRaporImzaSil, "ozel-rapor", rapor.Id, eski: $"{dosya.Ad} · {imza.Rol}");
        return NoContent();
    }

    [HttpPost("{id}/files/{fileId}/kase")]
    public async Task<IActionResult> KaseBas(string id, string fileId, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null) return NotFound();
        if (!dosya.KaseGerekli)
            return BadRequest(new { message = "Bu belge için kaşe gerekli değil." });
        if (dosya.Kase is not null)
            return Conflict(new { message = "Bu belgede zaten kaşe var." });

        // Kaşe basabilen: MagazaYetkilisi rolü (Kullanici) veya Sistem. SayimBaskani basamaz.
        if (!User.IsSistem() && !User.IsInRole(Roles.Kullanici))
            return Forbid();

        var dbUser = await _users.FindByIdAsync(uid, ct);
        dosya.Kase = new KaseDamga
        {
            BasanKullaniciId = uid,
            BasanAdSoyad = dbUser?.AdSoyad ?? "Bilinmiyor",
        };
        await _repo.ReplaceAsync(rapor, ct);
        _audit.Log(User, AuditAksiyonlari.OzelRaporKaseBas, "ozel-rapor", rapor.Id, yeni: dosya.Ad);

        return Ok(await ToListDtoAsync(rapor, uid, User.IsSistem(),
            (await _users.ListByIdsAsync([rapor.OlusturanKullaniciId], ct)).ToDictionary(u => u.Id), ct));
    }

    [HttpDelete("{id}/files/{fileId}/kase")]
    public async Task<IActionResult> KaseSil(string id, string fileId, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null || dosya.Kase is null) return NotFound();

        if (dosya.Kase.BasanKullaniciId != uid && !User.IsSistem()) return Forbid();

        var eski = dosya.Kase.BasanAdSoyad;
        dosya.Kase = null;
        await _repo.ReplaceAsync(rapor, ct);
        _audit.Log(User, AuditAksiyonlari.OzelRaporKaseSil, "ozel-rapor", rapor.Id, eski: $"{dosya.Ad} · {eski}");
        return NoContent();
    }

    [HttpGet("{id}/files/{fileId}/signed")]
    public async Task<IActionResult> DownloadSigned(string id, string fileId, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null) return NotFound();

        // UX mock — sadece PDF'lerde imza/kaşe bindirebiliyoruz. xlsx için kullanıcıya
        // PDF olarak yükle önerisi.
        if (!IsPdf(dosya))
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                message = "İmzalı versiyon yalnızca PDF dosyalar için üretilebilir. Lütfen PDF'e çevirip yeniden yükleyin.",
            });

        Stream original;
        try { original = await _storage.OpenReadAsync(rapor.Id, dosya.StorageName, ct); }
        catch (FileNotFoundException) { return NotFound(); }

        MemoryStream signed;
        try
        {
            await using (original)
            {
                signed = _pdfSigner.Stamp(original, dosya.Imzalar, dosya.Kase);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "İmzalı PDF üretilemedi: {Id}/{FileId}", rapor.Id, fileId);
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { message = ex.Message });
        }

        _audit.Log(User, AuditAksiyonlari.OzelRaporImzaliDownload, "ozel-rapor", rapor.Id, yeni: dosya.Ad);

        var stem = Path.GetFileNameWithoutExtension(dosya.Ad);
        var disposition = new System.Net.Mime.ContentDisposition
        {
            FileName = $"{stem} (imzalı).pdf",
            Inline = false,
        };
        Response.Headers["Content-Disposition"] = disposition.ToString();
        return File(signed, "application/pdf");
    }

    private static bool IsPdf(OzelRaporDosya d) =>
        d.Ad.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
        d.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>İmzalayan rolün kullanıcı tarafından üstlenilebileceğini doğrular.</summary>
    private bool CanSignAs(string rol)
    {
        if (User.IsSistem()) return true;
        return rol switch
        {
            ImzaRolleri.SayimBaskani => User.IsInRole(Roles.SayimBaskani),
            ImzaRolleri.MagazaYetkilisi => User.IsInRole(Roles.Kullanici),
            _ => false,
        };
    }

    [HttpDelete("{id}/files/{fileId}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> DeleteFile(string id, string fileId, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanWrite(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null) return NotFound();

        rapor.Dosyalar.Remove(dosya);
        await _repo.ReplaceAsync(rapor, ct);
        try { _storage.DeleteFile(rapor.Id, dosya.StorageName); }
        catch (Exception ex) { _logger.LogWarning(ex, "OzelRapor dosya silme hatası: {Id}/{FileId}", rapor.Id, fileId); }
        _audit.Log(User, AuditAksiyonlari.OzelRaporFileDelete, "ozel-rapor", rapor.Id, eski: dosya.Ad);

        return NoContent();
    }

    [HttpGet("{id}/files/{fileId}/download")]
    public async Task<IActionResult> Download(string id, string fileId, CancellationToken ct)
    {
        var rapor = await _repo.FindByIdAsync(id, ct);
        if (rapor is null) return NotFound();
        var uid = User.GetUserId();
        if (uid is null) return Unauthorized();
        if (!CanRead(rapor, uid)) return Forbid();

        var dosya = rapor.Dosyalar.FirstOrDefault(d => d.Id == fileId);
        if (dosya is null) return NotFound();

        Stream stream;
        try { stream = await _storage.OpenReadAsync(rapor.Id, dosya.StorageName, ct); }
        catch (FileNotFoundException) { return NotFound(); }

        _audit.Log(User, AuditAksiyonlari.OzelRaporDownload, "ozel-rapor", rapor.Id, yeni: dosya.Ad);

        // Force download instead of inline rendering. The stored MimeType comes from the
        // uploader's Content-Type header — we don't trust it for inline display. Combined
        // with the extension allow-list (xlsx/xls/pdf) and X-Content-Type-Options=nosniff
        // this neutralises XSS-via-upload tricks.
        var disposition = new System.Net.Mime.ContentDisposition
        {
            FileName = dosya.Ad,
            Inline = false,
        };
        Response.Headers["Content-Disposition"] = disposition.ToString();
        return File(stream, "application/octet-stream");
    }

    private bool CanRead(OzelRapor rapor, string uid) =>
        User.IsSistem()
        || rapor.OlusturanKullaniciId == uid
        || rapor.ErisebilenKullaniciIds.Contains(uid);

    private bool CanWrite(OzelRapor rapor, string uid) =>
        User.IsSistem() || rapor.OlusturanKullaniciId == uid;

    /// <summary>
    /// Erişim listesindeki id'leri sadeleştirir: yalnızca DB'de bulunan ve aktif olan
    /// kullanıcılar kabul edilir. Frontend tarafı UI'da seçimi rapor sahibinin arkadaş
    /// listesiyle sınırlıyor; bu metod ise temel sanity check (var-mı/aktif-mi) yapar.
    /// </summary>
    private async Task<List<string>> FilterAccessibleUserIdsAsync(
        string ownerUserId, IEnumerable<string> requested, CancellationToken ct)
    {
        var distinctIds = requested.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (distinctIds.Count == 0) return [];

        var users = await _users.ListByIdsAsync(distinctIds, ct);
        return users.Where(u => u.AktifMi).Select(u => u.Id).ToList();
    }

    private async Task<OzelRaporListDto> ToListDtoAsync(
        OzelRapor r, string callerUid, bool callerIsSistem,
        IDictionary<string, User> olusturanlar, CancellationToken ct)
    {
        var belgeTipiIds = r.Dosyalar
            .Where(d => !string.IsNullOrEmpty(d.BelgeTipiId))
            .Select(d => d.BelgeTipiId!)
            .Distinct()
            .ToList();
        var belgeTipiMap = new Dictionary<string, string>();
        foreach (var btId in belgeTipiIds)
        {
            var bt = await _belgeTipleri.FindByIdAsync(btId, ct);
            if (bt is not null) belgeTipiMap[bt.Id] = bt.Ad;
        }
        return ToListDto(r, callerUid, callerIsSistem, olusturanlar, belgeTipiMap);
    }

    private static OzelRaporListDto ToListDto(
        OzelRapor r, string callerUid, bool callerIsSistem,
        IDictionary<string, User> olusturanlar,
        IReadOnlyDictionary<string, string> belgeTipiMap)
    {
        olusturanlar.TryGetValue(r.OlusturanKullaniciId, out var olusturan);
        return new OzelRaporListDto
        {
            Id = r.Id,
            Ad = r.Ad,
            Aciklama = r.Aciklama,
            OlusturanKullaniciId = r.OlusturanKullaniciId,
            OlusturanAdSoyad = olusturan?.AdSoyad,
            ErisebilenKullaniciIds = r.ErisebilenKullaniciIds,
            Dosyalar = r.Dosyalar.Select(d => new OzelRaporDosyaDto
            {
                Id = d.Id,
                Ad = d.Ad,
                MimeType = d.MimeType,
                Boyut = d.Boyut,
                YuklemeTarihi = d.YuklemeTarihi,
                BelgeTipiId = d.BelgeTipiId,
                BelgeTipiAdi = d.BelgeTipiId is not null && belgeTipiMap.TryGetValue(d.BelgeTipiId, out var btAd) ? btAd : null,
                ImzaGerekenRoller = d.ImzaGerekenRoller,
                KaseGerekli = d.KaseGerekli,
                Imzalar = d.Imzalar.Select(i => new DosyaImzaDto
                {
                    Id = i.Id,
                    Rol = i.Rol,
                    KullaniciId = i.KullaniciId,
                    KullaniciAdSoyad = i.KullaniciAdSoyad,
                    ImzalanmaTarihi = i.ImzalanmaTarihi,
                }).ToList(),
                Kase = d.Kase is null ? null : new KaseDamgaDto
                {
                    BasanKullaniciId = d.Kase.BasanKullaniciId,
                    BasanAdSoyad = d.Kase.BasanAdSoyad,
                    Tarih = d.Kase.Tarih,
                },
            }).ToList(),
            OlusturmaTarihi = r.OlusturmaTarihi,
            GuncellenmeTarihi = r.GuncellenmeTarihi,
            Duzenleyebilir = callerIsSistem || r.OlusturanKullaniciId == callerUid,
        };
    }

    private IActionResult ValidationProblemFromResult(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
