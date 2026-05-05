import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Observable, map, of } from 'rxjs';
import { AuthService } from './auth.service';
import { UserRole } from './auth.models';

export const authGuard: CanActivateFn = (_route, state):
  | boolean
  | UrlTree
  | Observable<boolean | UrlTree> => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;

  if (auth.bootstrapping()) {
    return auth.bootstrap().pipe(
      map((user) => user !== null
        ? true
        : router.createUrlTree(['/login'], { queryParams: { redirect: state.url } })),
    );
  }

  return router.createUrlTree(['/login'], { queryParams: { redirect: state.url } });
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/']);
  }
  if (auth.bootstrapping()) {
    return auth.bootstrap().pipe(
      map((user) => user === null ? true : router.createUrlTree(['/'])),
    );
  }
  return of(true);
};

export function roleGuard(...roles: UserRole[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      return router.createUrlTree(['/login']);
    }
    return auth.hasRole(...roles)
      ? true
      : router.createUrlTree(['/']);
  };
}
