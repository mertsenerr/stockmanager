import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const SKIP_REFRESH_PATHS = ['/api/auth/refresh', '/api/auth/login', '/api/auth/forgot-password', '/api/auth/reset-password'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const authedReq = attachToken(req, auth.accessToken());

  return next(authedReq).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || isSkipped(req.url)) {
        return throwError(() => err);
      }
      return handleUnauthorized(req, next, auth, router);
    }),
  );
};

function attachToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  if (!token) return req;
  return req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
}

function isSkipped(url: string): boolean {
  return SKIP_REFRESH_PATHS.some((p) => url.includes(p));
}

function handleUnauthorized(
  originalReq: HttpRequest<unknown>,
  next: HttpHandlerFn,
  auth: AuthService,
  router: Router,
): Observable<HttpEvent<unknown>> {
  return auth.refresh().pipe(
    switchMap((res): Observable<HttpEvent<unknown>> => {
      if (!res) {
        router.navigate(['/login'], { queryParams: { reason: 'expired' } });
        return throwError(() => new HttpErrorResponse({ status: 401 }));
      }
      return next(attachToken(originalReq, res.accessToken));
    }),
  );
}
