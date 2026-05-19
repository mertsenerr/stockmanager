namespace SayimLink.Api.Dtos.Admin;

public sealed class ImzaSlotDto
{
    public string Rol { get; set; } = string.Empty;
    public string Konum { get; set; } = string.Empty;
}

public sealed class BelgeTipiDto
{
    public string Id { get; set; } = string.Empty;
    public string FirmaId { get; set; } = string.Empty;
    public string? FirmaAdi { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public IReadOnlyList<ImzaSlotDto> ImzaSlotlari { get; set; } = [];
    public bool KaseGerekli { get; set; }
    public string? KaseKonum { get; set; }
    public bool Arsivlendi { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
    public DateTime? GuncellenmeTarihi { get; set; }
}

public sealed class BelgeTipiUpsertRequest
{
    /// <summary>
    /// Belge tipinin bağlı olduğu firma. SayimBaskani için kendi FirmaId'sine
    /// override edilir; Sistem'in başka firmaya yazması için açıkça verilmesi gerekir.
    /// </summary>
    public string? FirmaId { get; set; }

    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public List<ImzaSlotDto> ImzaSlotlari { get; set; } = [];
    public bool KaseGerekli { get; set; }
    public string? KaseKonum { get; set; }
    public bool Arsivlendi { get; set; }
}
