using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.AdminLevel)]
[Route("api/belge-tipleri")]
public sealed class BelgeTipleriController : ControllerBase
{
    private readonly IBelgeTipiRepository _repo;
    private readonly IFirmaRepository _firmalar;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly IValidator<BelgeTipiUpsertRequest> _validator;

    public BelgeTipleriController(
        IBelgeTipiRepository repo,
        IFirmaRepository firmalar,
        IUserRepository users,
        IAuditService audit,
        IValidator<BelgeTipiUpsertRequest> validator)
    {
        _repo = repo;
        _firmalar = firmalar;
        _users = users;
        _audit = audit;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? firmaId,
        [FromQuery] bool includeArchived,
        CancellationToken ct)
    {
        IReadOnlyList<BelgeTipi> kayitlar;
        if (User.IsSistem())
        {
            kayitlar = string.IsNullOrEmpty(firmaId)
                ? await _repo.ListAllAsync(includeArchived, ct)
                : await _repo.ListByFirmaAsync(firmaId, includeArchived, ct);
        }
        else
        {
            var scope = await ResolveCallerFirmaIdAsync(ct);
            if (scope is null) return Ok(Array.Empty<BelgeTipiDto>());
            kayitlar = await _repo.ListByFirmaAsync(scope, includeArchived, ct);
        }

        var firmaIds = kayitlar.Select(k => k.FirmaId).Distinct().ToList();
        var firmaMap = new Dictionary<string, string>();
        foreach (var fid in firmaIds)
        {
            var firma = await _firmalar.FindByIdAsync(fid, ct);
            if (firma is not null) firmaMap[firma.Id] = firma.Ad;
        }

        return Ok(kayitlar.Select(k => ToDto(k, firmaMap)).ToList());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var kayit = await _repo.FindByIdAsync(id, ct);
        if (kayit is null) return NotFound();
        if (!await CanAccessAsync(kayit.FirmaId, ct)) return Forbid();

        var firma = await _firmalar.FindByIdAsync(kayit.FirmaId, ct);
        var firmaMap = firma is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [firma.Id] = firma.Ad };
        return Ok(ToDto(kayit, firmaMap));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] BelgeTipiUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var firmaId = await ResolveTargetFirmaIdAsync(request.FirmaId, ct);
        if (firmaId is null) return BadRequest(new { message = "Firma bulunamadı veya yetkiniz yok." });

        var ad = request.Ad.Trim();
        if (await _repo.ExistsByAdAsync(firmaId, ad, excludeId: null, ct))
            return Conflict(new { message = $"\"{ad}\" adında aktif bir belge tipi zaten var." });

        var kayit = new BelgeTipi
        {
            FirmaId = firmaId,
            Ad = ad,
            Aciklama = string.IsNullOrWhiteSpace(request.Aciklama) ? null : request.Aciklama.Trim(),
            GerekenImzaRolleri = request.GerekenImzaRolleri.Distinct().ToList(),
            KaseGerekli = request.KaseGerekli,
            Arsivlendi = false,
            OlusturanKullaniciId = User.GetUserId(),
        };
        await _repo.InsertAsync(kayit, ct);
        _audit.Log(User, AuditAksiyonlari.BelgeTipiCreate, "belge-tipi", kayit.Id, yeni: kayit.Ad);

        var firma = await _firmalar.FindByIdAsync(firmaId, ct);
        return CreatedAtAction(nameof(Get), new { id = kayit.Id }, ToDto(kayit,
            firma is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [firma.Id] = firma.Ad }));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string id, [FromBody] BelgeTipiUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var kayit = await _repo.FindByIdAsync(id, ct);
        if (kayit is null) return NotFound();
        if (!await CanAccessAsync(kayit.FirmaId, ct)) return Forbid();

        var ad = request.Ad.Trim();
        // FirmaId değişmiyor — update yalnız mevcut firma içinde anlamlı. Reparent
        // istenirse arşivleyip yenisini açmak doğru workflow.
        if (!kayit.Arsivlendi && await _repo.ExistsByAdAsync(kayit.FirmaId, ad, excludeId: id, ct))
            return Conflict(new { message = $"\"{ad}\" adında aktif bir belge tipi zaten var." });

        var oldArsivli = kayit.Arsivlendi;
        kayit.Ad = ad;
        kayit.Aciklama = string.IsNullOrWhiteSpace(request.Aciklama) ? null : request.Aciklama.Trim();
        kayit.GerekenImzaRolleri = request.GerekenImzaRolleri.Distinct().ToList();
        kayit.KaseGerekli = request.KaseGerekli;
        kayit.Arsivlendi = request.Arsivlendi;
        await _repo.ReplaceAsync(kayit, ct);

        var aksiyon = oldArsivli == kayit.Arsivlendi
            ? AuditAksiyonlari.BelgeTipiUpdate
            : kayit.Arsivlendi ? AuditAksiyonlari.BelgeTipiArchive : AuditAksiyonlari.BelgeTipiRestore;
        _audit.Log(User, aksiyon, "belge-tipi", kayit.Id, yeni: kayit.Ad);

        var firma = await _firmalar.FindByIdAsync(kayit.FirmaId, ct);
        return Ok(ToDto(kayit,
            firma is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [firma.Id] = firma.Ad }));
    }

    /// <summary>
    /// SayimBaskani kendi firmasından bağımsız çalışırken FirmaId yok — Sistem ise
    /// liste içinde firma bazlı görür. Bu yardımcı, SayimBaskani'nın hangi firmaya
    /// scope edileceğini DB'den çözer (önce primary FirmaId, yoksa FirmaIds'in ilki).
    /// </summary>
    private async Task<string?> ResolveCallerFirmaIdAsync(CancellationToken ct)
    {
        var primary = User.GetFirmaId();
        if (!string.IsNullOrEmpty(primary)) return primary;

        var uid = User.GetUserId();
        if (string.IsNullOrEmpty(uid)) return null;
        var dbUser = await _users.FindByIdAsync(uid, ct);
        if (dbUser is null) return null;
        return !string.IsNullOrEmpty(dbUser.FirmaId)
            ? dbUser.FirmaId
            : dbUser.FirmaIds.FirstOrDefault();
    }

    /// <summary>Insert sırasında hedef firmaId'yi çözer ve yetki kontrolü yapar.</summary>
    private async Task<string?> ResolveTargetFirmaIdAsync(string? requested, CancellationToken ct)
    {
        if (User.IsSistem())
        {
            if (string.IsNullOrEmpty(requested)) return null;
            var firma = await _firmalar.FindByIdAsync(requested, ct);
            return firma?.Id;
        }

        var scope = await ResolveCallerFirmaIdAsync(ct);
        if (string.IsNullOrEmpty(scope)) return null;
        // SayimBaskani'nin gönderdiği FirmaId görmezden gelinir; her zaman kendi firması.
        return scope;
    }

    private async Task<bool> CanAccessAsync(string firmaId, CancellationToken ct)
    {
        if (User.IsSistem()) return true;
        var scope = await ResolveCallerFirmaIdAsync(ct);
        return scope == firmaId;
    }

    private static BelgeTipiDto ToDto(BelgeTipi t, IReadOnlyDictionary<string, string> firmaMap) => new()
    {
        Id = t.Id,
        FirmaId = t.FirmaId,
        FirmaAdi = firmaMap.TryGetValue(t.FirmaId, out var ad) ? ad : null,
        Ad = t.Ad,
        Aciklama = t.Aciklama,
        GerekenImzaRolleri = t.GerekenImzaRolleri,
        KaseGerekli = t.KaseGerekli,
        Arsivlendi = t.Arsivlendi,
        OlusturmaTarihi = t.OlusturmaTarihi,
        GuncellenmeTarihi = t.GuncellenmeTarihi,
    };

    private IActionResult ValidationFailure(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
