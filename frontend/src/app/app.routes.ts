import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'forgot-password',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then(
        (m) => m.ForgotPasswordComponent,
      ),
  },
  {
    path: 'reset-password',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password.component').then(
        (m) => m.ResetPasswordComponent,
      ),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/register/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'verify-email',
    loadComponent: () =>
      import('./features/auth/verify-email/verify-email.component').then(
        (m) => m.VerifyEmailComponent,
      ),
  },
  {
    path: 'two-factor',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/two-factor/two-factor.component').then(
        (m) => m.TwoFactorComponent,
      ),
  },
  {
    path: 'password-change-undo',
    loadComponent: () =>
      import('./features/auth/password-change-undo/password-change-undo.component').then(
        (m) => m.PasswordChangeUndoComponent,
      ),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/shell/app-shell.component').then((m) => m.AppShellComponent),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'takvim',
        canActivate: [roleGuard('Sistem')],
        loadComponent: () =>
          import('./features/takvim/takvim.component').then((m) => m.TakvimComponent),
      },
      {
        path: 'oturumlar',
        loadComponent: () =>
          import('./features/sayim/oturumlar/oturumlar.component').then((m) => m.OturumlarComponent),
      },
      {
        path: 'oturumlar/:id',
        loadComponent: () =>
          import('./features/sayim/oturum-detail/oturum-detail.component').then((m) => m.OturumDetailComponent),
      },
      {
        path: 'oturumlar/:id/canli',
        loadComponent: () =>
          import('./features/sayim/live/oturum-live.component').then((m) => m.OturumLiveComponent),
      },
      {
        path: 'firmalar',
        canActivate: [roleGuard('Sistem', 'SayimBaskani')],
        loadComponent: () =>
          import('./features/admin/firmalar/firmalar.component').then((m) => m.FirmalarComponent),
      },
      {
        path: 'magazalar',
        canActivate: [roleGuard('Sistem', 'SayimBaskani')],
        loadComponent: () =>
          import('./features/admin/magazalar/magazalar.component').then((m) => m.MagazalarComponent),
      },
      {
        path: 'kullanicilar',
        canActivate: [roleGuard('Sistem')],
        loadComponent: () =>
          import('./features/admin/kullanicilar/kullanicilar.component').then((m) => m.KullanicilarComponent),
      },
      {
        path: 'ozel-raporlar',
        loadComponent: () =>
          import('./features/ozel-raporlar/ozel-raporlar.component').then((m) => m.OzelRaporlarComponent),
      },
      {
        path: 'arkadaslar',
        loadComponent: () =>
          import('./features/arkadaslar/arkadaslar.component').then((m) => m.ArkadaslarComponent),
      },
      {
        path: 'audit',
        canActivate: [roleGuard('Sistem')],
        loadComponent: () =>
          import('./features/rapor/audit/audit.component').then((m) => m.AuditComponent),
      },
      {
        path: 'ayarlar',
        loadComponent: () =>
          import('./features/ayarlar/ayarlar-shell.component').then((m) => m.AyarlarShellComponent),
        children: [
          { path: '', redirectTo: 'profil', pathMatch: 'full' },
          {
            path: 'profil',
            loadComponent: () =>
              import('./features/ayarlar/profil/profil.component').then((m) => m.ProfilComponent),
          },
          {
            path: 'genel',
            loadComponent: () =>
              import('./features/ayarlar/genel/genel.component').then((m) => m.GenelComponent),
          },
          {
            path: 'guvenlik',
            loadComponent: () =>
              import('./features/ayarlar/guvenlik/guvenlik.component').then((m) => m.GuvenlikComponent),
          },
          {
            path: 'bildirimler',
            loadComponent: () =>
              import('./features/ayarlar/bildirimler/bildirimler.component').then((m) => m.BildirimlerComponent),
          },
          {
            path: 'belge-tipleri',
            canActivate: [roleGuard('Sistem', 'SayimBaskani')],
            loadComponent: () =>
              import('./features/belge-tipleri/belge-tipleri.component').then((m) => m.BelgeTipleriComponent),
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
