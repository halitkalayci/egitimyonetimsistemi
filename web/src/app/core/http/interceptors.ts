import { HttpInterceptorFn } from '@angular/common/http';

import { authInterceptor } from '../auth/auth.interceptor';
import { errorInterceptor } from './error.interceptor';

/**
 * The one ordering of the HTTP pipeline, shared by the app and its tests so they cannot drift.
 * Angular nests interceptors so that the FIRST one listed sees a response LAST. The error
 * interceptor therefore comes first: the auth interceptor sits closer to the server, sees the raw
 * 401, renews and replays, and only what it gives up on is turned into a user message.
 */
export const appInterceptors: HttpInterceptorFn[] = [errorInterceptor, authInterceptor];
