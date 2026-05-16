# SayımLink — Deployment Notları

> Production altyapısı, ortam değişkenleri, redeploy adımları, yaygın hata
> giderme. Yeni katılan biri buradan başlayabilir.

## Topoloji

```
[Browser]  →  Cloudflare Pages (Angular SPA)  →  Render (.NET 9 API + SignalR)  →  MongoDB Atlas
                                                       ↓
                                                   Resend (e-posta)
```

| Katman | Sağlayıcı | URL |
|---|---|---|
| Frontend | Cloudflare Pages | https://syncompare.com (custom domain) |
| Backend  | Render (Docker)  | https://stockmanager-d6nm.onrender.com |
| Database | MongoDB Atlas (M0) | `mongodb+srv://…/sayimlink` |
| E-posta  | Resend | https://resend.com |

---

## Frontend — Cloudflare Pages

- **Repo path**: `frontend/`
- **Framework preset**: Angular (CF Pages dashboard → Build settings)
- **Build command**: `npm run build`
- **Build output dir**: `dist/frontend/browser`
- **Root directory (advanced)**: `frontend`
- **Node version**: `20` (set via `NODE_VERSION=20` env var in CF Pages dashboard)
- **SPA routing + .well-known passthrough**: `frontend/public/_redirects`
  → build sırasında publish dir'in köküne kopyalanır, CF Pages native parse eder
- **Security headers**: `frontend/public/_headers` → CF Pages publish dir'den
  okuyup eşleşen response'lara uygular (HSTS, CSP, X-Frame, COOP, CORP, vs.)
- **`.well-known/security.txt`**: `frontend/public/.well-known/security.txt`
  (RFC 9116). `_redirects` içindeki passthrough kuralı SPA fallback'tan önce
  çalışır, dosya gerçek `text/plain` olarak servis edilir.
- **Production env değerleri**: `frontend/src/environments/environment.ts`
  içinde gömülü (build-time). CF Pages tarafında env var gerekmez.

### Redeploy
1. `main` branch'a push → CF Pages otomatik build başlatır.
2. Manuel: Cloudflare dashboard → Workers & Pages → proje → **Create
   deployment** veya commit'in yanındaki "Retry deployment".

### URL veya backend değişirse
1. `frontend/src/environments/environment.ts` içindeki `apiBaseUrl` ve
   `hubBaseUrl`'i güncelle.
2. `frontend/public/_headers` içindeki CSP `connect-src` / `frame-src`
   listesinde backend origin'i geçiyorsa onu da güncelle.
3. Commit + push → CF Pages yeniden build alır.

### Custom domain
1. CF Pages projesi → **Custom domains** → "Set up a custom domain"
2. `syncompare.com` ve `www.syncompare.com` ikisini de ekle (apex ↔ www 301
   yönlendirmesi otomatik kurulur, böylece eski host kaynaklı 522/SAN
   problemi kapanır).
3. DNS otomatik proxy'lenir (turuncu bulut). Bu durumda **CAA** kaydını
   `CAA 0 issue "letsencrypt.org"` + `CAA 0 issue "pki.goog"` olarak set et
   (CF Pages bu iki CA'yı kullanıyor).

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
| `Cors__AllowedOrigins__0` | `https://syncompare.com` | hayır |
| `Cors__AllowedOrigins__1` | `https://<project>.pages.dev` (CF Pages default URL — preview/fallback) | hayır |
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
| Tarayıcıda CORS hatası | `Cors__AllowedOrigins` Cloudflare Pages URL'ini (custom domain veya `*.pages.dev`) içermiyor | Render env'e ekle, otomatik restart bekle |
| 502/504 ilk istekte | Render free tier soğuk başlangıç (15dk idle sonrası) | 30-60sn bekle veya UptimeRobot ile warm tut |
| `IDX10503: Signature validation failed` | `Jwt__Secret` env'i değişti | Tüm tokenları geçersiz kıldı, yeniden login |
| `MongoConnectionException` | Atlas IP whitelist veya conn string | Network Access ve `MongoDb__ConnectionString` kontrol et |
| Login 500 + Mongo logu yok | `Jwt__Secret` boş veya 32 karakterden kısa | Yeni secret üret, env güncelle |
| Sayfa F5 → 404 (CF Pages) | `_redirects` build çıktısının kökünde yok | `frontend/public/_redirects` var mı? `public/` zaten `angular.json`'da glob asset olduğu için elle eklemeye gerek yok |
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
