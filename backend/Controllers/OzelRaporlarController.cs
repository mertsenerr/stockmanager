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
    private readonly IAuditService _audit;
    private readonly IValidator<OzelRaporUpsertRequest> _validator;
    private readonly ILogger<OzelRaporlarController> _logger;

    public OzelRaporlarController(
        IOzelRaporRepository repo,
        IUserRepository users,
        IOzelRaporStorage storage,
        IBelgeTipiRepository belgeTipleri,
        IAuditService audit,
        IValidator<OzelRaporUpsertRequest> validator,
        ILogger<OzelRaporlarController> logger)
    {
        _repo = repo;
        _users = users;
        _storage = storage;
        _belgeTipleri = belgeTipleri;
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
