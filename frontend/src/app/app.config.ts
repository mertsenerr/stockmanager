import {
  ApplicationConfig,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
} from '@angular/common/http';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { errorInterceptor } from './core/http/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withViewTransitions({
        onViewTransitionCreated: ({ to }) => {
          // Auth-flow directional slide: login → register/forgot/reset = "forward"
          // (wave slides right-to-left); coming back to login = "backward"
          // (wave slides left-to-right). Stored on <html> so CSS custom props flip.
          const fromPath = window.location.pathname;
          const toPath = '/' + (to.routeConfig?.path ?? '');
          const authPaths = ['/login', '/register', '/forgot-password', '/reset-password'];
          const isAuthFlow =
            authPaths.some((p) => fromPath.startsWith(p)) &&
            authPaths.some((p) => toPath.startsWith(p));
          if (isAuthFlow) {
            document.documentElement.dataset['navDirection'] =
              toPath === '/login' ? 'backward' : 'forward';
          } else {
            delete document.documentElement.dataset['navDirection'];
          }
        },
      }),
    ),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, errorInterceptor])),
  ],
};
