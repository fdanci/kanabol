import {
  CompletedReportDto,
  CycleTimeReportDto,
  DeadlinesReportDto,
  WorkTimeReportDto,
} from './models';

/** Fixtures for the reports' specs, four weeks of October 2026. A plain file, so importing it runs no spec. */
export function aCompletedReport(extra: Partial<CompletedReportDto> = {}): CompletedReportDto {
  return {
    total: 7,
    byUser: [
      { userId: 'u2', name: 'Dan Ionescu', count: 5 },
      { userId: 'u1', name: 'Ana Pop', count: 2 },
      { userId: 'u3', name: 'Eva Marin', count: 1 },
    ],
    byProject: [
      { projectId: 'p1', key: 'KAN', name: 'Kanabol', count: 4 },
      { projectId: 'p2', key: 'WEB', name: 'Site', count: 3 },
    ],
    series: [
      { periodStart: '2026-10-05T00:00:00+03:00', count: 2 },
      { periodStart: '2026-10-12T00:00:00+03:00', count: 2 },
      { periodStart: '2026-10-19T00:00:00+03:00', count: 1 },
      { periodStart: '2026-10-26T00:00:00+03:00', count: 2 },
    ],
    ...extra,
  };
}

export function aCycleTimeReport(extra: Partial<CycleTimeReportDto> = {}): CycleTimeReportDto {
  return {
    count: 7,
    leadTime: { averageSeconds: 502_457, medianSeconds: 439_200 },
    cycleTime: { averageSeconds: 315_771, medianSeconds: 172_800 },
    perColumn: null,
    ...extra,
  };
}

export function aDeadlinesReport(extra: Partial<DeadlinesReportDto> = {}): DeadlinesReportDto {
  return {
    overdue: 2,
    completedLate: 2,
    completedOnTime: 6,
    onTimeRate: 0.75,
    series: [
      { periodStart: '2026-10-05T00:00:00+03:00', onTime: 1, late: 1 },
      { periodStart: '2026-10-12T00:00:00+03:00', onTime: 2, late: 1 },
      { periodStart: '2026-10-19T00:00:00+03:00', onTime: 0, late: 0 },
      { periodStart: '2026-10-26T00:00:00+03:00', onTime: 3, late: 0 },
    ],
    ...extra,
  };
}

/**
 * Dan: 2 tasks, 10 h estimated, 16 h worked; Ana: 1 task, 2 h and 1 h; KAN 2 tasks, WEB 1. KAN-1 over its
 * estimate by 6 h, WEB-3 by 30 min. All of it 12 h estimated against 17 h worked: +42 %.
 */
export function aWorkTimeReport(extra: Partial<WorkTimeReportDto> = {}): WorkTimeReportDto {
  return {
    count: 4,
    estimatedCount: 3,
    estimatedSeconds: 12 * 3600,
    workedSeconds: 17 * 3600,
    deviation: 5 / 12,
    overCount: 2,
    byUser: [
      {
        userId: 'u2',
        name: 'Dan Ionescu',
        count: 2,
        estimatedSeconds: 10 * 3600,
        workedSeconds: 16 * 3600,
      },
      { userId: 'u1', name: 'Ana Pop', count: 1, estimatedSeconds: 2 * 3600, workedSeconds: 3600 },
    ],
    byProject: [
      {
        projectId: 'p1',
        key: 'KAN',
        name: 'Kanabol',
        count: 2,
        estimatedSeconds: 10 * 3600,
        workedSeconds: 16 * 3600,
      },
      {
        projectId: 'p2',
        key: 'WEB',
        name: 'Site',
        count: 1,
        estimatedSeconds: 0,
        workedSeconds: 0,
      },
    ],
    over: [
      {
        taskId: 't1',
        key: 'KAN-1',
        title: 'Login SSO',
        estimatedSeconds: 4 * 3600,
        workedSeconds: 10 * 3600,
      },
      {
        taskId: 't3',
        key: 'WEB-3',
        title: 'Formular',
        estimatedSeconds: 3600,
        workedSeconds: 5400,
      },
    ],
    ...extra,
  };
}
