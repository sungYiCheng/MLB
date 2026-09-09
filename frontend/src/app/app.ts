import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly games = signal<MlbGame[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly totalGames = computed(() => this.games().length);
  protected readonly liveGames = computed(
    () => this.games().filter((game) => this.isLive(game.status)).length
  );
  protected readonly finalGames = computed(
    () => this.games().filter((game) => game.status.toLowerCase().includes('final')).length
  );

  ngOnInit(): void {
    this.loadGames();
  }

  protected loadGames(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.http.get<MlbGame[]>('/api/games/today').subscribe({
      next: (games) => {
        this.games.set(games);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Could not load the MLB schedule. Check that the backend API is running.');
        this.isLoading.set(false);
      }
    });
  }

  protected isLive(status: string): boolean {
    const normalizedStatus = status.toLowerCase();
    return normalizedStatus.includes('progress') || normalizedStatus.includes('warmup');
  }

  protected formatTaipeiTime(value: string | null): string {
    if (!value) {
      return 'Time TBD';
    }

    return new Intl.DateTimeFormat('en-US', {
      hour: 'numeric',
      minute: '2-digit',
      timeZone: 'Asia/Taipei',
      timeZoneName: 'short'
    }).format(new Date(value));
  }

  protected scoreText(score: number | null): string {
    return score === null ? '-' : score.toString();
  }

  protected teamLogoUrl(teamId: number | null): string {
    return teamId
      ? `https://www.mlbstatic.com/team-logos/${teamId}.svg`
      : 'https://www.mlbstatic.com/team-logos/league-on-dark/1.svg';
  }
}

interface MlbGame {
  gamePk: string;
  gameDate: string;
  gameTimeUtc: string | null;
  awayTeamId: number | null;
  awayTeam: string;
  homeTeamId: number | null;
  homeTeam: string;
  status: string;
  awayScore: number | null;
  homeScore: number | null;
}
