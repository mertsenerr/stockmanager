# SayımLink

> Canlı, çok kullanıcılı, rol tabanlı sayım karşılaştırma ve koordinasyon platformu.
> Live, multi-user, role-based stock-count reconciliation and coordination platform.

---

## Türkçe

### Problem

Sayım ekipleri sahada barkod terminalleri ile ürünleri sayar, veriyi mağazadaki masaüstü sayım uygulamasına aktarır, fark çıkan ürünleri Excel olarak dışa aktarır. Bu Excel müdür–sayım başkanı–depo sorumlusu arasında telefon/WhatsApp ile gezdirilirken **saymanlar atıl bekler**. Hangi ürünün tekrar sayılacağı yavaş ve koordinasyonsuz ilerler.

### Çözüm

SayımLink, Excel verisini canlı bir tabloya dönüştürür. Sayım başkanı uzaktan, mağaza müdürü yerinden, sayım yöneticisi sahadan **aynı tabloyu eş zamanlı düzenler**; saymanlar "tekrar say" işaretli satırları anında görüp aksiyona geçer.

### Roller

| Rol | Yetki |
|---|---|
| Admin (Sayım Başkanı) | Tam CRUD, oturum kilitleme, tüm hücre düzenleme |
| Sayım Yöneticisi | Excel yükleme, oturum yönetimi, satır düzenleme |
| Mağaza Müdürü | Kendi mağazasını görüntüleme, sınırlı düzenleme, yorum |
| Sayman | Atandığı oturumu görüntüleme, "tekrar say" satırlarını sayma |

### Teknoloji

- **Frontend:** Angular 19 (standalone, signals), TypeScript strict, Tailwind CSS, AG Grid Community, FullCalendar, `@microsoft/signalr`, `xlsx`, Lucide
- **Backend:** .NET Core 9 Web API, SignalR Hub, MongoDB.Driver, JWT + refresh, BCrypt, Serilog
- **Veritabanı:** MongoDB Atlas (prod) / local MongoDB (dev)
- **Deploy:** Cloudflare Pages (frontend) + Render (backend)

### Repo Yapısı

```
sayim-link/
├── frontend/          # Angular 19 SPA
├── backend/           # .NET 9 Web API + SignalR Hub
├── .env.example       # ortak environment şablonu
└── README.md
```

### Geliştirme — Hızlı Başlangıç

**Önkoşullar:** .NET 9 SDK, Node.js 20+, MongoDB (yerel) veya MongoDB Atlas bağlantı dizesi.

```bash
# Backend
cd backend
cp appsettings.Example.json appsettings.Development.json
# appsettings.Development.json sadece NON-secret ayarları içerir.
# Sırlar (MongoDb__ConnectionString, Jwt__Secret) için "Local Development Setup"
# bölümüne bak.
dotnet restore
dotnet run

# Frontend (yeni terminal)
cd frontend
npm install
npm start
```

Frontend `http://localhost:4200`, backend `http://localhost:5080` üzerinde ayağa kalkar. Bağlantı kontrolü: `GET /api/health`.

### Local Development Setup — Sırlar (Secrets)

Backend, geliştirme sırlarını **`appsettings.Development.json` içinde değil**, .NET'in `dotnet user-secrets` mekanizmasında saklar. Bu sayede sırlar disk üzerinde repo dışında, kullanıcı profilinde tutulur ve yanlışlıkla commit edilmeleri imkânsızlaşır.

İlk kurulumda (her makinede bir kez):

```bash
cd backend

# Lokal bir Mongo veya Atlas bağlantı dizesi
dotnet user-secrets set "MongoDb:ConnectionString" "mongodb://localhost:27017"

# JWT için yüksek entropili bir secret üret (en az 32 karakter)
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"

# (opsiyonel) Resend API key — yoksa parola sıfırlama linki dev modda log'a düşer
dotnet user-secrets set "Resend:ApiKey" "re_xxxxxxxxxxxxx"
```

Mevcut sırları görmek için: `dotnet user-secrets list`.
Sırları silmek için: `dotnet user-secrets clear`.

Sırlar Windows'ta `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, macOS/Linux'ta `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` dosyasında tutulur.

> **Production'da `dotnet user-secrets` kullanılmaz.** Render env var'ları (`MongoDb__ConnectionString`, `Jwt__Secret`, `Resend__ApiKey`, …) override eder. Detay için `deployment.md`.

### Geliştirme Fazları

- [x] **Faz 0** — İskelet (frontend + backend ayakta, design token'lar, health check)
- [x] **Faz 1** — Kimlik doğrulama (JWT + refresh httpOnly cookie, login/forgot/reset, admin seed)
- [x] **Faz 2** — Firma / Mağaza / Kullanıcı CRUD (Leaflet harita + soft delete + müdür-mağaza otomatik senkron)
- [x] **Faz 3** — Takvim & atamalar (FullCalendar ay/hafta/gün, drag-drop, firma renk paleti, rol bazlı görünüm)
- [x] **Faz 4** — Sayım oturumu + Excel yükleme + kolon eşleme (SheetJS parse, durum lifecycle, embedded Urunler)
- [x] **Faz 5** — Canlı karşılaştırma ekranı (AG Grid + SignalR + hücre kilidi + aktivite akışı + yorum paneli)
- [x] **Faz 6** — Audit log + raporlar (mağaza sapma, sayman performansı, ClosedXML Excel export)
- [x] **Faz 7** — Polish (skeleton, mobile responsive, global error handler, audit kapsam genişletme)

### Production Notları

- **Render free tier cold-start:** Backend 15dk idle sonrası uyur. `GET /api/health` endpoint'ini cron-job.org / UptimeRobot ile her 10 dakikada bir ping at — uyumayı önler.
- **MongoDB Atlas IP allowlist:** Render egress IP'lerini ekle ya da development için `0.0.0.0/0` (intranet kullanım için kabul edilir).
- **JWT secret:** Render env var `Jwt__Secret` (en az 32 karakter, rastgele). `appsettings.Production.json` git ignore'da.
- **CORS:** Cloudflare Pages origin'ini `Cors__AllowedOrigins__0` env var'ı ile geç. SignalR + cookies için `AllowCredentials()` zorunlu, wildcard origin yasak.
- **Audit retention:** 180 günlük TTL index ile otomatik temizlenir; SOX/KVK uyumu için daha uzun istersen `AuditLogRepository` üzerindeki `ExpireAfter` değerini değiştir.

### Commit Konvansiyonu

Conventional Commits: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`, `test:`, `style:`.

---

## English

### Problem

Stock-count teams scan SKUs with barcode terminals on-site, push the data to the customer's desktop counting app, and export discrepancies as Excel. That Excel then bounces between manager, count chief, and warehouse lead via phone/WhatsApp **while counters sit idle**. Decisions about which SKUs to recount are slow and uncoordinated.

### Solution

SayımLink turns the Excel into a live table. Count chief (remote), store manager (on-site), and field lead (in-store) edit **the same rows simultaneously**. Counters see "recount" flags instantly and act.

### Stack

Same as Turkish section above. Angular 19 + .NET 9 + MongoDB + SignalR. Deployed on Cloudflare Pages + Render.

### Quick Start

```bash
# Backend
cd backend
cp appsettings.Example.json appsettings.Development.json
# Fill in MongoDB and JWT values
dotnet restore && dotnet run

# Frontend
cd frontend
npm install && npm start
```

Frontend at `http://localhost:4200`, backend at `http://localhost:5080`. Health check: `GET /api/health`.

### License

Proprietary — internal tooling for the SayımLink operations team. All rights reserved.
