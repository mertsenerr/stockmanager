# SayımLink — Deployment Notları

> Production altyapısı, ortam değişkenleri, redeploy adımları, yaygın hata
> giderme. Yeni katılan biri buradan başlayabilir.

## Topoloji

```
[Browser]  →  Netlify (Angular SPA)  →  Render (.NET 9 API + SignalR)  →  MongoDB Atlas
                                              ↓
                                          Resend (e-posta)
```

| Katman | Sağlayıcı | URL |
|---|---|---|
| Frontend | Netlify | https://&lt;netlify-site&gt;.netlify.app |
| Backend  | Render (Docker)  | https://stockmanager-d6nm.onrender.com |
| Database | MongoDB Atlas (M0) | `mongodb+srv://…/sayimlink` |
| E-posta  | Resend | https://resend.com |

---

## Frontend — Netlify

- **Repo path**: `frontend/`
- **Config dosyası**: `frontend/netlify.toml`
- **Build command**: `npm run build`
- **Publish dir**: `dist/frontend/browser` (`netlify.toml`'a göre, base=`frontend`)
- **SPA routing**: `frontend/src/_redirects` → build sırasında çıktıya kopyalanır
- **Production env değerleri**: `frontend/src/environments/environment.ts`
  içinde gömülü (build-time). Environment variable Netlify'da gerekmez.

### Redeploy
1. `main` branch'a push → Netlify otomatik build başlatır.
2. Manuel: Netlify dashboard → Deploys → "Trigger deploy".

### URL veya backend değişirse
1. `frontend/src/environments/environment.ts` içindeki `apiBaseUrl` ve
   `hubBaseUrl`'i güncelle.
2. Commit + push → Netlify yeniden build alır.

---

## Backend — Render

- **Repo path**: `backend/`
- **Runtime**: Docker (`backend/Dockerfile`)
- **Base image build**: `mcr.microsoft.com/dotnet/sdk:9.0`
- **Base image runtime**: `mcr.microsoft.com/dotnet/aspnet:9.0`
- **Port**: Render `PORT` env enjekte eder (~10000); Dockerfile
  `ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}` ile bind eder.

### Required environment variables (Render dashboard)

| Key | Örnek | Gizli mi? |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | hayır |
| `MongoDb__ConnectionString` | `mongodb+srv://USER:PASS@cluster.mongodb.net/?retryWrites=true&w=majority` | **evet** |
| `MongoDb__DatabaseName` | `sayimlink` | hayır |
| `Jwt__Secret` | (≥32 karakter rastgele — `openssl rand -base64 48`) | **evet** |
| `Jwt__Issuer` | `SayimLink` | hayır |
| `Jwt__Audience` | `SayimLinkClients` | hayır |
| `Jwt__AccessTokenMinutes` | `30` | hayır |
| `Jwt__RefreshTokenDays` | `14` | hayır |
| `Cors__AllowedOrigins__0` | `https://<netlify-site>.netlify.app` | hayır |
| `Cors__AllowedOrigins__1` | `https://<custom-domain>` (varsa) | hayır |
| `Resend__ApiKey` | `re_xxxxxxxxxxxxxxx` | **evet** |
| `Resend__FromEmail` | `noreply@syncompare.com` (Resend'de doğrulanmış domain üzerinde olmalı) | hayır |
| `Resend__FromName` | `SayımLink` | hayır |
| `Resend__PasswordResetUrlTemplate` | `https://syncompare.com/reset-password?token={token}` (literal `{token}` placeholder) | hayır |
| `Seed__AdminEmail` | `admin@syncompare.com` | hayır |
| `Seed__AdminPassword` | (güçlü) | **evet** |

> **Önceki sürümlerde yanlışlıkla `Resend__FromAddress` olarak belgelenmişti — kod karşılığı yoktur.** `ResendSettings.FromEmail` propertysi `Resend__FromEmail` env var'ı ile bağlanır. Stale bir `Resend__FromAddress` env var'ı varsa Render dashboard'dan sil.

> `PORT` Render tarafından otomatik enjekte edilir — manuel ekleme.

### Redeploy
1. `main` branch'a push → Render otomatik build başlatır.
2. Manuel: Render dashboard → service → **Manual Deploy** → "Deploy latest commit".
3. Env değişikliği → service otomatik restart eder (yeni build yok).

### Health check
- Endpoint: `/api/health`
- Render dashboard → Settings → **Health Check Path** = `/api/health` set
  edilmeli. Failed → otomatik restart.

---

## MongoDB Atlas

- **Cluster**: M0 (free tier — 512 MB, 500 conn)
- **Database**: `sayimlink`
- **Collections**: `users`, `refresh_tokens`, `firmalar`, `magazalar`,
  `atamalar`, `sayim_oturumlari`, `audit_logs`
- **TTL index**: `audit_logs.tarih` 180 gün

### Network Access
- Render egress IP'leri whitelist'te olmalı. Geçici: `0.0.0.0/0`.
- Doğru çözüm: Render dashboard → Connect → outbound IP listesini al,
  Atlas → Network Access → IP Access List'e ekle.

### Database User
- Yetki: **`readWrite@sayimlink`** (sadece bu DB).
- `atlasAdmin` veya `readWriteAnyDatabase` ASLA kullanma.

---

## Yaygın sorun giderme

| Belirti | Olası neden | Çözüm |
|---|---|---|
| Tarayıcıda CORS hatası | `Cors__AllowedOrigins` Netlify URL'ini içermiyor | Render env'e ekle, otomatik restart bekle |
| 502/504 ilk istekte | Render free tier soğuk başlangıç (15dk idle sonrası) | 30-60sn bekle veya UptimeRobot ile warm tut |
| `IDX10503: Signature validation failed` | `Jwt__Secret` env'i değişti | Tüm tokenları geçersiz kıldı, yeniden login |
| `MongoConnectionException` | Atlas IP whitelist veya conn string | Network Access ve `MongoDb__ConnectionString` kontrol et |
| Login 500 + Mongo logu yok | `Jwt__Secret` boş veya 32 karakterden kısa | Yeni secret üret, env güncelle |
| Sayfa F5 → 404 (Netlify) | `_redirects` build çıktısında yok | `angular.json` `assets` listesinde `src/_redirects` mi? |
| SignalR bağlanmıyor | `hubBaseUrl` yanlış veya CORS credentials | `withCredentials: true` set, origin Render'da explicit listede mi? |

---

## Monitoring (önerilen)

- **UptimeRobot** — `/api/health` her 5 dk ping → cold start engeli + downtime alarm
- **Render Logs** — service → Logs sekmesi, son 7 gün
- **Atlas Metrics** — connection count, op/sec, storage usage
- **Browser Sentry** — opsiyonel; frontend error reporting

---

## Lokal geliştirme

```bash
# Backend
cd backend
dotnet run
# → http://localhost:5080

# Frontend (ayrı terminal)
cd frontend
npm install
npm start
# → http://localhost:4200
```

`environment.development.ts` localhost'a işaret eder; `ng serve` otomatik
o dosyayı kullanır. `ng build` production env'i kullanır.
