import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { ApiError } from '../../core/http/api-error';
import { SystemInfo } from '../../core/api/system-info.models';
import { SystemInfoService } from '../../core/api/system-info.service';

type PageState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-system-info-page',
  templateUrl: './system-info.page.html',
  styleUrl: './system-info.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SystemInfoPage {
  private readonly service = inject(SystemInfoService);

  readonly state = signal<PageState>('loading');
  readonly info = signal<SystemInfo | null>(null);
  readonly errorMessage = signal<string>('');

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.errorMessage.set('');

    this.service.getSystemInfo().subscribe({
      next: (info) => {
        this.info.set(info);
        this.state.set('ready');
      },
      error: (error: ApiError) => {
        this.errorMessage.set(error.message);
        this.state.set('error');
      },
    });
  }

  /** True when the request succeeded but there is nothing to list - not an error (AC-7). */
  isEmpty(): boolean {
    return this.state() === 'ready' && (this.info()?.entries.length ?? 0) === 0;
  }
}
