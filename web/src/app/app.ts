import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { SessionBar } from './shared/session-bar/session-bar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, SessionBar],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
