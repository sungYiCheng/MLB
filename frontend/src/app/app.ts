import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { forkJoin } from 'rxjs';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly games = signal<MlbGame[]>([]);
  protected readonly standings = signal<MlbStandings | null>(null);
  protected readonly statLeaders = signal<MlbStatLeaders | null>(null);
  protected readonly expandedGameIds = signal<ReadonlySet<string>>(new Set());
  protected readonly expandedSections = signal<ReadonlySet<SectionKey>>(
    new Set(['leaders', 'divisionStandings', 'wildCard', 'games'])
  );
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

    forkJoin({
      games: this.http.get<MlbGame[]>('/api/games/today'),
      standings: this.http.get<MlbStandings>('/api/standings'),
      statLeaders: this.http.get<MlbStatLeaders>('/api/stat-leaders')
    }).subscribe({
      next: ({ games, standings, statLeaders }) => {
        this.games.set(games);
        this.standings.set(standings);
        this.statLeaders.set(statLeaders);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Could not load MLB data. Check that the backend API is running.');
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

  protected toggleSection(sectionKey: SectionKey): void {
    this.expandedSections.update((expandedSections) => {
      const nextExpandedSections = new Set(expandedSections);

      if (nextExpandedSections.has(sectionKey)) {
        nextExpandedSections.delete(sectionKey);
      } else {
        nextExpandedSections.add(sectionKey);
      }

      return nextExpandedSections;
    });
  }

  protected isSectionExpanded(sectionKey: SectionKey): boolean {
    return this.expandedSections().has(sectionKey);
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

  protected batterSeasonLine(batter: MlbBatterLine): string {
    return [
      `AVG ${this.detailText(batter.seasonAverage)}`,
      `OBP ${this.detailText(batter.seasonOnBasePercentage)}`,
      `SLG ${this.detailText(batter.seasonSluggingPercentage)}`,
      `OPS ${this.detailText(batter.seasonOps)}`,
      `${this.detailText(batter.seasonHomeRuns)} HR`,
      `${this.detailText(batter.seasonRbi)} RBI`,
      `${this.detailText(batter.seasonStolenBases)} SB`
    ].join(' · ');
  }

  protected pitcherSeasonLine(pitcher: MlbPitcherLine): string {
    return [
      `${this.detailText(pitcher.seasonWins)}-${this.detailText(pitcher.seasonLosses)}`,
      `ERA ${this.detailText(pitcher.seasonEra)}`,
      `WHIP ${this.detailText(pitcher.seasonWhip)}`,
      `${this.detailText(pitcher.seasonStrikeOuts)} K`,
      `${this.detailText(pitcher.seasonInningsPitched)} IP`,
      `${this.detailText(pitcher.seasonSaves)} SV`
    ].join(' · ');
  }

  protected recordText(team: MlbTeamStanding): string {
    return `${team.wins}-${team.losses}`;
  }

  protected gamesBackText(value: string | null): string {
    return value === null || value === '' ? '-' : value;
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
  seasonWins: number | null;
  seasonLosses: number | null;
  seasonEra: string | null;
  seasonWhip: string | null;
  seasonStrikeOuts: number | null;
  seasonInningsPitched: string | null;
  seasonSaves: number | null;
  seasonGamesPitched: number | null;
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
  seasonAverage: string | null;
  seasonOnBasePercentage: string | null;
  seasonSluggingPercentage: string | null;
  seasonOps: string | null;
  seasonHomeRuns: number | null;
  seasonRbi: number | null;
  seasonHits: number | null;
  seasonStolenBases: number | null;
  summary: string | null;
}

interface MlbStandings {
  leagues: MlbLeagueStandings[];
  wildCards: MlbWildCardStandings[];
  lastUpdatedUtc: string | null;
}

interface MlbLeagueStandings {
  leagueId: number;
  leagueName: string;
  divisions: MlbDivisionStandings[];
}

interface MlbDivisionStandings {
  divisionId: number;
  divisionName: string;
  teams: MlbTeamStanding[];
}

interface MlbWildCardStandings {
  leagueId: number;
  leagueName: string;
  teams: MlbTeamStanding[];
}

interface MlbTeamStanding {
  teamId: number | null;
  teamName: string;
  wins: number;
  losses: number;
  winningPercentage: string | null;
  rank: string | null;
  gamesBack: string | null;
  wildCardGamesBack: string | null;
  streak: string | null;
  lastTen: string | null;
  runDifferential: number | null;
}

interface MlbStatLeaders {
  categories: MlbStatLeaderCategory[];
}

interface MlbStatLeaderCategory {
  categoryKey: string;
  label: string;
  statGroup: string;
  unit: string;
  leaders: MlbStatLeader[];
}

interface MlbStatLeader {
  rank: number;
  value: string;
  playerId: number | null;
  playerName: string;
  teamId: number | null;
  teamName: string;
  leagueName: string | null;
}

type SectionKey = 'leaders' | 'divisionStandings' | 'wildCard' | 'games';
