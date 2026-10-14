import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SystemInfo } from './system-info.models';

@Injectable({ providedIn: 'root' })
export class SystemInfoService {
  private readonly http = inject(HttpClient);

  getSystemInfo(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>('/api/system-info');
  }
}
