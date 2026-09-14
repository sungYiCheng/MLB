import { describe, expect, it } from 'vitest';
import {
  batterSeasonLine,
  battingLine,
  countText,
  detailText,
  formatTaipeiTime,
  gamesBackText,
  hasList,
  inningText,
  isLiveStatus,
  pitcherCountText,
  pitcherSeasonLine,
  pitcherText,
  playerHeadshotUrl,
  recordText,
  scoreText,
  seriesText,
  teamLogoUrl
} from './mlb-display';

describe('MLB display helpers', () => {
  it('identifies live game statuses', () => {
    expect(isLiveStatus('In Progress')).toBe(true);
    expect(isLiveStatus('Warmup')).toBe(true);
    expect(isLiveStatus('Final')).toBe(false);
  });

  it('formats Taipei game time and handles missing time', () => {
    expect(formatTaipeiTime(null)).toBe('Time TBD');
    expect(formatTaipeiTime('2026-09-14T00:05:00Z')).toContain('GMT+8');
  });

  it('formats score, detail, record, and games-back values', () => {
    expect(scoreText(null)).toBe('-');
    expect(scoreText(7)).toBe('7');
    expect(detailText('')).toBe('-');
    expect(detailText(12)).toBe('12');
    expect(recordText({ wins: 88, losses: 58 })).toBe('88-58');
    expect(gamesBackText(null)).toBe('-');
    expect(gamesBackText('1.5')).toBe('1.5');
  });

  it('formats game situation text', () => {
    const game = {
      seriesGameNumber: 2,
      gamesInSeries: 3,
      currentInningOrdinal: '7th',
      scheduledInnings: 9,
      inningHalf: 'Top',
      balls: 3,
      strikes: 2,
      outs: 1
    };

    expect(seriesText(game)).toBe('Game 2 of 3');
    expect(inningText(game)).toBe('Top 7th');
    expect(countText(game)).toBe('3-2, 1 out');
  });

  it('formats scheduled games without live inning or count data', () => {
    const game = {
      seriesGameNumber: null,
      gamesInSeries: null,
      currentInningOrdinal: null,
      scheduledInnings: 9,
      inningHalf: null,
      balls: null,
      strikes: null,
      outs: null
    };

    expect(seriesText(game)).toBe('-');
    expect(inningText(game)).toBe('9 innings');
    expect(countText(game)).toBe('-');
    expect(pitcherText(null)).toBe('TBD');
  });

  it('formats batter and pitcher stat lines', () => {
    const batter = {
      hits: 2,
      atBats: 4,
      rbi: 3,
      runs: 1,
      homeRuns: 1,
      walks: 0,
      strikeOuts: 1,
      seasonAverage: '.312',
      seasonOnBasePercentage: '.390',
      seasonSluggingPercentage: '.560',
      seasonOps: '.950',
      seasonHomeRuns: 35,
      seasonRbi: 102,
      seasonStolenBases: 12
    };

    const pitcher = {
      seasonWins: 14,
      seasonLosses: 6,
      seasonEra: '2.87',
      seasonWhip: '1.05',
      seasonStrikeOuts: 188,
      seasonInningsPitched: '172.1',
      seasonSaves: 0
    };

    expect(battingLine(batter)).toBe('2-4 · 3 RBI · 1 R · 1 HR · 0 BB · 1 K');
    expect(batterSeasonLine(batter)).toContain('OPS .950');
    expect(pitcherSeasonLine(pitcher)).toBe('14-6 · ERA 2.87 · WHIP 1.05 · 188 K · 172.1 IP · 0 SV');
  });

  it('formats list availability, pitcher count, and image urls', () => {
    expect(hasList([])).toBe(false);
    expect(hasList(['home run'])).toBe(true);
    expect(pitcherCountText(null)).toBe('0 pitchers used');
    expect(pitcherCountText([{}])).toBe('1 pitcher used');
    expect(teamLogoUrl(147)).toContain('/147.svg');
    expect(playerHeadshotUrl(660271)).toContain('/people/660271/headshot/');
  });
});
