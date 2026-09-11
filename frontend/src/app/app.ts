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
  protected readonly expandedGameIds = signal<ReadonlySet<string>>(new Set());
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

  protected toggleGame(gamePk: string): void {
    this.expandedGameIds.update((expandedGameIds) => {
      const nextExpandedGameIds = new Set(expandedGameIds);

      if (nextExpandedGameIds.has(gamePk)) {
        nextExpandedGameIds.delete(gamePk);
      } else {
        nextExpandedGameIds.add(gamePk);
      }

      return nextExpandedGameIds;
    });
  }

  protected isExpanded(gamePk: string): boolean {
    return this.expandedGameIds().has(gamePk);
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

  protected detailText(value: string | number | null | undefined): string {
    return value === null || value === undefined || value === '' ? '-' : value.toString();
  }

  protected seriesText(game: MlbGame): string {
    if (game.seriesGameNumber && game.gamesInSeries) {
      return `Game ${game.seriesGameNumber} of ${game.gamesInSeries}`;
    }

    return '-';
  }

  protected inningText(game: MlbGame): string {
    if (!game.currentInningOrdinal) {
      return game.scheduledInnings ? `${game.scheduledInnings} innings` : '-';
    }

    return game.inningHalf ? `${game.inningHalf} ${game.currentInningOrdinal}` : game.currentInningOrdinal;
  }

  protected countText(game: MlbGame): string {
    if (game.balls === null || game.strikes === null || game.outs === null) {
      return '-';
    }

    return `${game.balls}-${game.strikes}, ${game.outs} out${game.outs === 1 ? '' : 's'}`;
  }

  protected pitcherText(value: string | null): string {
    return value ?? 'TBD';
  }

  protected hasList(value: readonly unknown[] | null | undefined): boolean {
    return Boolean(value?.length);
  }

  protected pitcherCountText(pitchers: MlbPitcherLine[] | null | undefined): string {
    const pitcherCount = pitchers?.length ?? 0;
    return `${pitcherCount} pitcher${pitcherCount === 1 ? '' : 's'} used`;
  }

  protected battingLine(batter: MlbBatterLine): string {
    return [
      `${this.detailText(batter.hits)}-${this.detailText(batter.atBats)}`,
      `${this.detailText(batter.rbi)} RBI`,
      `${this.detailText(batter.runs)} R`,
      `${this.detailText(batter.homeRuns)} HR`,
      `${this.detailText(batter.walks)} BB`,
      `${this.detailText(batter.strikeOuts)} K`
    ].join(' · ');
  }

  protected teamLogoUrl(teamId: number | null): string {
    return teamId
      ? `https://www.mlbstatic.com/team-logos/${teamId}.svg`
      : 'https://www.mlbstatic.com/team-logos/league-on-dark/1.svg';
  }

  protected playerHeadshotUrl(playerId: number | null): string {
    return playerId
      ? `https://img.mlbstatic.com/mlb-photos/image/upload/w_96,q_auto:best/v1/people/${playerId}/headshot/67/current`
      : 'https://www.mlbstatic.com/team-logos/league-on-dark/1.svg';
  }

  protected hideBrokenImage(event: Event): void {
    const image = event.target;

    if (image instanceof HTMLImageElement) {
      image.style.display = 'none';
    }
  }
}

interface MlbGame {
  gamePk: string;
  gameDate: string;
  gameTimeUtc: string | null;
  awayTeamId: number | null;
  awayTeam: string;
  awayRecord: string | null;
  awayProbablePitcher: string | null;
  awayWinner: boolean | null;
  homeTeamId: number | null;
  homeTeam: string;
  homeRecord: string | null;
  homeProbablePitcher: string | null;
  homeWinner: boolean | null;
  status: string;
  awayScore: number | null;
  homeScore: number | null;
  venueId: number | null;
  venueName: string | null;
  gameType: string | null;
  dayNight: string | null;
  scheduledInnings: number | null;
  gamesInSeries: number | null;
  seriesGameNumber: number | null;
  seriesDescription: string | null;
  doubleHeader: string | null;
  currentInning: number | null;
  currentInningOrdinal: string | null;
  inningHalf: string | null;
  balls: number | null;
  strikes: number | null;
  outs: number | null;
  awayHits: number | null;
  awayErrors: number | null;
  homeHits: number | null;
  homeErrors: number | null;
  awayPitchers: MlbPitcherLine[] | null;
  homePitchers: MlbPitcherLine[] | null;
  awayBattingLeaders: MlbBatterLine[] | null;
  homeBattingLeaders: MlbBatterLine[] | null;
  homeRunHitters: MlbBatterLine[] | null;
  highlights: string[] | null;
}

interface MlbPitcherLine {
  playerId: number | null;
  name: string;
  inningsPitched: string | null;
  hits: number | null;
  runs: number | null;
  earnedRuns: number | null;
  strikeOuts: number | null;
  walks: number | null;
  pitches: number | null;
  summary: string | null;
}

interface MlbBatterLine {
  playerId: number | null;
  name: string;
  team: string;
  atBats: number | null;
  runs: number | null;
  hits: number | null;
  doubles: number | null;
  triples: number | null;
  homeRuns: number | null;
  rbi: number | null;
  walks: number | null;
  strikeOuts: number | null;
  summary: string | null;
}
