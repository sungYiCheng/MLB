export interface GameSummaryForDisplay {
  seriesGameNumber: number | null;
  gamesInSeries: number | null;
  currentInningOrdinal: string | null;
  scheduledInnings: number | null;
  inningHalf: string | null;
  balls: number | null;
  strikes: number | null;
  outs: number | null;
}

export interface BatterLineForDisplay {
  hits: number | null;
  atBats: number | null;
  rbi: number | null;
  runs: number | null;
  homeRuns: number | null;
  walks: number | null;
  strikeOuts: number | null;
  seasonAverage: string | null;
  seasonOnBasePercentage: string | null;
  seasonSluggingPercentage: string | null;
  seasonOps: string | null;
  seasonHomeRuns: number | null;
  seasonRbi: number | null;
  seasonStolenBases: number | null;
}

export interface PitcherLineForDisplay {
  seasonWins: number | null;
  seasonLosses: number | null;
  seasonEra: string | null;
  seasonWhip: string | null;
  seasonStrikeOuts: number | null;
  seasonInningsPitched: string | null;
  seasonSaves: number | null;
}

export interface TeamStandingForDisplay {
  wins: number;
  losses: number;
}

export function isLiveStatus(status: string): boolean {
  const normalizedStatus = status.toLowerCase();
  return normalizedStatus.includes('progress') || normalizedStatus.includes('warmup');
}

export function formatTaipeiTime(value: string | null): string {
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

export function scoreText(score: number | null): string {
  return score === null ? '-' : score.toString();
}

export function detailText(value: string | number | null | undefined): string {
  return value === null || value === undefined || value === '' ? '-' : value.toString();
}

export function seriesText(game: GameSummaryForDisplay): string {
  if (game.seriesGameNumber && game.gamesInSeries) {
    return `Game ${game.seriesGameNumber} of ${game.gamesInSeries}`;
  }

  return '-';
}

export function inningText(game: GameSummaryForDisplay): string {
  if (!game.currentInningOrdinal) {
    return game.scheduledInnings ? `${game.scheduledInnings} innings` : '-';
  }

  return game.inningHalf ? `${game.inningHalf} ${game.currentInningOrdinal}` : game.currentInningOrdinal;
}

export function countText(game: GameSummaryForDisplay): string {
  if (game.balls === null || game.strikes === null || game.outs === null) {
    return '-';
  }

  return `${game.balls}-${game.strikes}, ${game.outs} out${game.outs === 1 ? '' : 's'}`;
}

export function pitcherText(value: string | null): string {
  return value ?? 'TBD';
}

export function hasList(value: readonly unknown[] | null | undefined): boolean {
  return Boolean(value?.length);
}

export function pitcherCountText(pitchers: readonly unknown[] | null | undefined): string {
  const pitcherCount = pitchers?.length ?? 0;
  return `${pitcherCount} pitcher${pitcherCount === 1 ? '' : 's'} used`;
}

export function battingLine(batter: BatterLineForDisplay): string {
  return [
    `${detailText(batter.hits)}-${detailText(batter.atBats)}`,
    `${detailText(batter.rbi)} RBI`,
    `${detailText(batter.runs)} R`,
    `${detailText(batter.homeRuns)} HR`,
    `${detailText(batter.walks)} BB`,
    `${detailText(batter.strikeOuts)} K`
  ].join(' · ');
}

export function batterSeasonLine(batter: BatterLineForDisplay): string {
  return [
    `AVG ${detailText(batter.seasonAverage)}`,
    `OBP ${detailText(batter.seasonOnBasePercentage)}`,
    `SLG ${detailText(batter.seasonSluggingPercentage)}`,
    `OPS ${detailText(batter.seasonOps)}`,
    `${detailText(batter.seasonHomeRuns)} HR`,
    `${detailText(batter.seasonRbi)} RBI`,
    `${detailText(batter.seasonStolenBases)} SB`
  ].join(' · ');
}

export function pitcherSeasonLine(pitcher: PitcherLineForDisplay): string {
  return [
    `${detailText(pitcher.seasonWins)}-${detailText(pitcher.seasonLosses)}`,
    `ERA ${detailText(pitcher.seasonEra)}`,
    `WHIP ${detailText(pitcher.seasonWhip)}`,
    `${detailText(pitcher.seasonStrikeOuts)} K`,
    `${detailText(pitcher.seasonInningsPitched)} IP`,
    `${detailText(pitcher.seasonSaves)} SV`
  ].join(' · ');
}

export function recordText(team: TeamStandingForDisplay): string {
  return `${team.wins}-${team.losses}`;
}

export function gamesBackText(value: string | null): string {
  return value === null || value === '' ? '-' : value;
}

export function teamLogoUrl(teamId: number | null): string {
  return teamId
    ? `https://www.mlbstatic.com/team-logos/${teamId}.svg`
    : 'https://www.mlbstatic.com/team-logos/league-on-dark/1.svg';
}

export function playerHeadshotUrl(playerId: number | null): string {
  return playerId
    ? `https://img.mlbstatic.com/mlb-photos/image/upload/w_96,q_auto:best/v1/people/${playerId}/headshot/67/current`
    : 'https://www.mlbstatic.com/team-logos/league-on-dark/1.svg';
}
