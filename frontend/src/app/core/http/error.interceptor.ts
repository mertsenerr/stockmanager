import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../../shared/ui/toast/toast.service';

const SILENT_PATHS = [
  '/api/auth/login',
  '/api/auth/refresh',
  '/api/auth/me',
  '/api/auth/forgot-password',
  '/api/auth/reset-password',
];

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      // Skip silent paths — components handle their own auth/login errors.
      if (SILENT_PATHS.some((p) => req.url.includes(p))) {
        return throwError(() => err);
      }

      // 401 is handled upstream (auth interceptor), don't double-toast.
      // 400 typically carries field-level validation that components render inline.
      if (err.status === 401 || err.status === 400) {
        return throwError(() => err);
      }

      const serverMsg = (err.error as { message?: string } | null | undefined)?.message;
      if (err.status === 403) {
        toast.error(serverMsg ?? 'Bu işlem için yetkin yok.');
      } else if (err.status === 409) {
        toast.error(serverMsg ?? 'Çakışma — işlem reddedildi.');
      } else if (err.status === 404) {
        toast.error(serverMsg ?? 'Kayıt bulunamadı.');
      } else if (err.status === 0) {
        toast.error('Sunucuya ulaşılamıyor.');
      } else if (err.status >= 500) {
        toast.error('Sunucu hatası — sistem yöneticisine bildirildi.');
      }

      return throwError(() => err);
    }),
  );
};
